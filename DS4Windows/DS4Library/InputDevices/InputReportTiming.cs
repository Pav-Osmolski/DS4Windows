using System;
using System.Diagnostics;
using System.Threading;

namespace DS4Windows.InputDevices;

internal sealed class InputReportTiming
{
    private const int BucketCount = 5;

    private struct Bucket
    {
        internal long Second;
        internal long LongestGap;
    }

    private readonly Bucket[] recent = new Bucket[BucketCount];
    private readonly ViiperLatencyHistogram distribution = new();
    private long previousReport;

    internal ViiperLatencySnapshot Snapshot() => distribution.Snapshot();

    // Called by the controller's input producer. The tooltip only reads the
    // buckets and does not block the input thread.
    internal void Observe(long timestamp)
    {
        long previous = previousReport;
        previousReport = timestamp;
        if (previous == 0 || timestamp <= previous)
            return;

        long gap = timestamp - previous;
        long second = timestamp / Stopwatch.Frequency;
        ref Bucket bucket = ref recent[second % BucketCount];
        if (bucket.Second != second)
        {
            Volatile.Write(ref bucket.Second, -1);
            Volatile.Write(ref bucket.LongestGap, gap);
            Volatile.Write(ref bucket.Second, second);
        }
        else if (gap > bucket.LongestGap)
        {
            Volatile.Write(ref bucket.LongestGap, gap);
        }

        distribution.Observe(gap, previous);
    }

    internal void Reset()
    {
        previousReport = 0;
        foreach (ref Bucket bucket in recent.AsSpan())
        {
            Volatile.Write(ref bucket.Second, -1);
            Volatile.Write(ref bucket.LongestGap, 0);
        }
    }

    internal double RecentLongestGapMilliseconds(long now)
    {
        long second = now / Stopwatch.Frequency;
        long maximum = 0;
        foreach (ref Bucket bucket in recent.AsSpan())
        {
            long recordedSecond = Volatile.Read(ref bucket.Second);
            if (recordedSecond < 0 || recordedSecond <= second - BucketCount || recordedSecond > second)
                continue;

            long gap = Volatile.Read(ref bucket.LongestGap);
            if (recordedSecond == Volatile.Read(ref bucket.Second))
                maximum = Math.Max(maximum, gap);
        }

        return maximum * 1000.0 / Stopwatch.Frequency;
    }
}
