using DS4Windows.Switch2;

namespace DS4WinWPF.DS4Forms.ViewModels;

internal static class Switch2BluetoothDiscoveryPresentation
{
    internal static string Describe(Switch2BluetoothDiscoveryStatus status,
        int candidateCount) => status?.State switch
    {
        Switch2BluetoothDiscoveryState.Stopped =>
            global::DS4Windows.UiText.Get("Bluetooth discovery is stopped. Use DS4Windows' Stop/Start controls to start discovery."),
        Switch2BluetoothDiscoveryState.Starting =>
            global::DS4Windows.UiText.Get("Starting Bluetooth discovery. Refresh in a moment to check its status."),
        Switch2BluetoothDiscoveryState.Scanning when candidateCount > 0 =>
            global::DS4Windows.UiText.Format("Bluetooth discovery is active. {0} controller{1} available to associate or reconnect.", candidateCount, (candidateCount == 1 ? string.Empty : "s")),
        Switch2BluetoothDiscoveryState.Scanning =>
            global::DS4Windows.UiText.Get("Bluetooth discovery is active. No new controllers found yet. Wake an associated controller to reconnect; use sync mode only to associate a new controller, then refresh."),
        Switch2BluetoothDiscoveryState.Unavailable =>
            global::DS4Windows.UiText.Get("Windows did not provide a usable Bluetooth adapter. Check that Bluetooth is available and turned on, then stop and start DS4Windows to retry discovery."),
        Switch2BluetoothDiscoveryState.StartFailed =>
            global::DS4Windows.UiText.Format("Bluetooth discovery could not start{0}. Stop and start DS4Windows to retry; check the log if it fails again.", DescribeFailure(status.Failure)),
        Switch2BluetoothDiscoveryState.Interrupted =>
            global::DS4Windows.UiText.Get("Bluetooth discovery stopped unexpectedly. Stop and start DS4Windows to retry discovery."),
        Switch2BluetoothDiscoveryState.Stopping =>
            global::DS4Windows.UiText.Get("Bluetooth discovery is still cleaning up. Wait a moment, then retry Stop before starting or associating another controller."),
        Switch2BluetoothDiscoveryState.CleanupFailed =>
            global::DS4Windows.UiText.Get("Bluetooth cleanup did not complete safely. Discovery cannot restart yet. Check the log; do not launch a second instance to bypass cleanup."),
        _ => global::DS4Windows.UiText.Get("Bluetooth discovery status is unavailable. Refresh to check again."),
    };

    internal static string ActionLabel(bool isRemembered) =>
        isRemembered ? global::DS4Windows.UiText.Get("Reconnect selected") : global::DS4Windows.UiText.Get("Associate selected");

    internal static string DescribeReconnect(Switch2BluetoothWindowsAssociationResult result) =>
        result.Succeeded ? global::DS4Windows.UiText.Get("Controller reconnected. Its existing association was preserved. Joy-Con halves may still need a pair selection.") :
        result.Failure == Switch2BluetoothWindowsAssociationFailure.SlotActivationRejected ?
            global::DS4Windows.UiText.Get("The controller was reached, but its controller slot could not activate. Its association was preserved. Check the log for profile, output or cleanup errors, correct the cause, then wake it and retry.") :
            global::DS4Windows.UiText.Format("Reconnect failed: {0}. Its association was not changed. Wake it and refresh after cleanup completes; check the log if it remains unavailable.", result.Failure);

    private static string DescribeFailure(Switch2BluetoothWindowsScanStartFailure failure) =>
        failure == Switch2BluetoothWindowsScanStartFailure.None ? string.Empty :
            $" ({failure})";
}
