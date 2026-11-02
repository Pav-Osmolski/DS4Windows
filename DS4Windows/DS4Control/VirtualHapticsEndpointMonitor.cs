using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace DS4Windows;

// Read-only, demand-driven observation. Never open an audio stream or discover
// endpoints on the UI/input thread. Each output owns its cache; generation and
// USB/IP port fence late results across reconnects and slot reuse.
internal sealed class VirtualHapticsEndpointMonitor
{
    internal const long RefreshMilliseconds = 2000;
    private sealed record Observation(int Port, long Generation, bool Disabled, long NextCheck);
    private Observation observation;
    private int checking;
    private readonly Func<int, bool?> probe;
    private readonly Func<long> clock;

    internal VirtualHapticsEndpointMonitor(Func<int, bool?> probe = null, Func<long> clock = null)
    {
        this.probe = probe ?? ReadDisabled;
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    internal bool IsDisabled(int port, long generation)
    {
        var current = Volatile.Read(ref observation);
        if (port < 0)
        {
            Interlocked.Exchange(ref observation, null);
            return false;
        }
        long now = clock();
        if (current == null || current.Port != port || current.Generation != generation)
        {
            // Leave normal endpoint enumeration time to settle after connection.
            Interlocked.CompareExchange(ref observation,
                new Observation(port, generation, false, now + RefreshMilliseconds), current);
            return false;
        }
        if (now >= current.NextCheck && Interlocked.CompareExchange(ref checking, 1, 0) == 0)
        {
            _ = Task.Run(() =>
            {
                bool? disabled = null;
                try { disabled = probe(port); }
                catch { /* An unreadable endpoint is not evidence that it is disabled. */ }
                finally
                {
                    Interlocked.CompareExchange(ref observation,
                        new Observation(port, generation, disabled == true,
                            clock() + RefreshMilliseconds), current);
                    Volatile.Write(ref checking, 0);
                }
            });
        }
        return current.Disabled;
    }

    internal static bool? EvaluateStates(IEnumerable<DeviceState> matchedStates)
    {
        int count = 0;
        DeviceState state = 0;
        foreach (var match in matchedStates) { count++; state = match; }
        // Missing/disconnected, ambiguous and unreadable devices must never be
        // described as disabled. Capture devices are not part of this check.
        return count == 1 && state is DeviceState.Active or DeviceState.Disabled
            ? state == DeviceState.Disabled : null;
    }

    private static bool? ReadDisabled(int port)
    {
        using var enumerator = new MMDeviceEnumerator();
        var states = new List<DeviceState>();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
        {
            using (endpoint)
            {
                if (DualSenseAudioPassthrough.IsDualSenseEndpoint(endpoint) &&
                    DualSenseAudioPassthrough.TryGetEndpointUsbipPort(endpoint, out int endpointPort) &&
                    endpointPort == port)
                    states.Add(endpoint.State);
            }
        }
        return EvaluateStates(states);
    }
}
