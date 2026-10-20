using System.Diagnostics;
using System.Globalization;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

// Presentation only: input sampling, live plots and warning thresholds retain
// their existing cadence and precision. Called on the readings UI dispatcher.
internal sealed class ControllerReportIntervalDisplay
{
    private DS4Device lastDevice;
    private long lastUpdate;

    internal void Reset() => lastDevice = null;

    internal bool TryUpdate(DS4Device device, long timestamp, out string text)
    {
        text = null;
        if (ReferenceEquals(lastDevice, device) &&
            Stopwatch.GetElapsedTime(lastUpdate, timestamp).TotalMilliseconds < 250)
            return false;

        lastDevice = device;
        lastUpdate = timestamp;
        text = string.Format(CultureInfo.CurrentCulture,
            DS4WinWPF.Properties.Resources.InputDelay, device.Latency);
        return true;
    }
}
