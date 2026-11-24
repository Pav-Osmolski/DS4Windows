using System;
using System.Diagnostics;
using System.Threading;

namespace DS4Windows;

internal sealed class MacroControllerReportProgress
{
    private readonly object gate = new();
    private int requested;
    private int mapped;
    private int published;

    internal bool HasPending => Volatile.Read(ref requested) != Volatile.Read(ref published);

    internal void Request() => Interlocked.Increment(ref requested);

    internal void Capture() => mapped = Volatile.Read(ref requested);

    internal void Publish()
    {
        if (mapped == Volatile.Read(ref published)) return;
        lock (gate)
        {
            Volatile.Write(ref published, mapped);
            Monitor.PulseAll(gate);
        }
    }

    internal void WaitForReport()
    {
        int revision = Volatile.Read(ref requested);
        if (unchecked(Volatile.Read(ref published) - revision) >= 0) return;
        // A disconnected, paused or busy output must not strand a macro worker.
        long start = Stopwatch.GetTimestamp();
        lock (gate)
        {
            while (unchecked(published - revision) < 0)
            {
                int remaining = 50 - (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (remaining <= 0) return;
                Monitor.Wait(gate, remaining);
            }
        }
    }
}
