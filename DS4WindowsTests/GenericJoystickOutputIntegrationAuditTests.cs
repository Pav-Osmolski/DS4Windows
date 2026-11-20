using System.Text.Json;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class GenericJoystickOutputIntegrationAuditTests
{
    [TestMethod]
    public void EveryMappingTargetHasAReadableJoystickLabel()
    {
        foreach (var control in Enum.GetValues<X360Controls>())
            Assert.IsFalse(string.IsNullOrWhiteSpace(Global.getX360ControlString(
                control, OutContType.ViiperGenericJoystick)), control.ToString());

        Assert.AreEqual("Button 1 (Cross)", Global.getX360ControlString(X360Controls.A, OutContType.ViiperGenericJoystick));
        Assert.AreEqual("Button 3 (Square)", Global.getX360ControlString(X360Controls.X, OutContType.ViiperGenericJoystick));
        Assert.AreEqual("Button 12 (R3)", Global.getX360ControlString(X360Controls.RS, OutContType.ViiperGenericJoystick));
        StringAssert.Contains(Global.getX360ControlString(X360Controls.Guide, OutContType.ViiperGenericJoystick), "Button 13");
        StringAssert.Contains(Global.getX360ControlString(X360Controls.TouchpadClick, OutContType.ViiperGenericJoystick), "Button 14");
        StringAssert.Contains(Global.getX360ControlString(X360Controls.LT, OutContType.ViiperGenericJoystick), "profile trigger mode");
        // A mouse binding must remain a mouse binding when the output changes.
        Assert.AreEqual(Global.xboxDefaultNames[X360Controls.LeftMouse], Global.getX360ControlString(X360Controls.LeftMouse, OutContType.ViiperGenericJoystick));
        Assert.AreEqual(Global.xboxDefaultNames[X360Controls.A], Global.getX360ControlString(X360Controls.A, OutContType.ViiperX360));
        Assert.AreEqual(Global.ds4DefaultNames[X360Controls.A], Global.getX360ControlString(X360Controls.A, OutContType.ViiperDS4));
    }

    [TestMethod]
    public void OldOrUnmatchedBackendCannotAdmitJoystickInput()
    {
        foreach (string json in new[] {
            "null", "{}", "{\"version\":1,\"buttonCount\":12}",
            "{\"contract\":\"genericjoystick-v2\",\"version\":2,\"experimental\":true,\"buttonCount\":12,\"neutralAxes\":[0,0,0,0,0,0],\"inputConfigured\":true}"
        })
        {
            using var document = JsonDocument.Parse(json);
            var error = Assert.ThrowsExactly<IOException>(() => GenericJoystickContract.ReadConfigured(document.RootElement, new short[6]));
            StringAssert.Contains(error.Message, "matched DirectInput preview package");
        }
    }

    [TestMethod]
    public void ChangedJoystickProfileCannotReuseThePriorImmutableOutput()
    {
        const int slot = 8;
        var previous = Global.GenericJoystickSettings[slot];
        var output = new ViiperOutDevice(OutContType.ViiperGenericJoystick, ViiperVirtualDeviceType.GenericJoystick);
        try
        {
            var settings = Global.GenericJoystickSettings[slot] = new GenericJoystickProfileSettings();
            output.BindPhysicalController(slot);
            Assert.IsTrue(output.CanReuseForPhysicalController(slot));
            foreach (var options in new[] {
                GenericJoystickOptions.Default with { Layout = GenericJoystickLayout.Racing },
                GenericJoystickOptions.Default with { TriggerMode = GenericJoystickTriggerMode.FullRange },
                GenericJoystickOptions.Default with { ButtonThreshold = 200 },
                GenericJoystickOptions.Default with { InvertLeftTrigger = true }
            })
            {
                settings.Options = options;
                Assert.IsFalse(output.CanReuseForPhysicalController(slot), options.ToString());
            }
            settings.Options = GenericJoystickOptions.Default;
            settings.Valid = false;
            Assert.IsFalse(output.CanReuseForPhysicalController(slot));
            Assert.ThrowsExactly<InvalidOperationException>(() => output.BindPhysicalController(slot));
            Assert.IsTrue(ViiperOutDevice.IsViiperType(OutContType.ViiperGenericJoystick),
                "The output must participate in the normal managed HidHide path.");
        }
        finally
        {
            output.Disconnect();
            Global.GenericJoystickSettings[slot] = previous;
        }
    }
}
