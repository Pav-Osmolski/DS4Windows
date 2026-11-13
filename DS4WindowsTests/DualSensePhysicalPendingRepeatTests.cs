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

    [TestMethod]
    public void CapturedAlternatingBowUpdatesLeaveRoomForBothPauseReleases()
    {
        var device = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice(ConnectionType.USB);
        var context = Context(device);
        var writes = new List<byte[]>();
        device.PhysicalRawOutputWriteTestHook = r => { writes.Add((byte[])r.Clone()); return true; };
        for (int i = 0; i < 10000; i++)
        {
            Assert.IsTrue(Enqueue(device, Bow((byte)(i % 3 == 0 ? 4 : i % 3 == 1 ? 8 : 12)), context, out _));
        }
        Assert.IsTrue(Field<int>(device, "physicalOutputCommandCount") <= 4);
        Assert.IsTrue(Enqueue(device, Bow(4, 5), context, out _));
        Assert.IsTrue(Enqueue(device, Bow(8, 5), context, out _));
        while (Field<int>(device, "physicalOutputCommandCount") > 0)
            Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
        Assert.AreEqual((byte)5, writes[^2][11]);
        Assert.AreEqual((byte)5, writes[^1][22]);
        Assert.IsTrue(Enqueue(device, Bow(12), context, out _), "An occurrence after delivery must be admitted again.");
        Assert.AreEqual(1, Field<int>(device, "physicalOutputCommandCount"));
    }

    [TestMethod]
    public void BowOffOnTransitionsAndOtherLaneCommandsRemainInOrder()
    {
        var device = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice(ConnectionType.USB);
        var context = Context(device);
        var barrier = Bow(12); barrier[5] = 1; // audio/vendor command barrier
        byte[][] reports = { Bow(12), Bow(4, 5), Bow(4), Bow(8, 5), Bow(8), barrier, Bow(12) };
        foreach (var r in reports) Assert.IsTrue(Enqueue(device, r, context, out _));
        Assert.AreEqual(reports.Length, Field<int>(device, "physicalOutputCommandCount"));
        var writes = new List<byte[]>();
        device.PhysicalRawOutputWriteTestHook = r => { writes.Add((byte[])r.Clone()); return true; };
        foreach (var r in reports)
        {
            device.ProcessNextPhysicalOutputCommand();
            Assert.AreEqual(r[1] & 0x0C, writes[^1][1] & 0x0C);
            for (int side = 0; side < 2; side++)
                if ((r[1] & (4 << side)) != 0)
                    CollectionAssert.AreEqual(r.Skip(11 + side * 11).Take(11).ToArray(),
                        writes[^1].Skip(11 + side * 11).Take(11).ToArray());
        }
    }

    [TestMethod]
    [DataRow("profile")]
    [DataRow("stream")]
    [DataRow("producer")]
    [DataRow("local")]
    [DataRow("overlay")]
    [DataRow("rumble")]
    [DataRow("extension")]
    public void PartialTriggerSharingHonoursLifecycleAndOtherState(string change)
    {
        var device = DualSenseNativeFeedbackDiagnosticsTests.OfflineDevice(ConnectionType.USB);
        var context = Context(device);
        foreach (var r in new[] { Bow(12), Bow(4), Bow(8) }) Assert.IsTrue(Enqueue(device, r, context, out _));
        int before = Field<int>(device, "physicalOutputCommandCount");
        byte[] candidate = Bow(12); byte mask = 0;
        switch (change)
        {
            case "profile": context = context with { Command = context.Command with { ProfileRevision = 2 } }; break;
            case "stream": context = context with { StreamGeneration = 2 }; break;
            case "producer": context = context with { Producer = new object() }; break;
            case "local": typeof(DualSenseDevice).GetField("physicalOutputRequestedGeneration", Private)!.SetValue(device, 2L); break;
            case "overlay": mask = 4; break;
            case "rumble": candidate[3] = 20; break;
            case "extension": candidate[18] = 1; break;
        }
        Assert.IsTrue(device.WriteRawOutputReportFromGame(candidate, 0, 48, out _, preparedTriggerLabValidity: mask, repeatContext: context));
        Assert.AreEqual(before + 1, Field<int>(device, "physicalOutputCommandCount"));
    }

    private static byte[] Bow(byte mask, byte mode = 0x22)
    {
        byte[] r = new byte[48]; r[0] = 2; r[1] = mask;
        if ((mask & 4) != 0) { r[11] = mode; if (mode == 0x22) { r[12] = 0x44; r[14] = 0x29; } }
        if ((mask & 8) != 0) { r[22] = mode; if (mode == 0x22) { r[23] = 2; r[24] = 2; r[25] = 0x3C; } }
        return r;
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
