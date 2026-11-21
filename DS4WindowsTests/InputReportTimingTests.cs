using System.Diagnostics;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public sealed class InputReportTimingTests
{
    [TestMethod]
    public void ARecentStallSurvivesAFastBurstAndExpiresAfterSeveralSeconds()
    {
        var timing = new InputReportTiming();
        long first = 10 * Stopwatch.Frequency;
        long stall = Stopwatch.Frequency * 80 / 1000;
        long shortGap = Stopwatch.Frequency * 4 / 1000;

        timing.Observe(first);
        Assert.AreEqual(0, timing.RecentLongestGapMilliseconds(first), 0.01);
        timing.Observe(first + stall);
        timing.Observe(first + stall + shortGap);
        Assert.AreEqual(80, timing.RecentLongestGapMilliseconds(first + 4 * Stopwatch.Frequency), 0.01);
        Assert.AreEqual(0, timing.RecentLongestGapMilliseconds(first + 5 * Stopwatch.Frequency), 0.01);
    }

    [TestMethod]
    public void RestartRemovesPreviousConnectionTiming()
    {
        var timing = new InputReportTiming();
        long first = 10 * Stopwatch.Frequency;
        timing.Observe(first);
        timing.Observe(first + Stopwatch.Frequency / 10);

        timing.Reset();
        long reconnected = first + Stopwatch.Frequency;
        timing.Observe(reconnected);
        Assert.AreEqual(0, timing.RecentLongestGapMilliseconds(reconnected), 0.01);
        timing.Observe(reconnected + Stopwatch.Frequency / 100);
        Assert.AreEqual(10, timing.RecentLongestGapMilliseconds(reconnected + Stopwatch.Frequency / 100), 0.01);
    }

    [TestMethod]
    [DoNotParallelize]
    public void StartingANewCaptureExcludesPreviousSamplesAndGapsThatStartedEarlier()
    {
        bool initialSetting = ViiperLatencyHistogram.Enabled;
        try
        {
            var histogram = new ViiperLatencyHistogram();
            ViiperLatencyHistogram.SetEnabled(false);
            histogram.Observe(1);
            Assert.AreEqual(0L, histogram.Snapshot().Count);

            ViiperLatencyHistogram.SetEnabled(true);
            histogram.Observe(100, startedAt: 0);
            Assert.AreEqual(0L, histogram.Snapshot().Count);
            histogram.Observe(20);
            Assert.AreEqual(20L, histogram.Snapshot().MaximumTicks);

            ViiperLatencyHistogram.SetEnabled(false);
            Assert.AreEqual(0L, histogram.Snapshot().Count);
            ViiperLatencyHistogram.SetEnabled(true);
            Assert.AreEqual(0L, histogram.Snapshot().Count);
            histogram.Observe(7);
            Assert.AreEqual(1L, histogram.Snapshot().Count);
            Assert.AreEqual(7L, histogram.Snapshot().MaximumTicks);
        }
        finally
        {
            ViiperLatencyHistogram.SetEnabled(initialSetting);
        }
    }
}
