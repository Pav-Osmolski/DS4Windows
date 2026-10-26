using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Media;
using WixToolset.BootstrapperApplicationApi;

namespace DS4Windows.Bootstrapper
{
    public partial class InstallerWindow : Window
    {
        private readonly InstallerApplication application;
        private InstallerMode mode;
        private bool applying;
        private readonly OptionalDriverSelection optionalDrivers = new OptionalDriverSelection();

        internal InstallerWindow(InstallerApplication application)
        {
            this.application = application;
            InitializeComponent();
            ApplyWindowsTheme();
            Closing += (_, e) =>
            {
                if (applying) e.Cancel = true;
            };
            Closed += (_, __) => application.OnWindowClosed();
        }

        internal void ShowConfirmation(InstallerMode detectedMode, IReadOnlyDictionary<string, PackageState> packages, bool infrastructureHealthy, bool relatedFakerInput = false,
            string hidHideVersion = null, string fakerInputVersion = null)
        {
            mode = detectedMode;
            HidePages();
            ConfirmationPage.Visibility = Visibility.Visible;
            OptionsCard.Visibility = Visibility.Visible;
            applying = false;

            switch (mode)
            {
                case InstallerMode.Update:
                    ModeTitle.Text = "Update DS4Windows";
                    ModeDescription.Text = "A managed DS4Windows installation was found. Only package-owned files will be replaced.";
                    ActionButton.Content = "Update";
                    break;
                case InstallerMode.Repair:
                    ModeTitle.Text = "Repair DS4Windows";
                    ModeDescription.Text = "This version is already installed. Setup will verify and repair its managed components.";
                    ActionButton.Content = "Repair";
                    break;
                case InstallerMode.Uninstall:
                    ModeTitle.Text = "Uninstall DS4Windows";
                    ModeDescription.Text = "DS4Windows and its managed VIIPER installation will be removed. Profiles, settings, and shared system drivers are preserved.";
                    ActionButton.Content = "Uninstall";
                    OptionsCard.Visibility = Visibility.Collapsed;
                    break;
                default:
                    ModeTitle.Text = "Install DS4Windows";
                    ModeDescription.Text = "Everything needed for a standard x64 installation is included and works offline.";
                    ActionButton.Content = "Install";
                    break;
            }

            Ds4Status.Text = PackageStatus(packages, "DS4WindowsMsi");
            ViiperLabel.Text = InfrastructureProbe.ViiperDisplayName;
            ViiperStatus.Text = infrastructureHealthy ? "Ready" : "Will install or repair";
            UsbipStatus.Text = infrastructureHealthy ? "Ready" : "Will verify before changing";
            optionalDrivers.Detect(packages, relatedFakerInput);
            Ds4Label.Text = "DS4Windows " + typeof(InstallerWindow).Assembly.GetName().Version.ToString(3);
            HidHideLabel.Text = OptionalDriverVersions.Label("HidHide", optionalDrivers.HidHide, hidHideVersion, OptionalDriverVersions.BundledHidHide);
            FakerInputLabel.Text = OptionalDriverVersions.Label("FakerInput", optionalDrivers.FakerInput, fakerInputVersion, OptionalDriverVersions.BundledFakerInput);
            HidHideCheckBox.IsChecked = optionalDrivers.InstallHidHide;
            FakerInputCheckBox.IsChecked = optionalDrivers.InstallFakerInput;
            HidHideCheckBox.IsEnabled = optionalDrivers.HidHide != OptionalDriverPresence.Installed;
            FakerInputCheckBox.IsEnabled = optionalDrivers.FakerInput != OptionalDriverPresence.Installed;
            HidHideCheckBox.ToolTip = HidHideCheckBox.IsEnabled ? null : "HidHide is already installed and will be kept unchanged.";
            FakerInputCheckBox.ToolTip = FakerInputCheckBox.IsEnabled ? null : "FakerInput is already installed and will be kept unchanged.";
            UpdateOptionalStatuses();
            bool uninstall = mode == InstallerMode.Uninstall;
            StatusHeading.Text = uninstall ? "Uninstall summary" : "Installation status";
            ProfileSummary.Visibility = UninstallTools.Visibility = uninstall ? Visibility.Visible : Visibility.Collapsed;
            InstallLocationNote.Visibility = StartupTasksNote.Visibility = uninstall ? Visibility.Collapsed : Visibility.Visible;
            UninstallToolMessage.Visibility = Visibility.Collapsed;
            if (uninstall)
            {
                Ds4Status.Text = packages.TryGetValue("DS4WindowsMsi", out var appState) && appState == PackageState.Present
                    ? "Will remove" : "Not installed";
                ViiperStatus.Text = File.Exists(Path.Combine(ProgramRoot, "VIIPER", "viiper.exe"))
                    ? "Will remove — managed copy" : "Will remove managed files, if present";
                UsbipStatus.Text = "Will keep — shared component";
                HidHideStatus.Text = KeptDriverStatus(optionalDrivers.HidHide);
                FakerInputStatus.Text = KeptDriverStatus(optionalDrivers.FakerInput);
            }
        }

