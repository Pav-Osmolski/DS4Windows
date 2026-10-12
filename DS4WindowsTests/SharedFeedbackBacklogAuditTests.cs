using System;
using System.Collections.Generic;
using System.Threading;
using DS4Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4WindowsTests;

// Hardware-free audit of complete actuator-state paths. These are deliberately
// not assertions that validity-masked native DualSense commands may coalesce.
[TestClass]
public sealed class SharedFeedbackBacklogAuditTests
{
    [TestMethod]
    public void CumulativeMailboxFloodLeavesOnlyNewestNeutralWithoutReplay()
    {
        var buffer = new ViiperFeedbackDispatchBuffer(2, 8, 16);
        byte[] state = new byte[2];
        for (int index = 0; index < 10_000; index++)
        {
            state[0] = (byte)(index % 254 + 1);
            state[1] = (byte)(255 - state[0]);
            Assert.IsTrue(buffer.QueueControl(state, state.Length, 7, 3));
        }

        Array.Clear(state);
        Assert.IsTrue(buffer.QueueControl(state, state.Length, 7, 3));
        byte[] received = new byte[16];
        Assert.IsTrue(buffer.TryTakeControl(received, out int length,
            out long generation, out int deviceIndex));
        Assert.AreEqual(7L, generation);
        Assert.AreEqual(3, deviceIndex);
        Assert.IsTrue(Xbox360CanonicalFeedbackAdapter.TryDecode(received,
            length, out ControllerFeedbackActuatorState neutral));
        Assert.IsTrue(neutral.IsNeutral);
        Assert.IsFalse(buffer.TryTakeControl(received, out _, out _, out _));
        Assert.AreEqual(10_001L, buffer.ControlEnqueued);
        Assert.AreEqual(10_000L, buffer.ControlCoalesced);
        Assert.AreEqual(1L, buffer.ControlDequeued);
        Assert.AreEqual(0L, buffer.ControlDropped);
        Assert.AreEqual(0L, buffer.OrderedControlEnqueued);
    }

    [TestMethod]
    [DataRow((int)ControllerFeedbackSource.Xbox360VirtualDevice)]
    [DataRow((int)ControllerFeedbackSource.XboxOneVirtualDevice)]
    [DataRow((int)ControllerFeedbackSource.XboxSeriesVirtualDevice)]
    [DataRow((int)ControllerFeedbackSource.DualShock4VirtualDevice)]
    [DataRow((int)ControllerFeedbackSource.DualSenseVirtualDevice)]
    [DataRow((int)ControllerFeedbackSource.DualSenseEdgeVirtualDevice)]
    public void CanonicalStateFloodPresentsFinalNeutralInsteadOfOldHistory(
        int sourceValue)
    {
        var (pump, lane) = CreateLane((ControllerFeedbackSource)sourceValue);
        var sink = new RecordingSink();
        Assert.IsTrue(lane.TryPublish(State(1), 1_000));
        Assert.AreEqual(ControllerFeedbackPumpDisposition.Delivered,
            pump.PumpOnce(1_000, sink, out _));

        for (ushort index = 1; index <= 10_000; index++)
            Assert.IsTrue(lane.TryPublish(State(index), (ulong)index + 1_000));
        Assert.IsTrue(lane.TryPublish(default, 11_001));

        Assert.AreEqual(ControllerFeedbackPumpDisposition.Delivered,
            pump.PumpOnce(11_001, sink, out var neutral));
        Assert.AreEqual(ControllerFeedbackCommand.Neutral, neutral.Frame.Command);
        Assert.AreEqual((ushort)0, neutral.Frame.BodyLow);
        Assert.AreEqual((ushort)0, neutral.Frame.BodyHigh);
        Assert.AreEqual((ushort)0, neutral.Frame.LeftTrigger);
        Assert.AreEqual((ushort)0, neutral.Frame.RightTrigger);
        Assert.AreEqual(ControllerFeedbackPumpDisposition.None,
            pump.PumpOnce(11_001, sink, out _));
        Assert.AreEqual(2, sink.Deliveries.Count,
            "Complete cumulative state must not create a historical FIFO.");

        Assert.IsTrue(lane.RequestStop(11_002));
        Assert.AreEqual(ControllerFeedbackPumpDisposition.Delivered,
            pump.PumpOnce(11_002, sink, out var stop));
        Assert.AreEqual(ControllerFeedbackDeliveryDisposition.Stop, stop.Disposition);
        Assert.AreEqual(ControllerFeedbackPumpDisposition.None,
            pump.PumpOnce(11_002, sink, out _));
    }

    [TestMethod]
    [DataRow((int)ControllerFeedbackSource.Xbox360VirtualDevice)]
    [DataRow((int)ControllerFeedbackSource.XboxOneVirtualDevice)]
    [DataRow((int)ControllerFeedbackSource.XboxSeriesVirtualDevice)]
    [DataRow((int)ControllerFeedbackSource.DualShock4VirtualDevice)]
    public void TerminalStopOvertakesUnpresentedStateFloodAndRemainsRetryable(
        int sourceValue)
    {
        var (pump, lane) = CreateLane((ControllerFeedbackSource)sourceValue);
        var sink = new RecordingSink();
        Assert.IsTrue(lane.TryPublish(State(1), 1_000));
        Assert.AreEqual(ControllerFeedbackPumpDisposition.Delivered,
            pump.PumpOnce(1_000, sink, out var initial));
        for (ushort index = 1; index <= 10_000; index++)
            Assert.IsTrue(lane.TryPublish(State(index), (ulong)index + 1_000));

        Assert.IsTrue(lane.RequestStop(11_001));
        Assert.IsFalse(lane.TryPublish(State(42), 11_002));
        sink.RejectNext = true;
        Assert.AreEqual(ControllerFeedbackPumpDisposition.RetryPending,
            pump.PumpOnce(11_001, sink, out var firstStop));
        Assert.AreEqual(ControllerFeedbackDeliveryDisposition.Stop, firstStop.Disposition);
        Assert.AreEqual(initial.DeliveryEpoch, firstStop.DeliveryEpoch);
        Assert.AreEqual(ControllerFeedbackPumpDisposition.Delivered,
            pump.PumpOnce(11_002, sink, out var retriedStop));
        Assert.AreEqual(firstStop, retriedStop);
        Assert.AreEqual(ControllerFeedbackPumpDisposition.None,
            pump.PumpOnce(11_003, sink, out _));
        Assert.AreEqual(3, sink.Deliveries.Count);
        Assert.AreEqual(ControllerFeedbackDeliveryDisposition.Stop, sink.Deliveries[1].Disposition);
        Assert.AreEqual(ControllerFeedbackDeliveryDisposition.Stop, sink.Deliveries[2].Disposition);
    }

