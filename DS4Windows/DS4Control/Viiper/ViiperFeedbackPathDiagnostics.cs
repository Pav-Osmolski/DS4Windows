using System;
using System.Diagnostics;
using System.Threading;

namespace DS4Windows;

// Observations only: never gates delivery or labels an idle game as unhealthy.
internal sealed class ViiperFeedbackPathDiagnostics
{
    private long controls, speaker, atomic, rear, microphone, other;
    private long nonzeroMotorRequests, nonzeroRearRequests, nonzeroSpeakerPackets;
    private long lastGame, lastMedia, lastLog;
    internal bool Observe(bool verbose, byte type, long now, ReadOnlySpan<byte> payload = default, bool dualSense = false)
    {
        if (!verbose) return false;
        // Inspect known source formats only; these are requests, not proof of
        // physical delivery. Never scan encoded Opus bytes as actuator samples.
        if (type == 0x82 && ContainsNonzero(payload)) Interlocked.Increment(ref nonzeroSpeakerPackets);
        if (dualSense && type == 0x81 && payload.Length >= 76 && payload[28] == 0x02 &&
            (payload[29] & 0x03) != 0 && (payload[31] != 0 || payload[32] != 0))
            Interlocked.Increment(ref nonzeroMotorRequests);
        if (dualSense && type == 0x84 && payload.Length == 474 && payload[76] == 0x36 &&
            payload[152] == 0x92 && payload[153] == 64 && ContainsNonzero(payload.Slice(154, 64)))
            Interlocked.Increment(ref nonzeroRearRequests);
        switch (type)
        {
            case 0x81: Interlocked.Increment(ref controls); Interlocked.Exchange(ref lastGame, now); break;
            case 0x82: Interlocked.Increment(ref speaker); Interlocked.Exchange(ref lastMedia, now); break;
            case 0x83: Interlocked.Increment(ref atomic); Interlocked.Exchange(ref lastMedia, now); break;
            case 0x84: Interlocked.Increment(ref rear); Interlocked.Exchange(ref lastMedia, now); break;
            case 0x85: Interlocked.Increment(ref microphone); break;
            default: Interlocked.Increment(ref other); break;
        }
        return TrySchedule(now);
    }
    internal bool TrySchedule(long now)
    {
        long previous = Interlocked.Read(ref lastLog);
        return (previous == 0 || now - previous >= Stopwatch.Frequency * 30L) &&
            Interlocked.CompareExchange(ref lastLog, now, previous) == previous;
    }
    internal string Format(long now) => FormattableString.Invariant(
        $"verboseObservations: controlFrames={Interlocked.Read(ref controls)} speakerFrames={Interlocked.Read(ref speaker)} atomicFrames={Interlocked.Read(ref atomic)} rearHapticsFrames={Interlocked.Read(ref rear)} microphoneStateFrames={Interlocked.Read(ref microphone)} otherFrames={Interlocked.Read(ref other)} nonzeroMotorRequests={Interlocked.Read(ref nonzeroMotorRequests)} nonzeroRearRequests={Interlocked.Read(ref nonzeroRearRequests)} nonzeroSpeakerPackets={Interlocked.Read(ref nonzeroSpeakerPackets)} lastControlAgeMs={Age(now, Interlocked.Read(ref lastGame)):F0} lastMediaAgeMs={Age(now, Interlocked.Read(ref lastMedia)):F0}");
    private static bool ContainsNonzero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) if (value != 0) return true;
        return false;
    }
    private static double Age(long now, long tick) => tick == 0 ? -1 : (now - tick) * 1000.0 / Stopwatch.Frequency;
}
