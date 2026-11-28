using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DS4Windows;

// Synchronized by the output device's pending-packet lock. Short digital
// pulses survive a busy writer while unclaimed axes, touch and motion coalesce.
internal sealed class ViiperDualShock4InputQueue
{
    private const int TransitionCapacity = 64;
    private static readonly TimeSpan MaximumTransitionAge = TimeSpan.FromMilliseconds(50);
    private readonly Queue<(byte[] Packet, long Timestamp)> transitions = new(TransitionCapacity);
    private (byte[] Packet, long Timestamp) latest;
    private (byte[] Packet, long Timestamp) continuous;

    internal bool Publish(byte[] packet, long timestamp)
    {
        bool boundary = latest.Packet == null || ButtonsChanged(latest.Packet, packet);
        latest = (packet, timestamp);
        if (boundary)
        {
            bool overflow = transitions.Count == TransitionCapacity;
            if (overflow) transitions.Clear();
            transitions.Enqueue(latest);
            continuous = default;
            return overflow;
        }

        bool replaced = continuous.Packet != null;
        continuous = latest;
        return replaced;
    }

    internal byte[] Peek(long now, out long timestamp)
    {
        if (transitions.TryPeek(out var next))
        {
            if (Stopwatch.GetElapsedTime(next.Timestamp, now) < MaximumTransitionAge)
            {
                timestamp = next.Timestamp;
                return next.Packet;
            }

            DiscardHistory();
        }

        timestamp = continuous.Timestamp;
        return continuous.Packet;
    }

    internal void Commit(byte[] packet)
    {
        if (transitions.TryPeek(out var next) && ReferenceEquals(next.Packet, packet))
            transitions.Dequeue();
        else if (ReferenceEquals(continuous.Packet, packet))
            continuous = default;
    }

    internal void DiscardHistory()
    {
        transitions.Clear();
        continuous = latest;
    }

    internal void Clear()
    {
        transitions.Clear();
        latest = default;
        continuous = default;
    }

    private static bool ButtonsChanged(byte[] previous, byte[] current) =>
        // DualShock 4 wire bytes 4-5 hold buttons and digital triggers.
        // Byte 6 holds the D-pad independently.
        previous[4] != current[4] || previous[5] != current[5] ||
        previous[6] != current[6];
}
