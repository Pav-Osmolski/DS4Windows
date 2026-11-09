using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DS4Windows;
using DS4Windows.Installation;
using DS4WinWPF.DS4Control;

namespace DS4WinWPF.DS4Forms;

public partial class MainWindow
{
    private bool componentActionRunning;
    private bool componentRefreshRunning;
    private string usbipAction;
    private string confirmedUsbipAction;

    private void ManageComponents_Click(object sender, RoutedEventArgs e) =>
        settingsComponentsNav.IsChecked = true;

    private void ComponentHidingSettings_Click(object sender, RoutedEventArgs e)
    {
        settingsControllersNav.IsChecked = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (settingsControllersNav.IsChecked == true && preventDoubleInputCard.IsVisible)
                SettingsNavigator.ScrollCardToTop(settingsScrollViewer, preventDoubleInputCard);
        }));
    }

    private async void RefreshViiperStatusText()
    {
        if (viiperStatusText == null || componentRefreshRunning || componentActionRunning) return;
        componentRefreshRunning = true;
        try
        {
            var result = await Task.Run(() =>
            {
                var status = ViiperSetupManager.GetFreshDependencyStatus();
                Global.RefreshHidHideInfo();
                Global.RefreshFakerInputInfo();
                bool? systemHiding = null;
                if (Global.hidHideInstalled && settingsWrapVM.SystemIntegrationEnabled)
                {
                    try
                    {
                        using var hidHide = new HidHideAPIDevice(writeAccess: false);
                        if (hidHide.TryGetActiveState(out bool active)) systemHiding = active;
                    }
                    catch (Exception error) { App.rootHub.LogDebug("HidHide status check failed: " + error.Message, warning: true); }
                }
                string boot = null;
                if (status.UsbipPaused && status.UsbipPauseState != "Removed")
                {
                    try { boot = StartupSetupRecovery.BootSessionId(); }
                    catch (Exception error) { App.rootHub.LogDebug("USB/IP boot check failed: " + error.Message, warning: true); }
                }
                return (status, boot, systemHiding);
            });
            ApplyComponentStatus(result.status, result.boot, result.systemHiding);
            settingsWrapVM.DriverCheckRefresh();
        }
        catch (Exception error)
        {
            componentControllerSummary.Text = componentReadiness.Text = UiText.Get("Status unavailable");
            usbipAction = null;
            componentUsbipButton.IsEnabled = false;
            ShowComponentResult(error.Message);
        }
        finally { componentRefreshRunning = false; }
    }

    private void ApplyComponentStatus(ViiperPrerequisiteStatus status, string boot, bool? systemHiding)
    {
        string state = status.UsbipPauseState;
        bool bootPending = status.UsbipPaused && state != "Removed" &&
            (boot == null || state.EndsWith(":" + boot, StringComparison.Ordinal));
        bool removalPending = state?.StartsWith("Removing:", StringComparison.Ordinal) == true;
        string summary = UiText.Get(status.DisplayText);
        string readiness = status.Ready ? "Ready" : "Needs attention";
        bool canRemove = status.UsbipInstalled && status.UsbipExecutableSafe && status.UsbipDriverFilesSafe;
        usbipAction = canRemove ? "Remove" : null;
        string usbipButtonText = "Remove USB/IP…";
        string usbipText = status.UsbipVersion == null ? UiText.Get("Not detected")
            : UiText.Format("{0} · {1}", status.UsbipVersion,
                UiText.Get(status.UsbipRuntimeReady ? "Driver ready" : "Driver needs attention"));
        if (status.UsbipPaused)
        {
            readiness = bootPending ? "Restart required" : "Output unavailable";
            bool retryRemoval = removalPending && canRemove;
            usbipAction = bootPending ? "Restart" : removalPending ? retryRemoval ? "Remove" : "Verify" : "Restore";
            usbipButtonText = bootPending ? "Restart Windows…" : removalPending
                ? retryRemoval ? "Retry removal…" : "Verify removal…"
                : state == "Removed" ? "Restore USB/IP…" : "Verify restoration…";
            usbipText = UiText.Get(bootPending
                ? removalPending ? "Removal pending restart" : "Restoration pending restart"
                : removalPending ? retryRemoval ? "USB/IP is still installed" : "Removal awaiting verification"
                : state == "Removed" ? "Removed by choice" : "Restoration awaiting verification");
            summary = UiText.Format("Virtual controller output is paused. {0}.", usbipText);
        }

        componentControllerSummary.Text = summary;
        viiperStatusText.Text = summary;
        viiperStatusText.ToolTip = status.StartupPreferenceReadError;
        componentReadiness.Text = UiText.Get(readiness);
        componentReadiness.SetResourceReference(ForegroundProperty,
            status.Ready ? "SuccessColor" : status.UsbipPaused ? "MutedForegroundColor" : "DangerColor");
        componentUsbipStatus.Text = usbipText;
        componentUsbipButton.Content = UiText.Get(usbipButtonText);
        componentUsbipButton.Visibility = usbipAction == null ? Visibility.Collapsed : Visibility.Visible;
        componentUsbipButton.IsEnabled = settingsWrapVM.SystemIntegrationEnabled;
        viiperSetupBtn.IsEnabled = !status.UsbipPaused && settingsWrapVM.SystemIntegrationEnabled;

        string version = null;
        if (status.ViiperInstalled)
        {
            try { version = FileVersionInfo.GetVersionInfo(status.ViiperPath).ProductVersion; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        bool portable = PortableBrokerContext.IsActive;
        componentViiperStatus.Text = UiText.Format("{0} · {1} · {2}", version ?? UiText.Get("Version unavailable"),
            UiText.Get(status.ServerRunning ? "Running" : "Stopped"),
            UiText.Get(portable ? "Portable copy" : "Managed backend"));

        componentHidHideStatus.Text = Global.hidHideInstalled
            ? UiText.Format("{0} · {1} · {2}", DriverDiagnostics.Describe("HidHide"),
                UiText.Get(Global.UseExclusiveMode ? "DS4Windows hiding enabled" : "DS4Windows hiding off"),
                UiText.Get(systemHiding switch
                {
                    true => "System-wide hiding on",
                    false => "System-wide hiding off",
                    _ => "System-wide hiding status unavailable",
                }))
            : UiText.Get("Not installed · Helps prevent double input");
        bool hasClient = !string.IsNullOrEmpty(Util.GetHidHideClientPath());
        componentHidHideButton.Content = UiText.Get(Global.hidHideInstalled ? "Open HidHide…" : "Install HidHide…");
        componentHidHideButton.IsEnabled = settingsWrapVM.SystemIntegrationEnabled && (!Global.hidHideInstalled || hasClient);

        string activeHandler = App.rootHub.running ? Global.outputKBMHandler?.GetFullDisplayName() : null;
        componentFakerInputStatus.Text = UiText.Format("{0} · {1}",
            Global.fakerInputInstalled ? UiText.Format("Installed ({0})", Global.fakerInputVersion) : UiText.Get("Not installed"),
            activeHandler == null ? UiText.Get("Controller service stopped") : UiText.Format("Using {0}", activeHandler));
        componentFakerInputButton.Content = UiText.Get(!Global.fakerInputInstalled
            ? "Install FakerInput…" : Global.PreferWindowsKeyboardMouse ? "Use automatic input selection" : "Use Windows input");
        componentFakerInputButton.IsEnabled = settingsWrapVM.SystemIntegrationEnabled;
        if (confirmedUsbipAction != usbipAction) componentConfirmation.Visibility = Visibility.Collapsed;
    }

    private void UsbipComponent_Click(object sender, RoutedEventArgs e)
    {
        if (componentActionRunning || componentRefreshRunning || usbipAction == null) return;
        confirmedUsbipAction = usbipAction;
        var (title, detail, button) = usbipAction switch
        {
            "Remove" => ("Remove USB/IP?", "This affects the whole PC. DS4Windows will stop virtual output. Close games and any other apps using USB/IP first. Administrator approval and a restart are required.", "Remove USB/IP"),
            "Verify" => ("Verify USB/IP removal?", "DS4Windows will check that Windows has removed the driver and its package. Windows will ask for administrator approval.", "Verify removal"),
            "Restart" => ("Restart Windows?", "Save your work before restarting. Open DS4Windows again after Windows starts to finish the check.", "Restart Windows"),
            _ => ("Restore USB/IP?", "DS4Windows will verify or reinstall its supported driver package. Windows will ask for administrator approval. A restart may be needed.", "Restore USB/IP"),
        };
        componentConfirmTitle.Text = UiText.Get(title);
        componentConfirmText.Text = UiText.Get(detail);
        componentConfirmButton.Content = UiText.Get(button);
        componentConfirmation.Visibility = Visibility.Visible;
    }

    private void CancelUsbipComponent_Click(object sender, RoutedEventArgs e)
    {
        confirmedUsbipAction = null;
        componentConfirmation.Visibility = Visibility.Collapsed;
        componentUsbipButton.Focus();
    }

    private async void ConfirmUsbipComponent_Click(object sender, RoutedEventArgs e)
    {
        if (componentActionRunning || componentRefreshRunning || confirmedUsbipAction == null) return;
        string action = confirmedUsbipAction;
        CancelUsbipComponent_Click(sender, e);
        await RunComponentActionAsync(async () =>
        {
            if (action == "Restart")
            {
                if (!Global.Save()) throw new IOException(UiText.Get("DS4Windows could not save its settings. Restart Windows when ready."));
                using var process = Process.Start(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/r /t 0");
                if (process == null) throw new IOException(UiText.Get("Windows could not schedule a restart."));
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new IOException(UiText.Get("Windows could not schedule a restart."));
                return UiText.Get("Windows restart requested.");
            }

            if (action == "Remove")
            {
                if (!StartStopBtn.IsEnabled)
                    throw new InvalidOperationException(UiText.Get("Wait for the current controller service change to finish, then try again."));
                if (App.rootHub.running && !await Task.Run(() => App.rootHub.Stop(immediateUnplug: true)))
                    throw new InvalidOperationException(UiText.Get("DS4Windows could not stop virtual output. Stop the service and try again."));
            }

            int result = await ViiperSetupManager.RunUsbipComponentActionAsync(action);
            return result switch
            {
                3010 => UiText.Get("Restart Windows to finish this change."),
                0 when action == "Verify" => UiText.Get("USB/IP removal verified."),
                0 => UiText.Get("USB/IP driver verified. Reopen DS4Windows to start and verify virtual controller output."),
                _ => throw new IOException(UiText.Format("USB/IP setup returned {0}. Copy the component diagnostics for details.", result)),
            };
        });
    }

    private async Task RunComponentActionAsync(Func<Task<string>> operation)
    {
        if (componentActionRunning || componentRefreshRunning || !settingsWrapVM.SystemIntegrationEnabled) return;
        componentActionRunning = true;
        settingsComponentsPage.IsEnabled = false;
        ShowComponentResult(UiText.Get("Working…"));
        try { ShowComponentResult(await operation()); }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            ShowComponentResult(UiText.Get("Administrator approval was cancelled."));
        }
        catch (Exception error)
        {
            App.rootHub.LogDebug("Component action failed: " + error.Message, warning: true);
            ShowComponentResult(error.Message);
        }
        finally
        {
            settingsComponentsPage.IsEnabled = true;
            componentActionRunning = false;
            RefreshViiperStatusText();
        }
    }

    private void ShowComponentResult(string message)
    {
        componentOperationStatus.Text = message;
        componentOperationStatus.Visibility = Visibility.Visible;
    }

    private async void ComponentHidHide_Click(object sender, RoutedEventArgs e)
    {
        if (Global.hidHideInstalled) HidHideBtn_Click(sender, e);
        else await InstallComponentDriverAsync(hidHide: true);
    }

    private async void ComponentFakerInput_Click(object sender, RoutedEventArgs e)
    {
        if (!Global.fakerInputInstalled)
        {
            await InstallComponentDriverAsync(hidHide: false);
            return;
        }
        bool previous = Global.PreferWindowsKeyboardMouse;
        Global.PreferWindowsKeyboardMouse = !previous;
        if (Global.Save())
            ShowComponentResult(UiText.Get("Input preference saved. Stop and start the controller service to apply it."));
        else
        {
            Global.PreferWindowsKeyboardMouse = previous;
            ShowComponentResult(UiText.Get("The input preference could not be saved."));
        }
        RefreshViiperStatusText();
    }

    private Task InstallComponentDriverAsync(bool hidHide) => RunComponentActionAsync(async () =>
        await WelcomeDialog.InstallOptionalDriverAsync(hidHide, ShowComponentResult)
            ? UiText.Get("Driver setup completed. Restart Windows to finish.")
            : UiText.Get("Driver setup completed. Stop and start the controller service to use the driver."));

    private void OpenViiperComponentLog_Click(object sender, RoutedEventArgs e) =>
        OpenComponentTarget(File.Exists(ViiperDiagnostics.LogPath) ? ViiperDiagnostics.LogPath : Path.GetDirectoryName(ViiperDiagnostics.LogPath));

    private void ManageSharedDrivers_Click(object sender, RoutedEventArgs e) =>
        OpenComponentTarget("ms-settings:appsfeatures");

    private void OpenComponentTarget(string path)
    {
        try { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception error) { ShowComponentResult(error.Message); }
    }

    private void CopyComponentDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = ViiperSetupManager.GetInfrastructureActionsLogPath();
            string hostLog = ViiperSetupManager.GetInstallerHostLogPath();
            Clipboard.SetText(string.Join(Environment.NewLine, componentControllerSummary.Text,
                "VIIPER: " + componentViiperStatus.Text, "USB/IP: " + componentUsbipStatus.Text,
                "HidHide: " + componentHidHideStatus.Text, "FakerInput: " + componentFakerInputStatus.Text,
                componentOperationStatus.Text, File.Exists(path) ? path : "", File.Exists(hostLog) ? hostLog : ""));
            ShowComponentResult(UiText.Get("Component diagnostics copied. Review them before sharing."));
        }
        catch (Exception error) { ShowComponentResult(error.Message); }
    }
}
