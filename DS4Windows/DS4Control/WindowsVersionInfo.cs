using System;
using System.Globalization;
using Microsoft.Win32;

namespace DS4Windows;

internal sealed record WindowsVersionInfo(string ProductName, string DisplayVersion,
    string Build, string KernelVersion)
{
    internal string[] LogLines => new[]
    {
        $"OS Version: {ProductName} {DisplayVersion} (Build {Build})",
        $"OS Kernel Version: {KernelVersion}",
    };

    internal static WindowsVersionInfo Read()
    {
        Version kernel = Environment.OSVersion.Version;
        try
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
            using RegistryKey key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            int build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string,
                NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : kernel.Build;
            int? revision = key?.GetValue("UBR") is int ubr && ubr >= 0 ? ubr : null;
            return Create(key?.GetValue("ProductName") as string,
                key?.GetValue("DisplayVersion") as string, key?.GetValue("ReleaseId") as string,
                key?.GetValue("InstallationType") as string, build, revision, kernel);
        }
        catch
        {
            // Diagnostics must not prevent startup when registry access fails.
            return Create(null, null, null, null, kernel.Build, null, kernel);
        }
    }

    internal static WindowsVersionInfo Create(string productName, string displayVersion,
        string releaseId, string installationType, int build, int? revision, Version kernel)
    {
        string product = string.IsNullOrWhiteSpace(productName) ? "Windows" : productName;
        // Windows 11 retains the legacy Windows 10 product string on many PCs.
        // Restrict normalization to client editions; server build numbers overlap.
        if (build >= 22000 && product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(installationType) ||
             string.Equals(installationType, "Client", StringComparison.OrdinalIgnoreCase)))
            product = "Windows 11" + product.Substring("Windows 10".Length);

        string display = !string.IsNullOrWhiteSpace(displayVersion) ? displayVersion :
            !string.IsNullOrWhiteSpace(releaseId) ? releaseId : "version unavailable";
        string buildText = build.ToString(CultureInfo.InvariantCulture) +
            (revision.HasValue ? "." + revision.Value.ToString(CultureInfo.InvariantCulture) :
                " (revision unavailable)");
        string kernelText = $"{kernel.Major}.{kernel.Minor}.{build}" +
            (revision.HasValue ? "." + revision.Value.ToString(CultureInfo.InvariantCulture) : "");
        return new(product, display, buildText, kernelText);
    }
}
