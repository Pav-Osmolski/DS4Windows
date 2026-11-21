using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace DS4Windows
{
    /// <summary>
    /// Fixed-bucket latency distribution for realtime paths. Storage is
    /// allocated once per histogram per enabled capture.
    /// Snapshots and formatting are diagnostic-only cold operations.
    /// </summary>
    internal sealed class ViiperLatencyHistogram
    {
        internal const string EnvironmentVariableName =
            "DS4WINDOWS_VIIPER_LATENCY_DIAGNOSTICS";

        private static readonly long[] BucketUpperTicks = BuildBucketTicks();
        private sealed class Capture(long startedAt)
        {
            internal long StartedAt { get; } = startedAt;
            internal DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
        }

        private sealed class Samples(Capture capture)
        {
            internal Capture Capture { get; } = capture;
            internal readonly long[] Buckets = new long[BucketUpperTicks.Length];
            internal long Count;
            internal long MaximumTicks;
        }

        private static Capture activeCapture = ParseEnabled(Environment.GetEnvironmentVariable(EnvironmentVariableName))
            ? new(Stopwatch.GetTimestamp()) : null;
        private Samples samples;

        internal static bool Enabled => Volatile.Read(ref activeCapture) != null;
        internal static DateTimeOffset? CaptureStartedUtc => Volatile.Read(ref activeCapture)?.StartedUtc;

        internal static void SetEnabled(bool enabled)
        {
            if (enabled != Enabled)
                Volatile.Write(ref activeCapture, enabled ? new(Stopwatch.GetTimestamp()) : null);
        }

        internal void Observe(long elapsedTicks, long startedAt = long.MaxValue)
        {
            Capture capture = Volatile.Read(ref activeCapture);
            if (capture == null || elapsedTicks < 0 || startedAt < capture.StartedAt)
            {
                return;
            }

            Samples current = Volatile.Read(ref samples);
            while (current?.Capture != capture)
            {
                if (Volatile.Read(ref activeCapture) != capture)
                    return;

                var replacement = new Samples(capture);
                Samples existing = Interlocked.CompareExchange(ref samples, replacement, current);
                if (existing == current)
                {
                    current = replacement;
                    break;
                }

                current = existing;
            }

            int bucket = BucketUpperTicks.Length - 1;
            for (int index = 0; index < BucketUpperTicks.Length; index++)
            {
                if (elapsedTicks <= BucketUpperTicks[index])
                {
                    bucket = index;
                    break;
                }
            }
            RecordMaximum(ref current.MaximumTicks, elapsedTicks);
            Interlocked.Increment(ref current.Buckets[bucket]);
            Interlocked.Increment(ref current.Count);
        }

        internal ViiperLatencySnapshot Snapshot()
        {
            Samples current = Volatile.Read(ref samples);
            if (current == null || current.Capture != Volatile.Read(ref activeCapture))
                return default;

            long sampleCount = Interlocked.Read(ref current.Count);
            if (sampleCount == 0)
            {
                return default;
            }

            return new ViiperLatencySnapshot(sampleCount,
                QuantileTicks(current, sampleCount, 0.50),
                QuantileTicks(current, sampleCount, 0.95),
                QuantileTicks(current, sampleCount, 0.99),
                QuantileTicks(current, sampleCount, 0.999),
                Interlocked.Read(ref current.MaximumTicks));
        }

        private static long QuantileTicks(Samples samples, long sampleCount, double quantile)
        {
            long target = Math.Max(1,
                (long)Math.Ceiling(sampleCount * quantile));
            long cumulative = 0;
            for (int index = 0; index < samples.Buckets.Length; index++)
            {
                cumulative += Interlocked.Read(ref samples.Buckets[index]);
                if (cumulative >= target)
                {
                    return index == samples.Buckets.Length - 1 ?
                        Interlocked.Read(ref samples.MaximumTicks) :
                        BucketUpperTicks[index];
                }
            }
            return Interlocked.Read(ref samples.MaximumTicks);
        }

        private static long[] BuildBucketTicks()
        {
            int[] microseconds =
            {
                1, 2, 5, 10, 20, 50, 100, 200, 500,
                1_000, 2_000, 5_000, 10_000, 20_000, 50_000,
                100_000, 250_000, 500_000, 1_000_000,
            };
            long[] ticks = new long[microseconds.Length];
            for (int index = 0; index < ticks.Length; index++)
            {
                ticks[index] = Math.Max(1,
                    Stopwatch.Frequency * microseconds[index] / 1_000_000);
            }
            return ticks;
        }

        private static bool ParseEnabled(string value) =>
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);

        private static void RecordMaximum(ref long target, long candidate)
        {
            long current = Interlocked.Read(ref target);
            while (candidate > current)
            {
                long observed = Interlocked.CompareExchange(ref target,
                    candidate, current);
                if (observed == current)
                {
                    return;
                }
                current = observed;
            }
        }
    }

    internal readonly struct ViiperLatencySnapshot
    {
        internal ViiperLatencySnapshot(long count, long p50Ticks,
            long p95Ticks, long p99Ticks, long p999Ticks, long maximumTicks)
        {
            Count = count;
            P50Ticks = p50Ticks;
            P95Ticks = p95Ticks;
            P99Ticks = p99Ticks;
            P999Ticks = p999Ticks;
            MaximumTicks = maximumTicks;
        }

        internal long Count { get; }
        internal long P50Ticks { get; }
        internal long P95Ticks { get; }
        internal long P99Ticks { get; }
        internal long P999Ticks { get; }
        internal long MaximumTicks { get; }

        internal string Format(string name)
        {
            return $"{name}[count={Count} " +
                $"p50={Milliseconds(P50Ticks):F3}ms " +
                $"p95={Milliseconds(P95Ticks):F3}ms " +
                $"p99={Milliseconds(P99Ticks):F3}ms " +
                $"p99.9={Milliseconds(P999Ticks):F3}ms " +
                $"max={Milliseconds(MaximumTicks):F3}ms]";
        }

        internal string FormatSummary(string name) => string.Create(CultureInfo.InvariantCulture,
            $"{name}: {Count} samples, p99 ≤ {Milliseconds(Math.Min(P99Ticks, MaximumTicks)):F2} ms, max {Milliseconds(MaximumTicks):F2} ms");

        private static double Milliseconds(long ticks) =>
            ticks * 1000.0 / Stopwatch.Frequency;
    }
}
