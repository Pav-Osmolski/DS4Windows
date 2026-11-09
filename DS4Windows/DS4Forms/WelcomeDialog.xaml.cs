/*
DS4Windows
Copyright (C) 2026 hbashton

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
*/

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;

namespace DS4WinWPF.DS4Forms
{
    public partial class WelcomeDialog : Window
    {
        private const string HidHideInstallerFileName =
            "HidHide_1.5.230_x64.exe";
        private const string HidHideInstallerSha256 =
            "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6";
        private const string FakerInputX64FileName =
            "FakerInput_0.1.0_x64.msi";
        private const string FakerInputX64Sha256 =
            "30CF218B624740A91BE4FCCA3ADFB4550BA8CC8F31AC9625FE39D238E64D13EA";
        private const string FakerInputX86FileName =
            "FakerInput_0.1.0_x86.msi";
        private const string FakerInputX86Sha256 =
            "0C0A01EEF8C57C9B3DB917131995A10ADB3599CC643D2F27AD28D9511B96DEC1";

        public WelcomeDialog(bool loadConfig = false)
        {
            if (loadConfig)
            {
                DS4Windows.Global.FindConfigLocation();
                DS4Windows.Global.Load();
            }

            InitializeComponent();
            step4HidHidePanel.IsEnabled = IsHidHideCompatible();
            step5FakerInputPanel.IsEnabled = DS4Windows.Global.IsWin8OrGreater();

            DS4Windows.ViiperPrerequisiteStatus status =
                DS4Windows.ViiperSetupManager.GetStatus(tryStartServer: true);
            if (status.Ready)
            {
                viiperInstallBtn.Content = "VIIPER is ready";
            }
        }

        private void ViiperInstallBtn_Click(object sender, RoutedEventArgs e)
        {
            DS4Windows.ViiperPrerequisiteStatus status =
                DS4Windows.ViiperSetupManager.GetStatus(tryStartServer: true);
            if (status.Ready)
            {
                viiperInstallBtn.Content = "VIIPER is ready";
                return;
            }

            bool launched = DS4Windows.ViiperSetupManager.
                EnsureReadyWithPrompt(this, forcePrompt: true);
            viiperInstallBtn.Content = launched
                ? "Setup opened — finish it, then click here to verify"
                : "VIIPER setup needs attention";
        }

        private async void HidHideInstall_Click(object sender, RoutedEventArgs e) =>
            await RunBundledInstallerAsync(hidHideInstallBtn, hidHide: true);

        private async void FakerInputInstallBtn_Click(object sender, RoutedEventArgs e) =>
            await RunBundledInstallerAsync(fakerInputInstallBtn, hidHide: false);

        private async Task RunBundledInstallerAsync(System.Windows.Controls.Button button, bool hidHide)
        {
            if (DS4Windows.PortableLabContext.IsActive) return;
            string component = hidHide ? "HidHide" : "FakerInput";
            try
            {
                SetInstallerControlsEnabled(false);
                bool restart = await InstallOptionalDriverAsync(hidHide, text => button.Content = text);
                button.Content = restart ? $"{component} setup complete — restart required" : $"{component} setup complete";
                if (restart)
                    MessageBox.Show(this,
                        global::DS4Windows.UiText.Format("{0} was installed successfully. Restart Windows to finish setup.", component),
                        global::DS4Windows.UiText.Format("{0} setup", component), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                button.Content = $"{component} setup failed";
                MessageBox.Show(this, global::DS4Windows.UiText.Format("Could not install {0}: {1}", component, error.Message),
                    global::DS4Windows.UiText.Format("{0} setup", component), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { SetInstallerControlsEnabled(true); }
        }

        internal static async Task<bool> InstallOptionalDriverAsync(bool hidHide, Action<string> progress = null)
        {
            bool x64 = Environment.Is64BitOperatingSystem;
            string component = hidHide ? "HidHide" : "FakerInput";
            string name = hidHide ? HidHideInstallerFileName : x64 ? FakerInputX64FileName : FakerInputX86FileName;
            string digest = hidHide ? HidHideInstallerSha256 : x64 ? FakerInputX64Sha256 : FakerInputX86Sha256;
            string target = Path.Combine(AppContext.BaseDirectory, "extras", name);
            if (DS4Windows.PortableLabContext.IsActive)
                throw new InvalidOperationException("Optional driver setup is unavailable in the portable lab.");
            if (hidHide ? DS4Windows.Global.IsHidHideInstalled() : DS4Windows.Global.IsFakerInputInstalled())
                return false;

            progress?.Invoke(global::DS4Windows.UiText.Format("Verifying bundled {0}…", component));
            // Hold a handle that denies writes and replacement through launch.
            using FileStream stream = new FileStream(target, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 81920, useAsync: true);
            byte[] hash = await SHA256.HashDataAsync(stream);
            if (!string.Equals(Convert.ToHexString(hash), digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(global::DS4Windows.UiText.Format("The bundled {0} installer failed verification.", component));

            progress?.Invoke(global::DS4Windows.UiText.Format("Installing {0}…", component));
            var start = new ProcessStartInfo { UseShellExecute = true, Verb = "runas" };
            if (hidHide)
            {
                start.FileName = target;
                start.ArgumentList.Add("/norestart");
            }
            else
            {
                start.FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
                start.ArgumentList.Add("/i");
                start.ArgumentList.Add(target);
                start.ArgumentList.Add("/norestart");
            }
            using Process process = Process.Start(start) ??
                throw new InvalidOperationException(global::DS4Windows.UiText.Format("Windows did not start the {0} installer.", component));
            await process.WaitForExitAsync();
            if (process.ExitCode is not (0 or 3010 or 1641))
                throw new InvalidOperationException(global::DS4Windows.UiText.Format("The {0} installer exited with code {1}.", component, process.ExitCode));
            if (hidHide) DS4Windows.Global.RefreshHidHideInfo();
            else DS4Windows.Global.RefreshFakerInputInfo();
            return process.ExitCode != 0;
        }

        private void SetInstallerControlsEnabled(bool enabled)
        {
            viiperInstallBtn.IsEnabled = enabled;
            step4HidHidePanel.IsEnabled = enabled && IsHidHideCompatible();
            step5FakerInputPanel.IsEnabled = enabled &&
                DS4Windows.Global.IsWin8OrGreater();
        }

        private static bool IsHidHideCompatible() =>
            DS4Windows.Global.IsWin10OrGreater() &&
            Environment.Is64BitOperatingSystem;

        private void Step2Btn_Click(object sender, RoutedEventArgs e) =>
            DS4Windows.Util.StartProcessHelper(
                "https://support.xbox.com/help/hardware-network/controller/connect-xbox-wireless-controller-to-pc");

        private void BluetoothSetLink_Click(object sender,
            RoutedEventArgs e) => Process.Start("control", "bthprops.cpl");

        private void FinishedBtn_Click(object sender, RoutedEventArgs e) =>
            Close();
    }

    public class WelcomeDialogResourcePaths
    {
        public string PairmodePNG =>
            $"{DS4Windows.Global.RESOURCES_PREFIX}/Pairmode.png";
    }
}
