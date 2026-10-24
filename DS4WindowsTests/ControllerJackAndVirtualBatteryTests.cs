using System.Text.Json;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public sealed class ControllerJackAndVirtualBatteryTests
{
    [TestMethod]
    [DataRow(false, false, false, false)]
    [DataRow(false, false, true, false)]
    [DataRow(true, false, false, true)]
    [DataRow(true, false, true, true)]
    [DataRow(false, true, false, false)]
    [DataRow(false, true, true, true)]
    [DataRow(true, true, false, false)]
    [DataRow(true, true, true, true)]
    public void FollowJackDoesNotChangeManualProfiles(bool manual, bool follow, bool plugged, bool expected) =>
        Assert.AreEqual(expected, ControllerAudioRoutingPolicy.UseHeadset(manual, follow, plugged));

    [TestMethod]
    [DataRow(0)] [DataRow(1)]
    public void DualSenseHeadphoneBitUsesTransportNormalizedStatus(int prefix)
    {
        byte[] report = new byte[64 + prefix];
        report[54 + prefix] = 0x01;
        Assert.IsTrue(DualSenseDevice.TryExtractPhysicalInputStatus(report, prefix, out var status));
        Assert.IsTrue(ControllerAudioRoutingPolicy.HeadphoneDetected(status.ConnectionStatus));
        report[54 + prefix] = 0x0a; // USB and microphone bits alone are not headphones.
        Assert.IsTrue(DualSenseDevice.TryExtractPhysicalInputStatus(report, prefix, out status));
        Assert.IsFalse(ControllerAudioRoutingPolicy.HeadphoneDetected(status.ConnectionStatus));
    }

    [TestMethod]
    [DataRow(0x00, false)] [DataRow(0x10, false)] [DataRow(0x40, false)]
    [DataRow(0x20, true)] [DataRow(0x30, true)] [DataRow(0x60, true)]
    public void DualShock4HeadphoneBitIgnoresCableAndMicrophone(int status, bool connected) =>
        Assert.AreEqual(connected, ControllerAudioRoutingPolicy.DualShock4HeadphoneDetected((byte)status));

    [TestMethod]
    [DataRow(ConnectionType.BT, 0, false, false, false)]
    [DataRow(ConnectionType.BT, 60, false, false, false)]
    [DataRow(ConnectionType.USB, 60, true, false, true)]
    [DataRow(ConnectionType.USB, 100, true, true, true)]
    public void VirtualBatterySnapshotPreservesPowerAndCompletion(ConnectionType connection,
        int percent, bool charging, bool complete, bool powered)
    {
        using var d = new SyntheticDevice(connection);
        d.Read(percent, charging, complete);
        Assert.IsTrue(ViiperBatteryStatus.TryRead(d, out var s));
        Assert.AreEqual(percent, s.Percent);
        Assert.AreEqual(powered, s.ExternalPower);
        Assert.AreEqual(complete, s.Complete);
        Assert.AreEqual(charging && !complete, s.Charging);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(s));
        Assert.AreEqual(1, json.RootElement.GetProperty("version").GetInt32());
        Assert.AreEqual(percent, json.RootElement.GetProperty("percent").GetInt32());
    }

    [TestMethod]
    public void FollowJackRoundTripsAndLegacyProfilesStayManual()
    {
        var serializer = new System.Xml.Serialization.XmlSerializer(
            typeof(DS4WinWPF.DS4Control.DTOXml.DualSenseControllerSettings.AudioSettings));
        using var oldXml = new StringReader("<AudioSettings><HeadsetPluggedIn>true</HeadsetPluggedIn></AudioSettings>");
        var legacy = (DS4WinWPF.DS4Control.DTOXml.DualSenseControllerSettings.AudioSettings)serializer.Deserialize(oldXml);
        Assert.IsTrue(legacy.HeadsetOnlyAudio);
        Assert.IsFalse(legacy.FollowHeadphoneJack);
        legacy.FollowHeadphoneJack = true;
        using var writer = new StringWriter();
        serializer.Serialize(writer, legacy);
        using var reader = new StringReader(writer.ToString());
        var restored = (DS4WinWPF.DS4Control.DTOXml.DualSenseControllerSettings.AudioSettings)serializer.Deserialize(reader);
        Assert.IsTrue(restored.FollowHeadphoneJack);
    }

    [TestMethod]
    public void UnavailableBatteryIsNotReportedAsFull()
    {
        using var d = new SyntheticDevice(ConnectionType.BT);
        d.Read(100, false, false);
        d.FeatureSet = VidPidFeatureSet.NoBatteryReading;
        Assert.IsFalse(ViiperBatteryStatus.TryRead(d, out _));
        Assert.IsFalse(ViiperBatteryStatus.TryRead(null, out _));
    }

    [TestMethod]
    public async Task BatteryWorkerCoalescesReadingsAndRebindsToReplacement()
    {
        using var first = new SyntheticDevice(ConnectionType.BT);
        using var second = new SyntheticDevice(ConnectionType.BT);
        first.Read(50, false, false);
        second.Read(50, false, false);
        DS4Device current = first;
        int delivered = 0;
        using var bridge = new ViiperBatteryStatusBridge(() => Volatile.Read(ref current),
            _ => Interlocked.Increment(ref delivered), ex => Assert.Fail(ex.Message), TimeSpan.FromMilliseconds(10));
        await WaitFor(() => Volatile.Read(ref delivered) == 1);
        await Task.Delay(60);
        Assert.AreEqual(1, Volatile.Read(ref delivered));
        Volatile.Write(ref current, second);
        await WaitFor(() => Volatile.Read(ref delivered) == 2);
        second.Read(20, false, false);
        await WaitFor(() => Volatile.Read(ref delivered) == 3);
        bridge.Dispose();
        second.Read(10, false, false);
        await Task.Delay(40);
        Assert.AreEqual(3, Volatile.Read(ref delivered));
    }

    [TestMethod]
    public async Task BatteryWorkerRetriesFailuresAndJoinsBeforeStreamRetirement()
    {
        using var device = new SyntheticDevice(ConnectionType.BT);
        device.Read(40, false, false);
        int attempts = 0, errors = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var bridge = new ViiperBatteryStatusBridge(() => device, _ =>
        {
            if (Interlocked.Increment(ref attempts) <= 2) throw new IOException("synthetic transport failure");
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(3));
        }, _ => Interlocked.Increment(ref errors), TimeSpan.FromMilliseconds(10));
        try
        {
            await WaitFor(() => entered.IsSet);
            Assert.AreEqual(1, Volatile.Read(ref errors), "Repeated failures should log once.");
            Task retiring = Task.Run(bridge.Dispose);
            await Task.Delay(30);
            Assert.IsFalse(retiring.IsCompleted, "Retirement must wait for the last publish.");
            release.Set();
            await retiring.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(3, Volatile.Read(ref attempts));
        }
        finally { release.Set(); bridge.Dispose(); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.IsTrue(condition(), "Timed out waiting for the status worker.");
    }

    private sealed class SyntheticDevice(ConnectionType type) : DS4Device("Synthetic", InputDeviceType.DualSense, type), IDisposable
    {
        private bool full;
        public override bool BatteryChargeComplete => full;
        internal void Read(int percent, bool powered, bool complete)
        { FeatureSet = 0; battery = percent; charging = powered; full = complete; batteryReadingAvailable = true; }
        public void Dispose() => ReadWaitEv.Dispose();
    }
}