    [TestMethod]
    public void XboxDispatcherBackpressureDoesNotOverwriteActiveEffectOrRetriedStop()
    {
        using var deliveryEntered = new ManualResetEventSlim();
        using var releaseDelivery = new ManualResetEventSlim();
        using var ackEntered = new ManualResetEventSlim();
        using var releaseAck = new ManualResetEventSlim();
        var values = new List<byte>();
        var correlations = new List<ulong>();
        int faults = 0;
        bool firstPayloadPreserved = false;
        byte[] effect = new byte[ControllerFeedbackFrame.SerializedLength];
        effect[0] = 0x7A;
        byte[] stop = new byte[effect.Length];
        using var dispatcher = new XboxOneFeedbackDeliveryDispatcher((payload, _) =>
        {
            values.Add(payload[0]);
            if (values.Count == 1)
            {
                deliveryEntered.Set();
                if (!releaseDelivery.Wait(5_000)) return false;
                firstPayloadPreserved = payload[0] == 0x7A;
            }
            return true;
        }, (correlation, accepted) =>
        {
            if (!accepted) throw new InvalidOperationException("Unexpected delivery rejection.");
            correlations.Add(correlation);
            if (correlation == 1)
            {
                ackEntered.Set();
                if (!releaseAck.Wait(5_000)) throw new TimeoutException();
            }
        }, () => Interlocked.Increment(ref faults));
        try
        {
            Assert.IsTrue(dispatcher.TryEnqueue(effect, 1));
            Assert.IsTrue(deliveryEntered.Wait(2_000));
            for (ulong correlation = 2; correlation <= 10_001; correlation++)
                Assert.IsFalse(dispatcher.TryEnqueue(stop, correlation),
                    "An unacknowledged physical delivery cannot accumulate successors.");
            releaseDelivery.Set();
            Assert.IsTrue(ackEntered.Wait(2_000));
            Assert.IsTrue(dispatcher.TryEnqueue(stop, 10_002),
                "A retained stop must fit the one ACK-successor slot.");
            Assert.IsFalse(dispatcher.TryEnqueue(effect, 10_003));
        }
        finally
        {
            releaseDelivery.Set();
            releaseAck.Set();
        }
        Assert.IsTrue(dispatcher.WaitForIdle(2_000));
        Assert.IsTrue(firstPayloadPreserved);
        CollectionAssert.AreEqual(new byte[] { 0x7A, 0 }, values);
        CollectionAssert.AreEqual(new ulong[] { 1, 10_002 }, correlations);
        Assert.AreEqual(0, faults);
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public void ImpulseProjectionHasNoRetainedTailWhenAllChannelsBecomeNeutral(
        bool independentTriggers, bool impulseEnabled, bool routeToTriggers)
    {
        for (ushort index = 1; index <= 10_000; index++)
        {
            XboxOneCanonicalFeedbackAdapter.ProjectPhysical(State(index),
                independentTriggers, impulseEnabled, routeToTriggers,
                out _, out _, out _, out _);
        }
        XboxOneCanonicalFeedbackAdapter.ProjectPhysical(default,
            independentTriggers, impulseEnabled, routeToTriggers,
            out byte heavy, out byte light, out byte left, out byte right);
        Assert.AreEqual((byte)0, heavy);
        Assert.AreEqual((byte)0, light);
        Assert.AreEqual((byte)0, left);
        Assert.AreEqual((byte)0, right);
    }

    private static (ControllerFeedbackStateLanePump Pump,
        ControllerFeedbackStateLanePump.Lane Lane) CreateLane(ControllerFeedbackSource source)
    {
        Assert.IsTrue(ControllerFeedbackStateLanePump.TryCreate(7, 11, out var pump));
        Assert.IsTrue(pump.TryCreateLane(ControllerFeedbackPublicationOrigin.NativeGame,
            source, ownershipEpoch: 13, timeToLiveMicroseconds: 250_000,
            renewalIntervalMicroseconds: 100_000, out var lane));
        return (pump, lane);
    }

    private static ControllerFeedbackActuatorState State(ushort value) =>
        new(value, (ushort)(value + 1), (ushort)(value + 2), (ushort)(value + 3));

    private sealed class RecordingSink : IControllerFeedbackDeliverySink
    {
        internal readonly List<ControllerFeedbackDelivery> Deliveries = new();
        internal bool RejectNext;

        public bool TryDeliver(in ControllerFeedbackDelivery delivery)
        {
            Deliveries.Add(delivery);
            bool accepted = !RejectNext;
            RejectNext = false;
            return accepted;
        }
    }
}
