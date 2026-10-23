using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class BatteryAlertRepeatTests
{
    private static readonly BatteryAlertReading Low = new(10, false, false, true);
    private static BatteryAlertSettings Settings(int interval = 30, int count = 3) =>
        new() { LowEnabled = true, LowRepeatSeconds = interval, LowWarningCount = count };
    private static void Begin(BatteryAlertPolicy policy, BatteryAlertSettings settings, object session = null)
    {
        Assert.IsNull(policy.Observe(Low, settings, 0, session));
        Assert.IsNull(policy.Observe(Low, settings, 1, session));
        Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(Low, settings, 2, session));
    }

    [TestMethod]
    public void LegacySettingsRemainSingleWarningAndNewSettingsRoundTrip()
    {
        var xml = new XmlSerializer(typeof(BatteryAlertSettings));
        var legacy = (BatteryAlertSettings)xml.Deserialize(new StringReader("<BatteryAlertSettings><LowEnabled>true</LowEnabled></BatteryAlertSettings>"));
        Assert.AreEqual(0, legacy.LowRepeatSeconds);
        var policy = new BatteryAlertPolicy(); Begin(policy, legacy);
        for (int t = 3; t < 3600; t++) Assert.IsNull(policy.Observe(Low, legacy, t));
        foreach (int interval in new[] { 0, 30, 60, 120, 300, 600 })
        foreach (int count in Enumerable.Range(0, 11))
        {
            var value = Settings(interval, count);
            using var writer = new StringWriter(); xml.Serialize(writer, value);
            var restored = (BatteryAlertSettings)xml.Deserialize(new StringReader(writer.ToString()));
            Assert.AreEqual(interval, restored.LowRepeatSeconds); Assert.AreEqual(count, restored.LowWarningCount);
            var clone = restored.Clone(); Assert.AreEqual(interval, clone.LowRepeatSeconds); Assert.AreEqual(count, clone.LowWarningCount);
        }
        var invalid = Settings(1, 500);
        Assert.AreEqual(0, invalid.LowRepeatSeconds); Assert.AreEqual(10, invalid.LowWarningCount);
        invalid.LowWarningCount = -1; Assert.AreEqual(0, invalid.LowWarningCount);
    }

    [TestMethod]
    [DataRow(30)] [DataRow(60)] [DataRow(120)] [DataRow(300)] [DataRow(600)]
    public void EveryIntervalHonoursTotalIncludingFirstWarning(int interval)
    {
        foreach (int total in Enumerable.Range(1, 10))
        {
            var settings = Settings(interval, total); var policy = new BatteryAlertPolicy(); Begin(policy, settings);
            int delivered = 1;
            for (int t = 3; t <= interval * 12 + 2; t++)
            {
                var kind = policy.Observe(Low, settings, t);
                if (kind.HasValue) { Assert.AreEqual(BatteryAlertKind.Low, kind); Assert.AreEqual(0, (t - 2) % interval); delivered++; }
            }
            Assert.AreEqual(total, delivered);
        }
    }

    [TestMethod]
    public void UntilChargingRepeatsCoarseReadingsWithoutCatchupBursts()
    {
        var settings = Settings(30, 0); var policy = new BatteryAlertPolicy();
        var reading = new BatteryAlertReading(25, false, false, true, "low");
        Assert.IsNull(policy.Observe(reading, settings, 0)); Assert.IsNull(policy.Observe(reading, settings, 1));
        Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(reading, settings, 2));
        for (int t = 32; t <= 602; t += 30) Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(reading, settings, t));
        Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(reading, settings, 10000));
        Assert.IsNull(policy.Observe(reading, settings, 10000));
        Assert.IsNull(policy.Observe(reading with { Charging = true }, settings, 10001));
        Assert.IsNull(policy.Observe(reading, settings, 10032));
    }

    [TestMethod]
    [DataRow("charging")] [DataRow("recovery")] [DataRow("unknown")]
    [DataRow("disabled")] [DataRow("disconnect")] [DataRow("replacement")]
    public void EndingCycleInvalidatesQueuedWarningsAndCannotResumeOnReconnect(string reason)
    {
        var settings = Settings(30, 0); var policy = new BatteryAlertPolicy(); var session = new object();
        Begin(policy, settings, session); long cycle = policy.LowCycle;
        Assert.IsTrue(policy.IsLowCycleCurrent(cycle));
        switch (reason)
        {
            case "charging": policy.Observe(Low with { Charging = true }, settings, 10, session); break;
            case "recovery": policy.Observe(Low with { Percentage = 21 }, settings, 10, session); break;
            case "unknown": policy.Observe(Low with { Reliable = false }, settings, 10, session); break;
            case "disabled": settings.LowEnabled = false; policy.Observe(Low, settings, 10, session); settings.LowEnabled = true; break;
            case "disconnect": policy.Pause(); break;
            case "replacement": session = new object(); policy.Observe(Low, settings, 10, session); break;
        }
        Assert.IsFalse(policy.IsLowCycleCurrent(cycle));
        for (int t = 11; t < 400; t++) Assert.IsNull(policy.Observe(Low, settings, t, session));
        for (int t = 400; t < 403; t++) policy.Observe(Low with { Percentage = 30 }, settings, t, session);
        Assert.IsNull(policy.Observe(Low, settings, 403, session)); Assert.IsNull(policy.Observe(Low, settings, 404, session));
        Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(Low, settings, 405, session));
        Assert.IsTrue(policy.IsLowCycleCurrent(policy.LowCycle)); Assert.AreNotEqual(cycle, policy.LowCycle);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ServicePreviewsDoNotConsumeQuotaAndEndedCyclesDiscardQueues(bool endQueuedCycle)
    {
        var previous = Global.BatteryAlerts;
        using var device = new Device();
        var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService)); hub.DS4Controllers = new DS4Device[8]; hub.DS4Controllers[0] = device;
        var clock = new Clock(); var service = new BatteryAlertService(hub, clock);
        var playbackGate = (SemaphoreSlim)typeof(BatteryAlertService).GetField("playbackGate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
        bool gateHeld = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(16));
        async Task Wait(Func<bool> condition) { while (!condition()) await Task.Delay(20, timeout.Token); }
        try
        {
            Global.BatteryAlerts = Settings(30, endQueuedCycle ? 0 : 2);
            Global.BatteryAlerts.Low.Mode = BatteryAlertMode.Off; Global.BatteryAlerts.Low.Vibrate = true;
            Global.BatteryAlerts.Low.Pulses = 1; Global.BatteryAlerts.Low.Duration = 50;
            await service.PreviewAsync(0, BatteryAlertKind.Low);
            Assert.AreEqual(1, device.Pulses);
            service.Start(); await Wait(() => device.Pulses == 2);
            clock.Seconds = 32; await Wait(() => device.Pulses == 3);
            if (!endQueuedCycle)
            {
                clock.Seconds = 400; await Task.Delay(1300, timeout.Token);
                Assert.AreEqual(3, device.Pulses, "Two automatic warnings plus one independent preview.");
                return;
            }
            await playbackGate.WaitAsync(timeout.Token); gateHeld = true;
            clock.Seconds = 62; await Task.Delay(1300, timeout.Token); // The next repeat is now queued behind playback.
            device.ChargingNow = true;
            await Task.Delay(1300, timeout.Token); // Let the monitor end this low episode.
            device.ChargingNow = false;
            playbackGate.Release(); gateHeld = false;
            clock.Seconds = 400; await Task.Delay(1300, timeout.Token);
            Assert.AreEqual(3, device.Pulses, "Queued and future reminders from the ended episode must not play.");
        }
        finally { service.Stop(); if (gateHeld) playbackGate.Release(); Global.BatteryAlerts = previous; }
    }

    private sealed class Clock : TimeProvider
    {
        internal long Seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Interlocked.Read(ref Seconds);
    }
    private sealed class Device : DS4Device, IDisposable
    {
        private int pulses;
        internal int Pulses => Volatile.Read(ref pulses);
        internal bool ChargingNow { set => charging = value; }
        internal Device() : base("Battery reminder fixture", InputDeviceType.DS4, ConnectionType.USB)
        { battery = 10; batteryReadingAvailable = true; Synced = true; FeatureSet = 0; }
        public override string ProfileLinkId => "repeat-test-controller";
        internal override IDisposable TryBeginBatteryAlertRumble(byte strength)
        {
            var lease = base.TryBeginBatteryAlertRumble(strength);
            if (lease != null) Interlocked.Increment(ref pulses);
            return lease;
        }
        public void Dispose() => ReadWaitEv.Dispose();
    }
}
