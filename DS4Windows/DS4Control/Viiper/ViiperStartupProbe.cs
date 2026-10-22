using System;
using System.Diagnostics;

namespace DS4Windows
{
    // Only an explicit CLI deadline may be transient. Version, integrity,
    // ABI and process-ownership failures retain the normal repair gate.
    internal static class ViiperStartupProbe
    {
        internal const int RetryDelayMilliseconds = 500;

        internal static ViiperPrerequisiteStatus Check(
            Func<ViiperPrerequisiteStatus> inspect, Action<int> delay,
            Action<string> record)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            for (int attempt = 1; ; attempt++)
            {
                ViiperPrerequisiteStatus status = inspect();
                record($"Startup prerequisite check {attempt}: elapsed={elapsed.ElapsedMilliseconds} ms; ready={status.Ready}; USB/IP timeout={status.UsbipProbeTimedOut}");
                // Persist the original snapshot before any retry, recovery or
                // prompt can replace it with a later healthy status.
                foreach (string line in ViiperStartupDiagnostics.Describe(status))
                    record("Startup prerequisite check " + attempt + ": " + line);
                if (attempt == 2 || !CanRetry(status)) return status;
                record("USB/IP runtime probe timed out; retrying once before offering repair.");
                delay(RetryDelayMilliseconds);
            }
        }

        private static bool CanRetry(ViiperPrerequisiteStatus status) =>
            status.UsbipProbeTimedOut && !status.UsbipRuntimeReady &&
            status.UsbipInstalled && status.UsbipExecutableSafe &&
            status.UsbipDriverFilesSafe && !status.CitrixUsbMonitorConflict &&
            !status.ViiperProcessConflict;
    }
}
