using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class BatteryControllerCompatibilityTests
{
    [TestMethod]
    [DataRow(InputDeviceType.DS4, ConnectionType.USB, 10, false)]
    [DataRow(InputDeviceType.DS4, ConnectionType.BT, 10, false)]
    [DataRow(InputDeviceType.DS3, ConnectionType.USB, 10, true)]
    [DataRow(InputDeviceType.DS3, ConnectionType.BT, 25, true)]
    [DataRow(InputDeviceType.JoyConL, ConnectionType.BT, 25, true)]
    [DataRow(InputDeviceType.JoyConR, ConnectionType.BT, 25, true)]
    [DataRow(InputDeviceType.SwitchPro, ConnectionType.USB, 25, true)]
    [DataRow(InputDeviceType.SwitchPro, ConnectionType.BT, 25, true)]
    [DataRow(InputDeviceType.DualSense, ConnectionType.USB, 10, false)]
    [DataRow(InputDeviceType.DualSense, ConnectionType.BT, 10, false)]
    public void ReportedLowBatteryReachesPolicyWithoutInventingPrecision(InputDeviceType type, ConnectionType connection, int level, bool coarse)
    {
        using var device = new ReportedDevice(type, connection, "physical-1");
        device.Reading(level);
        var reading = BatteryAlertService.Read(device);
        Assert.IsTrue(reading.Reliable);
        string message = BatteryAlertService.Message(0, BatteryAlertKind.Check, reading);
        if (coarse)
        {
            Assert.AreEqual("low", reading.Level);
            Assert.IsFalse(message.Contains("percent"), "A categorical report is not a precise state-of-charge measurement.");
        }
        else Assert.IsTrue(message.Contains($"{level} percent"));
        var settings = new BatteryAlertSettings { LowEnabled = true };
        var policy = new BatteryAlertPolicy();
        Assert.IsNull(policy.Observe(reading, settings, 0));
        Assert.IsNull(policy.Observe(reading, settings, 1));
        Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(reading, settings, 2));
        for (int time = 3; time < 20; time++) Assert.IsNull(policy.Observe(reading, settings, time));
        device.Reading(coarse ? 50 : 30);
        for (int time = 20; time < 23; time++) policy.Observe(BatteryAlertService.Read(device), settings, time);
        device.Reading(level);
        Assert.IsNull(policy.Observe(BatteryAlertService.Read(device), settings, 301));
        Assert.IsNull(policy.Observe(BatteryAlertService.Read(device), settings, 302));
        Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(BatteryAlertService.Read(device), settings, 303));
    }

    [TestMethod]
    [DataRow(InputDeviceType.DS4)]
    [DataRow(InputDeviceType.DS3)]
    [DataRow(InputDeviceType.SwitchPro)]
    [DataRow(InputDeviceType.JoyConL)]
    [DataRow(InputDeviceType.JoyConR)]
    [DataRow(InputDeviceType.DualSense)]
    public void UnknownCloneReadingsAndChargingDoNotCauseFalseLowAlerts(InputDeviceType type)
    {
        using var device = new ReportedDevice(type, ConnectionType.BT, "clone-1");
        device.Reading(0); device.FeatureSet = VidPidFeatureSet.NoBatteryReading;
        var settings = new BatteryAlertSettings { LowEnabled = true, FullEnabled = true };
        var policy = new BatteryAlertPolicy();
        for (int time = 0; time < 6; time++) Assert.IsNull(policy.Observe(BatteryAlertService.Read(device), settings, time));
        Assert.IsFalse(BatteryAlertService.Read(device).Reliable);
        Assert.AreEqual("Controller 1: battery information unavailable.", BatteryAlertService.Message(0, BatteryAlertKind.Check, BatteryAlertService.Read(device)));
        device.FeatureSet = 0; device.Reading(10, charging: true);
        for (int time = 6; time < 12; time++) Assert.IsNull(policy.Observe(BatteryAlertService.Read(device), settings, time));
        device.Reading(100, charging: true, full: false);
        for (int time = 12; time < 18; time++) Assert.IsNull(policy.Observe(BatteryAlertService.Read(device), settings, time));
    }

    [TestMethod]
    [DataRow(InputDeviceType.DS3)]
    [DataRow(InputDeviceType.SwitchPro)]
    [DataRow(InputDeviceType.JoyConL)]
    [DataRow(InputDeviceType.JoyConR)]
    public async Task UnsupportedBuiltInSpeakersRejectBeforeSynthesisOrPlayback(InputDeviceType type)
    {
        using var device = new ReportedDevice(type, ConnectionType.BT, "physical-1");
        var hub = Hub(device);
        var previous = Global.BatteryAlerts;
        try
        {
            Global.BatteryAlerts = new BatteryAlertSettings { Endpoint = "controller" };
            var messages = new List<string>(); hub.BatteryAlerts.StatusChanged += messages.Add;
            await hub.BatteryAlerts.PreviewAsync(0, BatteryAlertKind.Check);
            CollectionAssert.AreEqual(new[] { "Battery alert: This controller does not have a supported built-in speaker route. Select a headset or Windows audio device." }, messages);
            Assert.AreEqual(0, device.Pulses);
        }
        finally { hub.BatteryAlerts.Stop(); Global.BatteryAlerts = previous; }
    }

    [TestMethod]
    public async Task AutomaticAlertsServeMultipleFamiliesAndPreserveReconnectSuppression()
    {
        var devices = new[] {
            new ReportedDevice(InputDeviceType.DS4, ConnectionType.USB, "ds4"),
            new ReportedDevice(InputDeviceType.DS3, ConnectionType.BT, "ds3"),
            new ReportedDevice(InputDeviceType.JoyConL, ConnectionType.BT, "joy-l"),
            new ReportedDevice(InputDeviceType.JoyConR, ConnectionType.BT, "joy-r"),
            new ReportedDevice(InputDeviceType.SwitchPro, ConnectionType.BT, "pro"),
            new ReportedDevice(InputDeviceType.DualSense, ConnectionType.BT, "dualsense"),
            new ReportedDevice(InputDeviceType.DS4, ConnectionType.BT, "clone"),
            new ReportedDevice(InputDeviceType.DS4, ConnectionType.USB, "charging") };
        for (int i = 0; i < devices.Length; i++) devices[i].Reading(i is >= 2 and <= 4 ? 25 : 10, charging: i == 7);
        devices[6].FeatureSet = VidPidFeatureSet.NoBatteryReading;
        var hub = Hub(devices);
        var previous = Global.BatteryAlerts;
        using var reconnected = new ReportedDevice(InputDeviceType.JoyConL, ConnectionType.BT, "joy-l");
        try
        {
            var settings = new BatteryAlertSettings { LowEnabled = true };
            settings.Low.Mode = BatteryAlertMode.Off; settings.Low.Vibrate = true;
            settings.Low.Duration = 50; settings.Low.Pulses = 1;
            Global.BatteryAlerts = settings;
            hub.BatteryAlerts.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (devices.Take(6).Any(d => d.Pulses == 0)) await Task.Delay(20, deadline.Token);
            Assert.IsTrue(devices.Take(6).All(d => d.Pulses == 1));
            Assert.AreEqual(0, devices[6].Pulses); Assert.AreEqual(0, devices[7].Pulses);
            hub.BatteryAlerts.Stop();
            reconnected.Reading(25); hub.DS4Controllers[2] = reconnected;
            hub.BatteryAlerts.Start();
            await Task.Delay(3400);
            Assert.AreEqual(0, reconnected.Pulses, "A replacement device object with the same physical identity must retain its low-alert latch.");
            Assert.IsTrue(devices.Take(6).All(d => d.Pulses == 1));
        }
        finally
        {
            hub.BatteryAlerts.Stop();
            Global.BatteryAlerts = previous;
            foreach (var device in devices) device.Dispose();
        }
    }

    private static ControlService Hub(params DS4Device[] devices)
    {
        var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        hub.DS4Controllers = new DS4Device[8];
        Array.Copy(devices, hub.DS4Controllers, devices.Length);
        return hub;
    }

    private sealed class ReportedDevice : DS4Device, IDisposable
    {
        private readonly string identity;
        private bool full;
        private int pulses;
        internal int Pulses => Volatile.Read(ref pulses);
        internal ReportedDevice(InputDeviceType type, ConnectionType connection, string identity) : base("Synthetic battery report", type, connection)
        { this.identity = identity; Synced = true; FeatureSet = 0; }
        public override string ProfileLinkId => identity;
        public override bool BatteryChargeComplete => full;
        internal void Reading(int level, bool charging = false, bool full = false)
        { battery = level; this.charging = charging; this.full = full; batteryReadingAvailable = true; }
        internal override IDisposable TryBeginBatteryAlertRumble(byte strength)
        {
            var lease = base.TryBeginBatteryAlertRumble(strength);
            if (lease != null) Interlocked.Increment(ref pulses);
            return lease;
        }
        public void Dispose() => ReadWaitEv.Dispose();
    }
}
