using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class GenericJoystickIdentityTests
{
    [TestMethod]
    public void IdentityUsesTheControllerRatherThanProfileSlotOrConnectionOrder()
    {
        string first = GenericJoystickIdentity.Create("AA:BB:CC:DD:EE:01", "port-one");
        Assert.AreEqual(first, GenericJoystickIdentity.Create("aa-bb-cc-dd-ee-01", "port-two"));
        Assert.AreNotEqual(first, GenericJoystickIdentity.Create("AA:BB:CC:DD:EE:02", "port-one"));
        Assert.AreEqual(32, first.Length);
        Assert.IsTrue(first.All(Uri.IsHexDigit));
        Assert.IsFalse(first.Contains("AABBCCDDEE01"));
        Assert.AreEqual(GenericJoystickIdentity.Create(null, "device-path"),
            GenericJoystickIdentity.Create(DS4Device.BLANK_SERIAL, "DEVICE-PATH"));
        Assert.AreNotEqual(GenericJoystickIdentity.Create(null, "path-one"), GenericJoystickIdentity.Create(null, "path-two"));
        Assert.IsNull(GenericJoystickIdentity.Create(null, null));
    }

    [TestMethod]
    public void ReorderedPhysicalControllersCannotAdoptEachOthersOutput()
    {
        var oldHub = Program.rootHub;
        var oldFirst = Global.GenericJoystickSettings[0];
        var oldSecond = Global.GenericJoystickSettings[1];
        var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        var first = Device("AA:BB:CC:DD:EE:01");
        var second = Device("AA:BB:CC:DD:EE:02");
        var outputA = new ViiperOutDevice(OutContType.ViiperGenericJoystick, ViiperVirtualDeviceType.GenericJoystick);
        var outputB = new ViiperOutDevice(OutContType.ViiperGenericJoystick, ViiperVirtualDeviceType.GenericJoystick);
        try
        {
            Global.GenericJoystickSettings[0] = new();
            Global.GenericJoystickSettings[1] = new(); // Same profile options do not imply same device.
            hub.DS4Controllers = new DS4Device[] { first, second };
            Program.rootHub = hub;
            outputA.BindPhysicalController(0);
            outputB.BindPhysicalController(1);
            Assert.IsFalse(outputA.CanReuseForPhysicalController(1));
            Assert.IsFalse(outputB.CanReuseForPhysicalController(0));
            hub.DS4Controllers = new DS4Device[] { second, first };
            Assert.IsTrue(outputA.CanReuseForPhysicalController(1));
            Assert.IsTrue(outputB.CanReuseForPhysicalController(0));
            Assert.IsFalse(outputA.CanReuseForPhysicalController(0));
            // A new physical object representing the same controller retains the identity.
            hub.DS4Controllers[1] = Device("aa-bb-cc-dd-ee-01");
            Assert.IsTrue(outputA.CanReuseForPhysicalController(1));
            hub.DS4Controllers[1] = Device("AA:BB:CC:DD:EE:02");
            Assert.ThrowsExactly<InvalidOperationException>(() => outputA.BindPhysicalController(1));
        }
        finally
        {
            outputA.Disconnect(); outputB.Disconnect();
            Program.rootHub = oldHub;
            Global.GenericJoystickSettings[0] = oldFirst;
            Global.GenericJoystickSettings[1] = oldSecond;
        }
    }

    [TestMethod]
    public void BackendMustEchoTheRequestedInstanceBeforeInputAdmission()
    {
        const string identity = "0123456789ABCDEF0123456789ABCDEF";
        const string response = "{\"contract\":\"genericjoystick-v1\",\"version\":1,\"experimental\":true,\"buttonCount\":16,\"neutralAxes\":[0,0,0,0,0,0],\"inputConfigured\":true,\"instanceId\":\"0123456789ABCDEF0123456789ABCDEF\"}";
        using var matching = JsonDocument.Parse(response);
        Assert.IsTrue(GenericJoystickContract.ReadConfigured(matching.RootElement, new short[6], identity));
        Assert.ThrowsExactly<IOException>(() => GenericJoystickContract.ReadConfigured(matching.RootElement, new short[6], new string('F', 32)));
        using var old = JsonDocument.Parse(response.Replace(",\"instanceId\":\"" + identity + "\"", ""));
        Assert.ThrowsExactly<IOException>(() => GenericJoystickContract.ReadConfigured(old.RootElement, new short[6], identity));
    }

    private static IdentifiedDevice Device(string identity)
    {
        var device = (IdentifiedDevice)RuntimeHelpers.GetUninitializedObject(typeof(IdentifiedDevice));
        device.Identity = identity;
        return device;
    }

    private sealed class IdentifiedDevice : DS4Device
    {
        private IdentifiedDevice() : base(null, "test") { }
        public string Identity;
        public override string ProfileLinkId => Identity;
    }
}

