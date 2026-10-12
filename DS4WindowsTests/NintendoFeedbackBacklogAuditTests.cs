using System;
using System.Collections.Generic;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4Windows.Switch2;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4WindowsTests;

// Admission/retention characterizations, not HID, radio, or actuator benchmarks.
// Frozen clocks distinguish fresh-command admission from sustain scheduling;
// no timers, workers, controllers, or transport handles are started here.
[TestClass]
public sealed class NintendoFeedbackBacklogAuditTests
{
    [TestMethod]
    [DataRow(10_000, 0)]
    [DataRow(12_000, 0)]
    [DataRow(15_000, 0)]
    [DataRow(10_000, 1)]
    [DataRow(12_000, 1)]
    [DataRow(15_000, 1)]
    [DataRow(10_000, 2)]
    [DataRow(12_000, 2)]
    [DataRow(15_000, 2)]
    public void Switch2FreshBurstDoesNotWaitForSustainCadenceAndEndsNeutral(
        int maintenanceIntervalMicroseconds, int sourceKind)
    {
        // The shared sink is used by standalone/joined BLE and Pro USB owners.
        // These rows exercise its three configured intervals, not transport speed.
        const ulong now = 1_000;
        var writer = new RecordingWriter();
        var sink = new Switch2HdRumbleDeliverySink(writer, 7, 11,
            minimumMaintenanceIntervalMicroseconds: (ulong)maintenanceIntervalMicroseconds,
            hostWriteStartClock: () => now, feedbackClock: () => now);
        var source = sourceKind switch
        {
            1 => ControllerFeedbackSource.DualSenseVirtualDevice,
            2 => ControllerFeedbackSource.Switch2VirtualDevice,
            _ => ControllerFeedbackSource.XboxOneVirtualDevice,
        };
        ControllerFeedbackDelivery last = default;
        for (ulong sequence = 1; sequence <= 1_000; sequence++)
        {
            // Alternating A/B/A states and independent left/right rich groups.
            ushort low = sequence % 2 == 0 ? (ushort)30_000 : (ushort)10_000;
            ushort high = sequence % 2 == 0 ? (ushort)5_000 : (ushort)20_000;
            last = Frame(sequence, source, low, high, now);
            var left = Switch2HdRumbleFeedbackTranslator.CreateCompatibilityGroup(low, high);
            var right = Switch2HdRumbleFeedbackTranslator.CreateCompatibilityGroup(high, low);
            if (sourceKind != 0)
                Assert.IsTrue(sink.TryStageSourcePreservedSynthesis(last.Frame,
                    sourceKind == 1 ? Switch2HdRumbleFeedbackFidelity.DualSensePcmDualBand :
                        Switch2HdRumbleFeedbackFidelity.NativeSwitch2PassThrough, left, right));

            Assert.IsTrue(sink.TryDeliver(last));
            Assert.AreEqual((int)sequence, writer.Reports.Count,
                "A fresh command must not be held for an unchanged-state sustain interval.");
            Assert.AreEqual(sequence, writer.Reports[^1].Sequence);
            Assert.AreEqual(now, writer.Reports[^1].TimestampMicroseconds);
            var expectedLeft = sourceKind == 0 ? HeldBody(left) : left;
            Assert.AreEqual(expectedLeft, writer.Reports[^1].Left);
            Assert.AreEqual(sourceKind == 0 ? expectedLeft : right, writer.Reports[^1].Right);

            Assert.IsTrue(sink.TryDeliver(last));
            Assert.IsTrue(sink.MaintenanceSink.TryDeliver(last));
            Assert.AreEqual((int)sequence, writer.Reports.Count,
                "Neither an exact delivered retry nor early maintenance may create another effect.");
        }

        Assert.IsTrue(sink.TryDeliver(Frame(1_001, source, 0, 0, now)));
        Assert.AreEqual(1_001, writer.Reports.Count);
        Assert.IsTrue(writer.Reports[^1].IsNeutral);
        Assert.AreEqual(ControllerFeedbackCommand.Neutral, writer.Reports[^1].Command);
        var stop = Stop();
        Assert.IsTrue(sink.TryDeliver(stop));
        Assert.AreEqual(1_002, writer.Reports.Count);
        Assert.IsTrue(writer.Reports[^1].IsStop);
        Assert.IsTrue(writer.Reports[^1].IsNeutral);
        Assert.IsTrue(sink.TryDeliver(stop));
        Assert.IsFalse(sink.TryDeliver(last), "A stopped epoch cannot resume the prior burst.");
        Assert.AreEqual(1_002, writer.Reports.Count);
    }

