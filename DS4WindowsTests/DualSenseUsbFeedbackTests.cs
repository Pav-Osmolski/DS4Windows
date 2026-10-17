using DS4Windows.InputDevices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4Windows.Tests;

[TestClass]
public sealed class DualSenseUsbFeedbackTests
{
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void EditorPreviewOverridesNativeUsbMotorModeAndRestoresItOnRelease(
        bool light, bool heavy)
    {
        byte[] native = new byte[48];
        native[0] = 2;
        native[1] = 0x0C; // Audio haptics; independently valid trigger effects.
        native[3] = 31;
        native[4] = 47;
        native[11] = 0x21;
        native[22] = 0x26;
        byte[] before = (byte[])native.Clone();
        var state = DualSensePhysicalOutputSnapshot.Default with
        {
            NativeGameLightbarOwnershipReleased = false,
            PreviewLightRumbleActive = light,
            PreviewLightRumbleStrength = 123,
            PreviewHeavyRumbleActive = heavy,
            PreviewHeavyRumbleStrength = 234,
        };
        byte[] result = new byte[48];
        DualSenseDevice.PrepareUsbNativeGameReportWithLocalOverridesInto(
            native, result, state);
        Assert.AreEqual(3, result[1] & 3,
            "The editor must select compatibility rumble even after a native audio-mode report.");
        Assert.AreEqual(light ? (byte)123 : (byte)0, result[3]);
        Assert.AreEqual(heavy ? (byte)234 : (byte)0, result[4]);
        CollectionAssert.AreEqual(before[11..33], result[11..33],
            "Testing the main motors must not change adaptive triggers.");
        CollectionAssert.AreEqual(before, native, "The game's underlay must remain immutable.");

        DualSenseDevice.PrepareUsbNativeGameReportWithLocalOverridesInto(
            native, result, state with
            { PreviewLightRumbleActive = false, PreviewHeavyRumbleActive = false });
        Assert.AreEqual(0, result[1] & 3, "Stopping the preview must restore native audio mode.");
        Assert.AreEqual(native[3], result[3]);
        Assert.AreEqual(native[4], result[4]);
    }
}
