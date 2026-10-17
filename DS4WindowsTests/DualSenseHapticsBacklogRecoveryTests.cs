using System.Diagnostics;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public class DualSenseHapticsBacklogRecoveryTests
{
    private static long At(int milliseconds) => Stopwatch.Frequency * milliseconds / 1000;
    private static DualSenseRealtimeHapticsSharedRing Ring(int capacity = 8) =>
        DualSenseRealtimeHapticsSharedRing.CreateOwner("DS4W.BacklogTest." + Guid.NewGuid().ToString("N"), capacity);
    private static byte[] Block(byte value) => Enumerable.Repeat(value, 64).ToArray();
    private static byte[] Report() => Enumerable.Repeat((byte)0x7b, 398).ToArray();

    [TestMethod]
    public void BriefDelayKeepsEveryAttackAndAuthoredZeroInOrder()
    {
        using var ring = Ring();
        ring.Publish(Block(0x31), 0, 1, long.MaxValue, At(1000));
        ring.Publish(Block(0), 0, 1, long.MaxValue, At(1010));
        byte[] report = Report();
        Assert.IsTrue(ring.PrepareForPresentation(report, At(1082)));
        Assert.AreEqual((byte)0x31, report[78]);
        ring.CommitPrepared();
        Assert.IsTrue(ring.PrepareForPresentation(report, At(1090)));
        CollectionAssert.AreEqual(Block(0), report[78..142]);
        Assert.AreEqual(0L, ring.RetiredBlocks);
        Assert.AreEqual(0L, ring.RecoveryCount);
    }

    [TestMethod]
    public void SustainedBacklogResumesAtFreshMediaWithoutChangingControllerOrSpeakerBytes()
    {
        using var ring = Ring();
        foreach (int stamp in new[] { 1000, 1010, 1200, 1280 })
            ring.Publish(Block(stamp == 1280 ? (byte)0 : (byte)0x31), 0, 1, long.MaxValue, At(stamp));
        byte[] report = Report();
        Assert.IsTrue(ring.PrepareForPresentation(report, At(1300)));
        CollectionAssert.AreEqual(Block(0), report[78..142], "The recent authored stop must survive recovery.");
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0x7b, 76).ToArray(), report[..76]);
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0x7b, 256).ToArray(), report[142..]);
        Assert.AreEqual(3L, ring.RetiredBlocks);
        Assert.AreEqual(1L, ring.RecoveryCount);
        ring.CommitPrepared();
        Assert.AreEqual(0, ring.Count);
    }

    [TestMethod]
    public void PreviouslyBoundButUnsubmittedEffectCanExpireDuringWriteCreditWait()
    {
        using var ring = Ring();
        ring.Publish(Block(0x41), 0, 1, long.MaxValue, At(1000));
        byte[] report = Report();
        Assert.IsTrue(ring.PrepareForPresentation(report, At(1010)));
        ring.Publish(Block(0x52), 0, 1, long.MaxValue, At(1290));
        Assert.IsTrue(ring.PrepareForPresentation(report, At(1300)));
        Assert.AreEqual((byte)0x52, report[78]);
        Assert.AreEqual(1L, ring.RetiredBlocks);
        ring.CommitPrepared();
        Assert.AreEqual(0, ring.Count);
    }

    [TestMethod]
    public void ExpiredOnlyQueueProducesSilenceAndFreshDataCanResume()
    {
        using var ring = Ring();
        ring.Publish(Block(0x41), 0, 1, At(1010), At(1000));
        byte[] report = Report();
        Assert.IsFalse(ring.PrepareForPresentation(report, At(1020)));
        CollectionAssert.AreEqual(Block(0), report[78..142]);
        Assert.AreEqual(0, ring.Count);
        ring.Publish(Block(0x52), 0, 1, long.MaxValue, At(1030));
        Assert.IsTrue(ring.PrepareForPresentation(report, At(1040)));
        Assert.AreEqual((byte)0x52, report[78]);
    }

    [TestMethod]
    public void RecoveryCannotCrossAnUnacknowledgedLifecycleGeneration()
    {
        using var ring = Ring();
        ring.Publish(Block(0x41), 0, 1, long.MaxValue, At(1000));
        ring.Publish(Block(0x52), 0, 2, long.MaxValue, At(1290));
        byte[] report = Report();
        Assert.IsFalse(ring.PrepareForPresentation(report, At(1300)));
        Assert.AreEqual(1, ring.Count, "The future-generation block must await its own lifecycle command.");
        CollectionAssert.AreEqual(Block(0), report[78..142]);
        ring.AcceptGeneration(2, silenceFutureReports: true);
        Assert.IsTrue(ring.PrepareForPresentation(report, At(1310)));
        Assert.AreEqual((byte)0x52, report[78]);
    }

    [TestMethod]
    public void FullRingBoundsProducerWaitWithoutRewritingTheConsumerOwnedSlot()
    {
        using var ring = Ring(1);
        long stamp = Stopwatch.GetTimestamp();
        ring.Publish(Block(0x41), 0, 1, long.MaxValue, stamp);
        Task<bool> pending = Task.Run(() => ring.Publish(Block(0x52), 0, 1, long.MaxValue, stamp));
        Assert.IsTrue(pending.Wait(3000), "An unavailable writer must not block the haptics producer indefinitely.");
        Assert.IsTrue(pending.Result, "Retired media must not be retried as if newly captured.");
        Assert.AreEqual(1L, ring.RetiredBlocks);
        Assert.AreEqual(1, ring.Count, "The producer must never overwrite the owned FIFO slot.");
        Assert.IsTrue(ring.ProducerWaitMaximumTicks > 0);
    }
}
