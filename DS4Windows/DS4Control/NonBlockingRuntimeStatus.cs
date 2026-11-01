using System;
using System.Threading;

namespace DS4Windows;

// Status is an observation, not permission to operate the runtime. In particular,
// UI polling must never wait behind endpoint discovery, capture teardown or a
// controller reconfiguration. Keep the last coherent observation while busy.
internal sealed class NonBlockingRuntimeStatus<T>
{
    private sealed record Snapshot(T Value);
    private Snapshot last;

    internal NonBlockingRuntimeStatus(T initial) => last = new Snapshot(initial);

    internal T Read(object gate, Func<T> readUnderGate)
    {
        if (!Monitor.TryEnter(gate)) return Volatile.Read(ref last).Value;
        try
        {
            T value = readUnderGate();
            Volatile.Write(ref last, new Snapshot(value));
            return value;
        }
        finally { Monitor.Exit(gate); }
    }

    internal static NonBlockingRuntimeStatus<T>[] CreateSlots(int count, T initial)
    {
        var result = new NonBlockingRuntimeStatus<T>[count];
        for (int i = 0; i < count; i++) result[i] = new(initial);
        return result;
    }
}
