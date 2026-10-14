using DS4Windows.InputDevices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4Windows.Tests;

[TestClass]
public sealed class DualSenseProfileLightbarPolicyTests
{
    private static readonly DS4Color ProfileColor = new(11, 22, 233);

    [TestMethod]
    [DataRow(1)]
    [DataRow(13)]
    public void NormalModeOverridesNativeColourWithoutChangingOtherFeedback(int stateOffset)
    {
        byte[] report = NativeReport(stateOffset);
        byte[] before = (byte[])report.Clone();
        var state = State(ownsLightbar: true);
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, stateOffset, state);
        AssertColor(report, stateOffset, ProfileColor);
        Assert.AreEqual((byte)0x04, (byte)(report[stateOffset + 1] & 0x04));
        Assert.AreEqual(before[stateOffset + 43], report[stateOffset + 43], "Player LEDs have independent control.");
        var changed = new HashSet<int> { stateOffset + 1, stateOffset + 38,
            stateOffset + 41, stateOffset + 42, stateOffset + 44, stateOffset + 45, stateOffset + 46 };
        for (int i = 0; i < report.Length; i++)
            if (!changed.Contains(i)) Assert.AreEqual(before[i], report[i], $"Unrelated feedback byte {i} changed.");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(13)]
    public void PassThroughKeepsTheExactNativeFeedback(int stateOffset)
    {
        byte[] report = NativeReport(stateOffset);
        byte[] before = (byte[])report.Clone();
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, stateOffset, State(ownsLightbar: false));
        CollectionAssert.AreEqual(before, report);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(13)]
    public void NativeLedReleaseCannotResetNormalModeColour(int stateOffset)
    {
        byte[] report = NativeReport(stateOffset);
        report[stateOffset + 1] = 0x08;
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, stateOffset, State(ownsLightbar: true));
        Assert.AreEqual(0, report[stateOffset + 1] & 0x08);
        AssertColor(report, stateOffset, ProfileColor);
    }

    [TestMethod]
    public void UsbNativeCompositorRestoresProfileDespiteHeldGameOwnership()
    {
        byte[] raw = NativeReport(1);
        byte[] before = (byte[])raw.Clone();
        byte[] written = new byte[48];
        DualSenseDevice.PrepareUsbNativeGameReportWithLocalOverridesInto(raw, written, State(true));
        AssertColor(written, 1, ProfileColor);
        CollectionAssert.AreEqual(before, raw, "The original native underlay must remain intact.");
        CollectionAssert.AreEqual(raw.Skip(12).Take(22).ToArray(), written.Skip(12).Take(22).ToArray(),
            "Native adaptive trigger commands must remain exact.");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(13)]
    public void ReturningToNormalRestoresProfileWithoutANewGamePacket(int stateOffset)
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        mailbox.SetProfileLightbar(State(true).ProfileLightbar);
        mailbox.SetNativeGameLightbarOwnershipReleased(false);
        var raw = NativeReport(stateOffset);
        mailbox.ObserveDsxNativeState(raw, stateOffset);
        byte[] output = (byte[])raw.Clone();
        DualSenseDevice.ApplyDsxOverlayToNativeReport(output, stateOffset, mailbox.ReadLatest());
        AssertColor(output, stateOffset, new DS4Color(101, 102, 103));
        Assert.IsTrue(mailbox.SetProfileLightbarOwnership(true));
        output = (byte[])raw.Clone();
        DualSenseDevice.ApplyDsxOverlayToNativeReport(output, stateOffset, mailbox.ReadLatest());
        AssertColor(output, stateOffset, ProfileColor);
        Assert.IsFalse(mailbox.SetProfileLightbarOwnership(true), "Unchanged input reports must not dirty output.");
        Assert.IsTrue(mailbox.SetProfileLightbarOwnership(false));
        output = (byte[])raw.Clone();
        DualSenseDevice.ApplyDsxOverlayToNativeReport(output, stateOffset, mailbox.ReadLatest());
        AssertColor(output, stateOffset, new DS4Color(101, 102, 103));
    }

    [TestMethod]
    public void DsxReleaseRestoresNormalProfileInsteadOfCachedGameColour()
    {
        var state = State(true) with { DsxNativeState = new DualSenseDsxOverlay(null, null, null,
            new DS4Color(101, 102, 103), null, null) };
        byte[] report = NativeReport(1);
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, 1, state, DualSenseDsxOverlay.ColorField);
        AssertColor(report, 1, ProfileColor);
    }

    [TestMethod]
    public void ExplicitDsxColourKeepsItsExistingPriorityOverGameAndProfile()
    {
        var dsxColor = new DS4Color(9, 8, 7);
        var state = State(true) with { DsxOverlay = new DualSenseDsxOverlay(new object(), null, null,
            dsxColor, null, null) };
        byte[] report = NativeReport(1);
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, 1, state);
        AssertColor(report, 1, dsxColor);
    }

    private static DualSensePhysicalOutputSnapshot State(bool ownsLightbar) =>
        DualSensePhysicalOutputSnapshot.Default with
        {
            ProfileOwnsLightbar = ownsLightbar,
            ProfileLightbar = new DS4LightbarState { LightBarColor = ProfileColor },
            NativeGameLightbarOwnershipReleased = false,
        };

    private static byte[] NativeReport(int offset)
    {
        byte[] report = Enumerable.Range(0, offset + 47).Select(i => (byte)(i + 1)).ToArray();
        report[offset - 1] = 0x02;
        report[offset] = 0x0C;
        report[offset + 1] = 0x14;
        report[offset + 44] = 101;
        report[offset + 45] = 102;
        report[offset + 46] = 103;
        return report;
    }

    private static void AssertColor(byte[] report, int offset, DS4Color expected)
    {
        Assert.AreEqual(expected.red, report[offset + 44]);
        Assert.AreEqual(expected.green, report[offset + 45]);
        Assert.AreEqual(expected.blue, report[offset + 46]);
    }
}
