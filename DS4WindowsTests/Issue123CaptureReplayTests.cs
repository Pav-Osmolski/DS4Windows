using DS4Windows;

namespace DS4WindowsTests;

// Diagnostic reproduction from #123's captured trigger bytes. No HID handles,
// game, controller or driver configuration is used by these tests.
[TestClass]
public sealed class Issue123CaptureReplayTests
{
    [TestMethod]
    public void CapturedBowModeCannotUseExistingRepeatSuppression()
    {
        var captured = Report(0x22, 0x0C);
        Assert.IsFalse(DualSenseNativeFeedbackRepeatPolicy.IsRepeatableReport(captured.AsSpan(28, 48)));
        var ordinary = Report(0x21, 0x0C);
        Assert.IsTrue(DualSenseNativeFeedbackRepeatPolicy.IsRepeatableReport(ordinary.AsSpan(28, 48)));
    }

    [TestMethod]
    public void RepeatedCapturedBowStateFillsQueueAndDelaysExplicitRelease()
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] effect = Report(0x22, 0x0C);
        for (int i = 0; i < 64; i++) Assert.IsTrue(Enqueue(buffer, effect, context));
        Assert.AreEqual(64, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlRepeated);
        Assert.IsFalse(Enqueue(buffer, Report(0x05, 0x0C), context));
    }

    [TestMethod]
    public void AlternatingSidesAlsoDefeatExactReportSuppressionForRecognizedModes()
    {
        var buffer = Buffer();
        var context = Context(buffer);
        // Recognized mode deliberately isolates the mask issue from 0x22.
        for (int i = 0; i < 64; i++)
            Assert.IsTrue(Enqueue(buffer, Report(0x21, i % 2 == 0 ? (byte)4 : (byte)8), context));
        Assert.AreEqual(64, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlRepeated);
        Assert.IsFalse(Enqueue(buffer, Report(0x05, 0x0C), context));
    }

    private static ViiperFeedbackDispatchBuffer Buffer() => new(
        speakerCapacity: 4, speakerSlotLength: 474, controlSlotLength: 76,
        orderedControlCapacity: 64, speakerMaximumAgeMilliseconds: 2000);
    private static ViiperNativeCommandContext Context(ViiperFeedbackDispatchBuffer buffer) =>
        new(new object(), 1, 1, buffer.PendingBoundaryRevision);
    private static bool Enqueue(ViiperFeedbackDispatchBuffer buffer, byte[] payload, ViiperNativeCommandContext context) =>
        buffer.TryEnqueueOrderedControl(payload, payload.Length, 17, 0, nativeCommand: true,
            expectedBoundaryRevision: context.PendingBoundaryRevision, nativeContext: context);

    private static byte[] Report(byte mode, byte mask)
    {
        byte[] payload = new byte[76];
        payload[28] = 2; payload[29] = mask;
        if ((mask & 4) != 0)
        {
            payload[39] = mode;
            if (mode != 5) { payload[40] = 0x44; payload[42] = 0x29; }
        }
        if ((mask & 8) != 0)
        {
            payload[50] = mode;
            if (mode != 5) { payload[51] = 2; payload[52] = 2; payload[53] = 0x3C; }
        }
        return payload;
    }
}