    [TestMethod]
    public void Switch2CanonicalUnpumpedBurstRetainsLatestStateNotAnOrderedBacklog()
    {
        const ulong now = 1_000;
        var writer = new RecordingWriter();
        var sink = new Switch2HdRumbleDeliverySink(writer, 7, 11,
            minimumMaintenanceIntervalMicroseconds: 15_000,
            hostWriteStartClock: () => now, feedbackClock: () => now);
        Assert.IsTrue(ControllerFeedbackStateLanePump.TryCreate(7, 11, out var pump));
        Assert.IsTrue(pump.TryCreateLane(ControllerFeedbackPublicationOrigin.NativeGame,
            ControllerFeedbackSource.XboxOneVirtualDevice, 19, 250_000, 100_000, out var lane));
        Assert.IsTrue(lane.TryPublish(new(100, 0, 0, 0), now));
        Assert.AreEqual(ControllerFeedbackPumpDisposition.Delivered, pump.PumpOnce(now, sink, out _));

        for (ushort value = 1; value <= 4_096; value++)
            Assert.IsTrue(lane.TryPublish(new(value, 0, 0, 0), now));
        Assert.AreEqual(ControllerFeedbackPumpDisposition.Delivered, pump.PumpOnce(now, sink, out var newest));
        Assert.AreEqual((ushort)4_096, newest.Frame.BodyLow);
        Assert.AreEqual((ulong)4_097, newest.Frame.Sequence);
        Assert.AreEqual(2, writer.Reports.Count,
            "Intermediate unclaimed states are coalesced, not deferred for later playback.");
        Assert.AreEqual(ControllerFeedbackPumpDisposition.None, pump.PumpOnce(now, sink, out _));
        Assert.IsTrue(lane.RequestStop(now));
        Assert.AreEqual(ControllerFeedbackPumpDisposition.Delivered, pump.PumpOnce(now, sink, out _));
        Assert.IsTrue(writer.Reports[^1].IsStop);
        Assert.AreEqual(3, writer.Reports.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LegacyLatestMailboxBurstAndStopCannotReplayOlderEffects(bool rejectFirst)
    {
        var reports = new List<byte[]>();
        var output = new LegacyNintendoRumbleOutput(64, report =>
        {
            reports.Add((byte[])report.Clone());
            return !rejectFirst || reports.Count != 1;
        });
        Assert.IsTrue(output.Publish(Packet(1), true));
        Assert.IsTrue(output.PumpOnce());
        for (int i = 0; i < 4_096; i++)
            Assert.IsTrue(output.Publish(Packet((byte)(2 + i % 200)), true));
        Assert.IsTrue(output.Publish(Packet(250), true));
        Assert.IsTrue(output.PumpOnce());
        Assert.AreEqual(2, reports.Count);
        CollectionAssert.AreEqual(Packet(250), reports[^1]);
        Assert.IsFalse(output.PumpOnce());

        for (int i = 0; i < 4_096; i++)
            Assert.IsTrue(output.Publish(Packet(2), true));
        output.RequestStop(Packet(0));
        Assert.IsTrue(output.PumpOnce());
        Assert.IsTrue(output.StopDelivered);
        Assert.AreEqual(3, reports.Count);
        CollectionAssert.AreEqual(Packet(0), reports[^1]);
        output.RequestRetry();
        Assert.IsFalse(output.PumpOnce());
        Assert.IsFalse(output.Publish(Packet(3), true));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LegacyExplicitDelayTerminalStopClearsQueuedAndExpiredRichFrames(bool streamed)
    {
        long now = 1_000;
        object credential = new();
        var reports = new List<byte[]>();
        var output = new LegacyNintendoRumbleOutput(64, report =>
        {
            reports.Add((byte[])report.Clone());
            return true;
        });
        using var delivery = new LegacyJoyConHdRumbleDelivery(output, 64, true,
            candidate => ReferenceEquals(candidate, credential), () => now, scheduleTimers: false);
        var active = Switch2HdRumbleFeedbackTranslator.CreateCompatibilityGroup(30_000, 10_000);
        Assert.IsTrue(delivery.Publish(active, credential, false, 0));
        Assert.IsTrue(output.PumpOnce());
        for (int i = 0; i < 1_000; i++)
            Assert.IsTrue(delivery.Publish(active, credential, streamed, 50));
        Assert.AreEqual(1, reports.Count);
        Assert.IsTrue(delivery.Publish(default, credential, false, 50, terminal: true));
        Assert.IsTrue(output.PumpOnce());
        byte[] neutral = new byte[64];
        LegacyJoyConHdRumble.Encode(default, true, neutral);
        CollectionAssert.AreEqual(neutral, reports[^1]);
        Assert.AreEqual(2, reports.Count);
        now += 10_000;
        _ = delivery.Service();
        Assert.IsFalse(output.PumpOnce(), "Old delayed applies must not reappear after terminal neutral.");
        Assert.AreEqual(2, reports.Count);
    }

    private static ControllerFeedbackDelivery Frame(ulong sequence, ControllerFeedbackSource source,
        ushort low, ushort high, ulong now)
    {
        Assert.IsTrue(ControllerFeedbackFrame.TryCreate(source,
            low == 0 && high == 0 ? ControllerFeedbackCommand.Neutral : ControllerFeedbackCommand.Apply,
            ControllerFeedbackActuators.All, low, high, 0, 0, sequence, 7, 11, 19, now, 250_000, out var frame));
        return new(ControllerFeedbackDeliveryDisposition.Frame,
            ControllerFeedbackPublicationOrigin.NativeGame, frame, 7, 11, 31);
    }

    private static ControllerFeedbackDelivery Stop() => new(
        ControllerFeedbackDeliveryDisposition.Stop, ControllerFeedbackPublicationOrigin.NativeGame,
        default, 7, 11, 31);

    private static Switch2HdRumbleGroup HeldBody(in Switch2HdRumbleGroup group) => new(group.First,
        new(group.Second.Oscillator0ControlCode, 0, group.Second.Oscillator1ControlCode, 0),
        new(group.Third.Oscillator0ControlCode, 0, group.Third.Oscillator1ControlCode, 0));

    private static byte[] Packet(byte value)
    {
        byte[] bytes = new byte[64];
        Array.Fill(bytes, value);
        return bytes;
    }

    private sealed class RecordingWriter : ISwitch2HdRumblePhysicalWriter
    {
        internal readonly List<Switch2HdRumblePhysicalSubmission> Reports = new();
        public bool Authenticates(ulong deviceGeneration, ulong transportGeneration) =>
            deviceGeneration == 7 && transportGeneration == 11;
        public Switch2HdRumblePhysicalWriteResult TryWrite(in Switch2HdRumblePhysicalSubmission submission)
        {
            Reports.Add(submission);
            return Switch2HdRumblePhysicalWriteResult.Success();
        }
    }
}