        private static string ProgramRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DS4Windows");

        private static string KeptDriverStatus(OptionalDriverPresence presence) => presence switch
        {
            OptionalDriverPresence.Missing => "Not installed",
            OptionalDriverPresence.Installed => "Will keep — shared driver",
            _ => "Will keep — if installed"
        };

        private void OpenProfiles_Click(object sender, RoutedEventArgs e)
        {
            // Show both existing locations when necessary. Do not guess which
            // configuration is active or create a new folder during uninstall.
            var locations = new[]
            {
                (Name: "AppData", Path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DS4Windows", "Profiles")),
                (Name: "Program folder", Path: Path.Combine(ProgramRoot, "Profiles"))
            }.Where(item => Directory.Exists(item.Path)).ToArray();
            if (locations.Length == 0)
            {
                ShowUninstallToolMessage("No profiles folder was found for this Windows user or in the installed program folder. Profiles belonging to other Windows users are also preserved.");
                return;
            }
            if (locations.Length == 1) { OpenUninstallTarget(locations[0].Path); return; }
            var menu = new ContextMenu { PlacementTarget = (Button)sender, Placement = PlacementMode.Bottom };
            foreach (var location in locations)
            {
                string path = location.Path;
                var item = new MenuItem { Header = location.Name + " — " + path };
                item.Click += (_, __) => OpenUninstallTarget(path);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void ManageDrivers_Click(object sender, RoutedEventArgs e) => OpenUninstallTarget("ms-settings:appsfeatures");

        private void OpenUninstallTarget(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
                UninstallToolMessage.Visibility = Visibility.Collapsed;
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                ShowUninstallToolMessage("Windows could not open this location. You can open it manually: " + target);
            }
        }

        private void ShowUninstallToolMessage(string message)
        {
            UninstallToolMessage.Text = message;
            UninstallToolMessage.Visibility = Visibility.Visible;
        }

        private void OptionalDriver_Click(object sender, RoutedEventArgs e)
        {
            optionalDrivers.InstallHidHide = HidHideCheckBox.IsChecked == true;
            optionalDrivers.InstallFakerInput = FakerInputCheckBox.IsChecked == true;
            UpdateOptionalStatuses();
        }

        private void UpdateOptionalStatuses()
        {
            HidHideStatus.Text = OptionalDriverSelection.Status(optionalDrivers.HidHide, optionalDrivers.InstallHidHide, mode == InstallerMode.Uninstall);
            FakerInputStatus.Text = OptionalDriverSelection.Status(optionalDrivers.FakerInput, optionalDrivers.InstallFakerInput, mode == InstallerMode.Uninstall);
        }

        internal void ShowPlanning()
        {
            HidePages();
            ProgressPage.Visibility = Visibility.Visible;
            ProgressTitle.Text = "Preparing installation…";
            ProgressDetail.Text = "Building a safe installation plan";
            OverallProgress.IsIndeterminate = true;
            applying = true;
        }

        internal void ShowApplying()
        {
            OverallProgress.IsIndeterminate = false;
            ProgressTitle.Text = mode == InstallerMode.Uninstall ? "Removing DS4Windows…" : "Installing DS4Windows…";
            ProgressDetail.Text = "Administrator permission is requested once";
        }

        internal void SetCurrentPackage(string packageId)
        {
            switch (packageId)
            {
                case "CloseRunningApplications": ProgressDetail.Text = "Closing running DS4Windows and VIIPER processes"; break;
                case "DS4WindowsMsi": ProgressDetail.Text = "Installing DS4Windows"; break;
                case "ViiperUsbipSetup": ProgressDetail.Text = "Verifying VIIPER and USB-IP"; break;
                case "HidHide": ProgressDetail.Text = "Installing optional HidHide"; break;
                case "FakerInput": ProgressDetail.Text = "Installing optional FakerInput"; break;
                default: ProgressDetail.Text = "Verifying installation"; break;
            }
        }

        internal void ShowInstallerBusyRetry(int attempt, int maximumRetries)
        {
            ProgressDetail.Text = "Another setup or repair is busy; " +
                "retrying safely (" + attempt + " of " + maximumRetries + ")";
        }

        internal void SetProgress(int percent)
        {
            OverallProgress.Value = Math.Max(0, Math.Min(100, percent));
            ProgressPercent.Text = percent + "%";
        }

        internal void ShowComplete(LaunchAction action, bool startupWarning = false)
        {
            HidePages();
            CompletePage.Visibility = Visibility.Visible;
            LaunchCheckBox.Visibility = Visibility.Visible;
            applying = false;
            CompleteTitle.Text = "DS4Windows is ready";
            CompleteDescription.Text = startupWarning
                ? "Installation completed. Automatic startup could not be configured. You can launch DS4Windows now; run Repair to retry startup setup."
                : "Installation and verification completed successfully.";
            if (action == LaunchAction.Uninstall)
            {
                CompleteTitle.Text = "DS4Windows was removed";
                CompleteDescription.Text = "Profiles, settings, and shared system drivers were preserved.";
                LaunchCheckBox.Visibility = Visibility.Collapsed;
            }
        }

        internal void ShowRestart(bool startupWarning = false)
        {
            HidePages();
            RestartPage.Visibility = Visibility.Visible;
            RestartDescription.Text = "Windows must restart before setup can safely continue. Setup will resume after you sign in.";
            if (startupWarning)
                RestartDescription.Text += " Automatic startup could not be configured. Run Repair after restarting to retry it.";
            RestartNowButton.IsEnabled = true;
            applying = false;
        }

        internal void ShowFailure(string message)
        {
            HidePages();
            FailurePage.Visibility = Visibility.Visible;
            FailureMessage.Text = message;
            applying = false;
        }

        private void Action_Click(object sender, RoutedEventArgs e)
        {
            var action = mode == InstallerMode.Uninstall ? LaunchAction.Uninstall :
                         mode == InstallerMode.Repair ? LaunchAction.Repair : LaunchAction.Install;
            application.Begin(action, DesktopShortcutCheckBox.IsChecked == true,
                HidHideCheckBox.IsChecked == true, FakerInputCheckBox.IsChecked == true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => application.Close(1223);
        private void CloseFailure_Click(object sender, RoutedEventArgs e) => application.CloseWithCurrentResult();
        private void Retry_Click(object sender, RoutedEventArgs e) { HidePages(); DetectingPage.Visibility = Visibility.Visible; application.Retry(); }
        private void OpenLog_Click(object sender, RoutedEventArgs e) => application.OpenLog();
        private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(application.Diagnostics()); }
            catch
            {
                FailureMessage.Text += "\r\n\r\nWindows could not access the clipboard. Use Open log instead.";
            }
        }
        private void RestartLater_Click(object sender, RoutedEventArgs e) => application.Close(3010);
        private void RestartNow_Click(object sender, RoutedEventArgs e)
        {
            if (application.RestartWindows())
            {
                application.Close(3010);
            }
            else
            {
                RestartDescription.Text = "Windows could not start the restart automatically. Restart manually; setup will resume after you sign in.";
                RestartNowButton.IsEnabled = false;
            }
        }
        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            if (LaunchCheckBox.Visibility == Visibility.Visible && LaunchCheckBox.IsChecked == true) application.LaunchDs4Windows();
            application.Close();
        }

        private void HidePages()
        {
            DetectingPage.Visibility = Visibility.Collapsed;
            ConfirmationPage.Visibility = Visibility.Collapsed;
            ProgressPage.Visibility = Visibility.Collapsed;
            CompletePage.Visibility = Visibility.Collapsed;
            RestartPage.Visibility = Visibility.Collapsed;
            FailurePage.Visibility = Visibility.Collapsed;
        }

        private static string PackageStatus(IReadOnlyDictionary<string, PackageState> packages, string id)
        {
            return packages.TryGetValue(id, out var state) && state == PackageState.Present ? "Installed" : "Will install";
        }

        private void ApplyWindowsTheme()
        {
            var light = true;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    light = Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) != 0;
                }
            }
            catch { }

            Resources["WindowBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#F4F7FB" : "#08121F"));
            Resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FFFFFF" : "#0E1B2A"));
            Resources["HoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#EAF2FC" : "#17283B"));
            Resources["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#D8E2EE" : "#22354A"));
            Resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#101D2D" : "#F4F8FC"));
            Resources["MutedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#56708D" : "#9FB8D3"));
        }
    }
}
