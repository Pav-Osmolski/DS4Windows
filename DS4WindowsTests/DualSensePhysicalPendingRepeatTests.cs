using System.Diagnostics;
using System.Reflection;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class DualSensePhysicalPendingRepeatTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    [DataRow(ConnectionType.USB, false)]
    [DataRow(ConnectionType.BT, false)]
    [DataRow(ConnectionType.USB, true)]
    [DataRow(ConnectionType.BT, true)]
    public void TenThousandPendingPowerStateRepeatsLeaveRoomForChangedEffectAndFinalStop(
        ConnectionType connection, bool improved)
    {
        var device = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice(connection);
        var context = Context(device);
        byte[] head = Report(30, improved), tail = Report(90, improved), stop = Report(0, improved);
        Assert.IsTrue(Enqueue(device, head, context, out _));
        Assert.IsTrue(Enqueue(device, tail, context, out long revision));
        var times = Field<long[]>(device, "physicalOutputCommandQueuedAt");
        long queuedAt = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        times[1] = queuedAt;
        for (int i = 0; i < 10000; i++)
        {
            Assert.IsTrue(Enqueue(device, tail, context, out long repeatedRevision));
            Assert.AreEqual(revision, repeatedRevision);
        }
        Assert.AreEqual(queuedAt, times[1], "Repeated updates must retain the first admission's age.");
        Assert.IsTrue(Enqueue(device, stop, context, out long stopRevision));
        Assert.AreEqual(revision + 1, stopRevision);
        Assert.AreEqual(3, Field<int>(device, "physicalOutputCommandCount"));
        StringAssert.Contains(device.FormatNativeFeedbackHealth(), "nativePhysicalPendingRepeats=10000 ");
        if (connection == ConnectionType.USB)
        {
            var writes = new List<byte[]>();
            device.PhysicalRawOutputWriteTestHook = report => { writes.Add((byte[])report.Clone()); return true; };
            for (int i = 0; i < 3; i++) Assert.AreEqual(
                DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
            CollectionAssert.AreEqual(new byte[] { 30, 90, 0 }, writes.Select(r => r[3]).ToArray());
            StringAssert.Contains(device.FormatNativeFeedbackHealth(), $"nativeLastStopCompletedRevision={stopRevision} ");
        }
    }

    [TestMethod]
    public void ClaimedOrRetryingHeadAndLaterOccurrenceRemainSeparate()
    {
        var device = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice();
        var context = Context(device);
        byte[] report = Report(60);
        Assert.IsTrue(Enqueue(device, report, context, out long first));
        device.PhysicalRawOutputWriteTestHook = _ => false;
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Retry, device.ProcessNextPhysicalOutputCommand());
        Assert.IsTrue(Enqueue(device, report, context, out long second));
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(2, Field<int>(device, "physicalOutputCommandCount"));
        device.PhysicalRawOutputWriteTestHook = _ => true;
        for (int i = 0; i < 2; i++) Assert.AreEqual(
            DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
        Assert.IsTrue(Enqueue(device, report, context, out long third));
        Assert.AreNotEqual(second, third, "An already delivered occurrence cannot suppress a new one.");
    }

    [TestMethod]
    [DataRow("producer")]
    [DataRow("stream")]
    [DataRow("profile")]
    [DataRow("binding")]
    [DataRow("media-boundary")]
    [DataRow("repeat-boundary")]
    [DataRow("local")]
    [DataRow("prepared")]
    [DataRow("underlay")]
    [DataRow("trigger-lab")]
    [DataRow("default")]
    public void BoundariesAndPreparedOverridesPreventSharing(string boundary)
    {
        var device = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice();
        var context = Context(device);
        byte[] report = Report(60), prepared = (byte[])report.Clone();
        Assert.IsTrue(Enqueue(device, Report(30), context, out _));
        Assert.IsTrue(Enqueue(device, report, context, out long first));
        byte mask = 0;
        switch (boundary)
        {
            case "producer": context = context with { Producer = new object() }; break;
            case "stream": context = context with { StreamGeneration = 22 }; break;
            case "profile": context = context with { Command = context.Command with { ProfileRevision = 22 } }; break;
            case "binding": context = context with { Command = context.Command with { BindingRevision = 22 } }; break;
            case "media-boundary": context = context with { Command = context.Command with { PendingBoundaryRevision = 22 } }; break;
            case "repeat-boundary": context = context with { Command = context.Command with { RepeatBoundaryRevision = 22 } }; break;
            case "default": context = default; break;
            case "local": typeof(DualSenseDevice).GetField("physicalOutputRequestedGeneration", Private)!.SetValue(device, 22L); break;
            case "prepared": prepared[3]++; break;
            case "underlay": report[3]++; break;
            case "trigger-lab": mask = 4; break;
        }
        Assert.IsTrue(device.WriteRawOutputReportFromGame(prepared, 0, 48, out long next,
            report, 0, mask, context));
        Assert.AreNotEqual(first, next);
        Assert.AreEqual(3, Field<int>(device, "physicalOutputCommandCount"));
    }

    [TestMethod]
    public void DistinctRumbleTriggerModeChangesAndUnknownCommandsKeepExactFifoOrder()
    {
        var device = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice();
        var context = Context(device);
        byte[] pulse = Report(60), stop = Report(0), trigger = Report(0), unknown = Report(60);
        trigger[1] = 0x0E; trigger[11] = 0x21;
        unknown[2] |= 0x20; // LED reset/animation is a command barrier.
        byte[][] commands = { pulse, trigger, pulse, unknown, unknown, pulse, stop, pulse };
        foreach (var command in commands) Assert.IsTrue(Enqueue(device, command, context, out _));
        var slots = Field<byte[][]>(device, "physicalOutputCommandSlots");
        Assert.AreEqual(commands.Length, Field<int>(device, "physicalOutputCommandCount"));
        for (int i = 0; i < commands.Length; i++) CollectionAssert.AreEqual(commands[i], slots[i]);
        StringAssert.Contains(device.FormatNativeFeedbackHealth(), "nativePhysicalPendingRepeats=0 ");
    }

    [TestMethod]
    public void FullQueueSharesOnlyAnEligibleTailAndDoesNotLoseADistinctStop()
    {
        var device = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice();
        var context = Context(device);
        for (byte i = 1; i <= 64; i++) Assert.IsTrue(Enqueue(device, Report(i), context, out _));
        Assert.IsTrue(Enqueue(device, Report(64), context, out _));
        Assert.IsFalse(Enqueue(device, Report(0), context, out _));
        device.PhysicalRawOutputWriteTestHook = _ => true;
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
        Assert.IsTrue(Enqueue(device, Report(0), context, out long stopRevision));
        for (int i = 0; i < 64; i++) Assert.AreEqual(
            DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
        StringAssert.Contains(device.FormatNativeFeedbackHealth(), $"nativeLastStopCompletedRevision={stopRevision} ");
    }

    private static byte[] Report(byte strength, bool improved = false)
    {
        byte[] report = DualSenseNativeFeedbackDiagnosticsTests.Report(improved ? (byte)2 : (byte)3, strength);
        report[2] = 0x44; // Documented power and lightbar state, as in issue 47 summaries.
        report[37] = 0x27;
        report[39] = improved ? (byte)4 : (byte)0;
        return report;
    }
    private static DualSenseNativeRepeatContext Context(DualSenseDevice device) =>
        new(new object(), 1, new(device, 1, 1, 1));
    private static bool Enqueue(DualSenseDevice device, byte[] report,
        DualSenseNativeRepeatContext context, out long revision) =>
        device.WriteRawOutputReportFromGame(report, 0, 48, out revision, repeatContext: context);
    private static T Field<T>(DualSenseDevice device, string name) =>
        (T)typeof(DualSenseDevice).GetField(name, Private)!.GetValue(device)!;
}
