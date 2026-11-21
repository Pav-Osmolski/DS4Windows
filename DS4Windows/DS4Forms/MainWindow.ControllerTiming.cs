using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WinWPF.DS4Forms;

public partial class MainWindow
{
    private string completedControllerTimingSummary;

    private void ControllerTimingCapture_Click(object sender, RoutedEventArgs e)
    {
        bool capturing = controllerTimingCapture.IsChecked == true;
        completedControllerTimingSummary = capturing ? null : BuildControllerTimingSummary();
        ViiperLatencyHistogram.SetEnabled(capturing);
        copyControllerTimingBtn.IsEnabled = capturing || completedControllerTimingSummary != null;
        logActionStatus.Text = string.Empty;
    }

    private void CopyControllerTimingBtn_Click(object sender, RoutedEventArgs e)
    {
        string summary = ViiperLatencyHistogram.Enabled ? BuildControllerTimingSummary() : completedControllerTimingSummary;
        if (summary == null)
        {
            logActionStatus.Text = UiText.Get("No controller timing samples yet.");
            return;
        }

        try
        {
            Clipboard.SetDataObject(summary, true);
            logActionStatus.Text = UiText.Get("Controller timing summary copied.");
        }
        catch (ExternalException)
        {
            logActionStatus.Text = UiText.Get("The clipboard is currently unavailable.");
        }
    }

    private static string BuildControllerTimingSummary()
    {
        var service = App.rootHub;
        if (service == null || ViiperLatencyHistogram.CaptureStartedUtc is not { } started)
            return null;

        var lines = new List<string> { $"DS4Windows {Global.exeversion} controller timing diagnostics" };
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"Capture: {started:O} to {DateTimeOffset.UtcNow:O}"));
        int headerLength = lines.Count;
        for (int index = 0; index < service.DS4Controllers.Length; index++)
        {
            DS4Device device = Volatile.Read(ref service.DS4Controllers[index]);
            if (device == null)
                continue;

            int heading = lines.Count;
            AppendTimingMetric(lines, "Input report gaps", device.InputTiming.Snapshot());
            if (device is DualSenseDevice dualSense)
            {
                AppendTimingMetric(lines, "HID read wait", dualSense.PhysicalReadObservationWaitLatencySnapshot);
                AppendTimingMetric(lines, "Report callback", dualSense.PhysicalReportCallbackLatencySnapshot);
            }

            var output = Volatile.Read(ref service.outputDevices[index]) as ViiperOutDevice;
            if (output != null)
            {
                var (queueWait, socketWrite) = output.CaptureOutputTiming();
                AppendTimingMetric(lines, "Virtual output queue wait", queueWait);
                AppendTimingMetric(lines, "VIIPER socket write", socketWrite);
            }

            if (lines.Count > heading)
            {
                string outputName = output == null ? "" : $" -> {output.GetDeviceType()}";
                lines.Insert(heading, $"{Environment.NewLine}Controller {index + 1}: {device.DisplayName} ({device.ConnectionType}){outputName}");
            }
        }

        return lines.Count > headerLength ? string.Join(Environment.NewLine, lines) : null;
    }

    private static void AppendTimingMetric(List<string> lines, string name, ViiperLatencySnapshot snapshot)
    {
        if (snapshot.Count > 0)
            lines.Add(snapshot.FormatSummary(name));
    }
}
