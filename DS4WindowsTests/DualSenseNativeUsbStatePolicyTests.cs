using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class DualSenseNativeUsbStatePolicyTests
{
    [TestMethod]
    [DataRow(76, 3)]
    [DataRow(217, 3)]
    [DataRow(474, 3)]
    [DataRow(76, 14)]
    public void TenThousandConsecutiveUnclaimedUsbRepeatsLeaveSpaceForStop(int length, int flags)
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] pulse = Envelope(length, (byte)flags, flags == 3 ? (byte)90 : (byte)0);
        if (flags == 14) pulse[28 + 11] = 0x21;
        for (int i = 0; i < 10000; i++) Assert.IsTrue(Enqueue(buffer, pulse, context));
        Assert.AreEqual(1, buffer.PendingOrderedControlCount);
        Assert.AreEqual(9999L, buffer.OrderedControlRepeated);
        byte[] stop = Envelope(length, 3, 0);
        Assert.IsTrue(Enqueue(buffer, stop, context));
        Take(buffer, pulse);
        Take(buffer, stop);
    }

    [TestMethod]
    public void AlternatingRumbleTriggerRumbleAndFinalStopAreAllPreserved()
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] rumble = Envelope(76, 3, 90);
        byte[] trigger = Envelope(76, 14, 0);
        byte[] stop = Envelope(76, 3, 0);
        foreach (byte[] command in new[] { rumble, trigger, rumble, stop })
            Assert.IsTrue(Enqueue(buffer, command, context));
        Assert.AreEqual(0L, buffer.OrderedControlRepeated);
        foreach (byte[] command in new[] { rumble, trigger, rumble, stop }) Take(buffer, command);
    }

    [TestMethod]
    [DataRow("claim")]
    [DataRow("profile")]
    [DataRow("binding")]
    [DataRow("target")]
    [DataRow("media")]
    public void OwnershipAndMediaBoundariesPreventUsbRepeatFolding(string boundary)
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] report = Envelope(76, 3, 90);
        Assert.IsTrue(Enqueue(buffer, report, context));
        switch (boundary)
        {
            case "claim":
                Assert.IsTrue(buffer.TryPeekNativeCommand(new byte[474], out _, out _, out _, out _, out _));
                break;
            case "profile": context = context with { ProfileRevision = context.ProfileRevision + 1 }; break;
            case "binding": context = context with { BindingRevision = context.BindingRevision + 1 }; break;
            case "target": context = Context(buffer); break;
            case "media": buffer.InvalidateNativeRepeat(); break;
        }
        Assert.IsTrue(Enqueue(buffer, report, context));
        Assert.AreEqual(2, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlRepeated);
    }

    [TestMethod]
    [DataRow(2)] // Other validity bits
    [DataRow(5)] // Audio route
    [DataRow(11)] // Opaque trigger payload in a rumble report
    [DataRow(33)] // Reserved bytes
    [DataRow(37)] // Unsupported power value
    [DataRow(39)] // Other command flags
    [DataRow(44)] // LED data
    [DataRow(76)] // Media extension
    public void UnknownOrMediaReportsKeepExactDelivery(int nativeOffset)
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] report = Envelope(474, 3, 90);
        report[nativeOffset == 76 ? 76 : 28 + nativeOffset] = 0xFF;
        Assert.IsTrue(Enqueue(buffer, report, context));
        Assert.IsTrue(Enqueue(buffer, report, context));
        Assert.AreEqual(0L, buffer.OrderedControlRepeated);
        Take(buffer, report);
        Take(buffer, report);
    }

    [TestMethod]
    public void BluetoothPowerStateNowUsesTheSharedDocumentedRepeatPolicy()
    {
        var buffer = Buffer();
        var context = Context(buffer) with
        {
            Target = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice(ConnectionType.BT)
        };
        byte[] report = Envelope(76, 3, 90);
        Assert.IsTrue(Enqueue(buffer, report, context));
        Assert.IsTrue(Enqueue(buffer, report, context));
        Assert.AreEqual(1L, buffer.OrderedControlRepeated);
    }

    private static ViiperFeedbackDispatchBuffer Buffer() => new(2, 4096, 474, 4);
    private static ViiperNativeCommandContext Context(ViiperFeedbackDispatchBuffer buffer) =>
        new(DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice(), 11, 13, buffer.PendingBoundaryRevision);
    private static byte[] Envelope(int length, byte flags, byte strength)
    {
        var envelope = new byte[length];
        DualSenseNativeFeedbackDiagnosticsTests.Report(flags, strength).CopyTo(envelope, 28);
        return envelope;
    }
    private static bool Enqueue(ViiperFeedbackDispatchBuffer buffer, byte[] report,
        ViiperNativeCommandContext context) => buffer.TryEnqueueOrderedControl(
            report, report.Length, 17, 2, nativeCommand: true, nativeContext: context);
    private static void Take(ViiperFeedbackDispatchBuffer buffer, byte[] expected)
    {
        byte[] actual = new byte[474];
        Assert.IsTrue(buffer.TryPeekNativeCommand(actual, out int length, out _, out _, out long receipt, out _));
        CollectionAssert.AreEqual(expected, actual[..length]);
        Assert.IsTrue(buffer.TryCommitNativeCommand(receipt));
    }
}
