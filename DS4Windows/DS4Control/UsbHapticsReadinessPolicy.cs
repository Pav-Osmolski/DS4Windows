using System;

namespace DS4Windows;

// Endpoint discovery follows Windows PnP enumeration asynchronously. These
// explicit prerequisites are retryable progress, not playback failures.
internal sealed class AudioEndpointPendingException(string message) : InvalidOperationException(message);

internal readonly record struct UsbHapticsRetryNotice(string Reason, bool Warning);

internal sealed class UsbHapticsReadinessPolicy
{
    internal static readonly TimeSpan ReadinessGrace = TimeSpan.FromSeconds(15);
    private TimeSpan? waitingSince;
    private string lastReason;
    private bool lastWarning, unexpectedFailure;

    // Callers supply monotonic elapsed time. Changing reason must not restart
    // the grace period and allow an indefinitely broken route to remain quiet.
    internal UsbHapticsRetryNotice? Observe(Exception failure, TimeSpan elapsed)
    {
        waitingSince ??= elapsed;
        unexpectedFailure |= failure is not AudioEndpointPendingException;
        bool warning = unexpectedFailure || elapsed - waitingSince.Value >= ReadinessGrace;
        string reason = failure.Message;
        if (reason == lastReason && warning == lastWarning) return null;
        lastReason = reason;
        lastWarning = warning;
        return new(reason, warning);
    }

    internal void Ready()
    {
        waitingSince = null;
        lastReason = null;
        lastWarning = unexpectedFailure = false;
    }
}
