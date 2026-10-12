using System;
using System.Diagnostics;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class FlickStickCalibrationTurnTests
{
    private const byte Left = 1;
    private const byte Right = 2;

    [TestMethod]
    [DataRow(125)]
    [DataRow(250)]
    [DataRow(500)]
    [DataRow(1000)]
    public void ReleasedTapDeliversExactTurnAtEveryReportRate(int reportsPerSecond)
    {
        var h = Started();
        int total = 0;
        for (int frame = 1; frame <= reportsPerSecond; frame++)
        {
            int delta = h.Frame(100_000 + frame * 1_000_000 / reportsPerSecond);
            Assert.IsTrue(delta >= 0, "The calibration turn must never reverse.");
            total += delta;
        }

        Assert.AreEqual(1908, total);
        Assert.IsFalse(h.Turn.Active);
        Assert.AreEqual(0, h.Frame(1_110_000));
    }

    [TestMethod]
    public void JitteredReportsConserveRoundedTargetIncludingFinalPartialInterval()
    {
        var h = Started(rightCalibration: 5.31);
        int[] intervals = { 1_000, 7_000, 2_000, 16_000, 4_000, 31_000, 3_000, 11_000, 23_000 };
        long timestamp = 100_000;
        int total = 0;
        int interval = 0;
        while (timestamp < 1_100_000)
        {
            timestamp = Math.Min(1_100_000, timestamp + intervals[interval++ % intervals.Length]);
            total += h.Frame(timestamp);
        }

        Assert.AreEqual(1912, total);
        Assert.IsFalse(h.Turn.Active);
    }

    [TestMethod]
    [DataRow(1.0, 360)]
    [DataRow(5.3, 1908)]
    [DataRow(5.31, 1912)]
    [DataRow(0.0625, 23)] // 22.5 rounds away from zero, not to the even integer.
    [DataRow(200.0, 72000)]
    public void TurnCountsRoundOnceAndAcceptTheMaximumCalibration(double calibration, int expected)
    {
        Assert.IsTrue(FlickStickCalibrationTurn.TryGetTurnCounts(calibration, out int counts));
        Assert.AreEqual(expected, counts);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-1.0)]
    [DataRow(0.001)] // Positive, but insufficient to produce one mouse count.
    [DataRow(double.Epsilon)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(200.00001)]
    [DataRow(double.MaxValue)]
    public void InvalidCalibrationCannotStartAndDoesNotRetryUntilReleased(double calibration)
    {
        Assert.IsFalse(FlickStickCalibrationTurn.TryGetTurnCounts(calibration, out int counts));
        Assert.AreEqual(0, counts);
        var h = new Harness { RightCalibration = calibration };
        h.Frame(0);
        Assert.AreEqual(0, h.Frame(100_000, Right));
        Assert.IsFalse(h.Turn.Active);

        h.RightCalibration = 5.3;
        Assert.AreEqual(0, h.Frame(110_000, Right));
        Assert.IsFalse(h.Turn.Active);
        h.Frame(120_000);
        h.Frame(130_000, Right);
        Assert.IsTrue(h.Turn.Active);
    }

    [TestMethod]
    public void HeldButtonCompletesOnlyOneTurnThenReleaseAllowsAnother()
    {
        var h = Started();
        int total = 0;
        for (int frame = 1; frame <= 150; frame++)
            total += h.Frame(100_000 + frame * 10_000, Right);
        Assert.AreEqual(1908, total);
        Assert.IsFalse(h.Turn.Active);

        h.Frame(1_610_000);
        h.Frame(1_620_000, Right);
        for (int frame = 1; frame <= 100; frame++)
            total += h.Frame(1_620_000 + frame * 10_000);
        Assert.AreEqual(3816, total);
    }

    [TestMethod]
    public void RapidPressesDuringTurnDoNotRestartOrQueueEitherSide()
    {
        var h = Started();
        h.LeftCalibration = 10;
        int total = 0;
        for (int frame = 1; frame <= 150; frame++)
        {
            // Keep the last press held across completion so there is no fresh
            // press after the one-shot ends. Earlier alternating taps are ignored.
            byte buttons = frame >= 90 ? Left : frame % 3 == 0 ? Left :
                frame % 3 == 1 ? Right : (byte)0;
            total += h.Frame(100_000 + frame * 10_000, buttons);
        }

        Assert.AreEqual(1908, total);
        Assert.IsFalse(h.Turn.Active);
        Assert.AreEqual(0, h.Frame(1_610_000));
    }

    [TestMethod]
    public void SimultaneousNewSidesChooseRightAndAggregateDuplicateBindings()
    {
        var h = new Harness { LeftCalibration = 10, RightCalibration = 5.3 };
        h.Frame(0);
        h.Turn.BeginFrame();
        h.Turn.Press(false);
        h.Turn.Press(true);
        h.Turn.Press(true);
        Assert.AreEqual(0, h.Advance(100_000));
        int total = 0;
        for (int frame = 1; frame <= 100; frame++)
            total += h.Frame(100_000 + frame * 10_000, Left | Right);
        Assert.AreEqual(1908, total);
        Assert.IsFalse(h.Turn.Active);
    }

    [TestMethod]
    public void LeftOnlyUsesLeftCalibrationAndRightPriorityDoesNotFallBackFromInvalidRight()
    {
        var h = Started(buttons: Left, leftCalibration: 10);
        int total = 0;
        for (int frame = 1; frame <= 100; frame++)
            total += h.Frame(100_000 + frame * 10_000);
        Assert.AreEqual(3600, total);

        h.RightCalibration = double.NaN;
        Assert.AreEqual(0, h.Frame(1_110_000, Left | Right));
        Assert.IsFalse(h.Turn.Active);
    }

    [TestMethod]
    public void RunningTurnKeepsItsCalibrationSnapshotUntilTheNextPress()
    {
        var h = Started();
        int total = 0;
        for (int frame = 1; frame <= 100; frame++)
        {
            h.RightCalibration = frame < 50 ? 10 : double.NaN;
            total += h.Frame(100_000 + frame * 10_000);
        }
        Assert.AreEqual(1908, total);

        h.RightCalibration = 10;
        h.Frame(1_110_000, Right);
        for (int frame = 1; frame <= 100; frame++)
            total += h.Frame(1_110_000 + frame * 10_000);
        Assert.AreEqual(5508, total);
    }

    [TestMethod]
    [DataRow("reset")]
    [DataRow("owner")]
    [DataRow("handler")]
    [DataRow("revision")]
    public void LifetimeChangeCancelsMotionAndRequiresReleaseOfHeldButton(string boundary)
    {
        var h = Started();
        Assert.IsTrue(h.Frame(110_000, Right) > 0);
        switch (boundary)
        {
            case "reset": h.CancellationGeneration++; break;
            case "owner": h.Owner = new object(); break;
            case "handler": h.Handler = new object(); break;
            case "revision": h.Revision++; break;
        }
        Assert.AreEqual(0, h.Frame(120_000, Right));
        Assert.IsFalse(h.Turn.Active);
        for (int frame = 1; frame <= 120; frame++)
            Assert.AreEqual(0, h.Frame(120_000 + frame * 10_000, Right));

        h.Frame(1_330_000);
        h.Frame(1_340_000, Right);
        Assert.IsTrue(h.Turn.Active);
        Assert.AreEqual(191, h.Frame(1_440_000));
    }

    [TestMethod]
    [DataRow("unavailable")]
    [DataRow("null owner")]
    [DataRow("null handler")]
    [DataRow("negative revision")]
    [DataRow("negative timestamp")]
    public void InvalidFrameCancelsAndRestorationCannotReplayHeldInput(string invalidity)
    {
        var h = Started();
        Assert.IsTrue(h.Frame(110_000, Right) > 0);
        object owner = h.Owner;
        object handler = h.Handler;
        switch (invalidity)
        {
            case "unavailable": h.Available = false; break;
            case "null owner": h.Owner = null; break;
            case "null handler": h.Handler = null; break;
            case "negative revision": h.Revision = -1; break;
        }
        long timestamp = invalidity == "negative timestamp" ? -1_000 : 120_000;
        Assert.AreEqual(0, h.Frame(timestamp, Right));
        Assert.IsFalse(h.Turn.Active);

        h.Available = true;
        h.Owner = owner;
        h.Handler = handler;
        h.Revision = 1;
        Assert.AreEqual(0, h.Frame(130_000, Right));
        Assert.AreEqual(0, h.Frame(140_000, Right));
        Assert.IsFalse(h.Turn.Active);
        h.Frame(150_000);
        h.Frame(160_000, Right);
        Assert.IsTrue(h.Turn.Active);
    }

    [TestMethod]
    public void MaximumPermittedReportGapPreservesAllCountsWithinBackendPacketRange()
    {
        var h = Started(rightCalibration: 200);
        int total = 0;
        for (int frame = 1; frame <= 4; frame++)
        {
            int delta = h.Frame(100_000 + frame * 250_000);
            Assert.AreEqual(18000, delta);
            Assert.IsTrue(delta <= short.MaxValue);
            total += delta;
        }
        Assert.AreEqual(72000, total);
        Assert.IsFalse(h.Turn.Active);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LongGapOrClockRollbackCancelsAndRequiresANewPress(bool rollback)
    {
        var h = Started();
        Assert.IsTrue(h.Frame(110_000, Right) > 0);
        long next = rollback ? 109_000 : 360_001;
        Assert.AreEqual(0, h.Frame(next, Right));
        Assert.IsFalse(h.Turn.Active);
        Assert.AreEqual(0, h.Frame(next + 10_000, Right));
        h.Frame(next + 20_000);
        h.Frame(next + 30_000, Right);
        Assert.IsTrue(h.Turn.Active);
    }

    [TestMethod]
    public void FirstObservedHeldInputRequiresReleaseAndDuplicateTimestampEmitsNoExtraCounts()
    {
        var h = new Harness();
        Assert.AreEqual(0, h.Frame(0, Right));
        Assert.AreEqual(0, h.Frame(10_000, Right));
        Assert.IsFalse(h.Turn.Active);
        h.Frame(20_000);
        h.Frame(30_000, Right);
        Assert.AreEqual(191, h.Frame(130_000));
        Assert.AreEqual(0, h.Frame(130_000));
        Assert.IsTrue(h.Turn.Active);
    }

    [TestMethod]
    public void SteadyStateActiveAndIdleAdvancementAllocatesNoManagedMemory()
    {
        var h = new Harness();
        h.Frame(0);
        long timestamp = 0;
        // Warm the same call paths that will be measured, including starts and
        // completions. The measured region creates no fixtures or assertions.
        Exercise(ref timestamp, h, 5000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = Exercise(ref timestamp, h, 5000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsTrue(total > 0);
        Assert.AreEqual(0L, allocated);
    }

    private static int Exercise(ref long timestamp, Harness h, int frames)
    {
        int total = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            timestamp += 1000;
            total += h.Frame(timestamp, frame % 1500 == 0 ? Right : (byte)0);
        }
        return total;
    }

    private static Harness Started(byte buttons = Right,
        double leftCalibration = 5.3, double rightCalibration = 5.3)
    {
        var h = new Harness { LeftCalibration = leftCalibration, RightCalibration = rightCalibration };
        Assert.AreEqual(0, h.Frame(0));
        Assert.AreEqual(0, h.Frame(100_000, buttons));
        Assert.IsTrue(h.Turn.Active);
        return h;
    }

    private sealed class Harness
    {
        internal FlickStickCalibrationTurn Turn;
        internal object Owner = new();
        internal object Handler = new();
        internal long Revision = 1;
        internal int CancellationGeneration;
        internal bool Available = true;
        internal double LeftCalibration = 5.3;
        internal double RightCalibration = 5.3;

        internal int Frame(long microseconds, byte buttons = 0)
        {
            Turn.BeginFrame();
            if ((buttons & Left) != 0) Turn.Press(false);
            if ((buttons & Right) != 0) Turn.Press(true);
            return Advance(microseconds);
        }

        internal int Advance(long microseconds) => Turn.Advance(
            (long)Math.Round(microseconds * (double)Stopwatch.Frequency / 1_000_000),
            Revision, CancellationGeneration, Owner, Handler, Available,
            LeftCalibration, RightCalibration);
    }
}
