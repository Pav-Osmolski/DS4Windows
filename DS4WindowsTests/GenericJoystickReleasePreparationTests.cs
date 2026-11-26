using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class GenericJoystickReleasePreparationTests
{
    [TestMethod]
    public void UnboundNormalOutputFailsBeforeStartingBackendAndPreservesSavedSettings()
    {
        Assert.IsFalse(PortableLabContext.IsActive);
        const int index = Global.TEST_PROFILE_INDEX;
        var priorSettings = Global.GenericJoystickSettings[index];
        var priorType = Global.OutContType[index];
        var settings = new GenericJoystickProfileSettings
        {
            Options = GenericJoystickOptions.Default with
            {
                Layout = GenericJoystickLayout.Racing,
                TriggerMode = GenericJoystickTriggerMode.FullRange,
                InvertLeftTrigger = true
            }
        };
        var output = new ViiperOutDevice(OutContType.ViiperGenericJoystick,
            ViiperVirtualDeviceType.GenericJoystick);
        try
        {
            Global.GenericJoystickSettings[index] = settings;
            Global.OutContType[index] = OutContType.ViiperGenericJoystick;
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => output.Connect());
            StringAssert.Contains(error.Message, "Bind a physical controller");
            Assert.IsFalse(output.IsRuntimeConnected);
            Assert.AreSame(settings, Global.GenericJoystickSettings[index]);
            Assert.IsTrue(settings.Valid);
            Assert.AreEqual(GenericJoystickTriggerMode.FullRange, settings.Options.TriggerMode);
            Assert.IsTrue(settings.Options.InvertLeftTrigger);
            Assert.AreEqual(OutContType.ViiperGenericJoystick, Global.OutContType[index]);
        }
        finally
        {
            output.Disconnect();
            Global.GenericJoystickSettings[index] = priorSettings;
            Global.OutContType[index] = priorType;
        }
    }
}
