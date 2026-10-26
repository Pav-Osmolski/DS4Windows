using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace DS4Windows.Bootstrapper;

internal static class OptionalDriverVersions
{
    internal const string BundledHidHide = "1.5.230";
    internal const string BundledFakerInput = "0.1.0";

    internal static string Label(string name, OptionalDriverPresence presence, string installedVersion, string bundledVersion) =>
        presence switch
        {
            OptionalDriverPresence.Installed => name + " " + (Numeric(installedVersion) ?? "(version unavailable)"),
            OptionalDriverPresence.Missing => name + " " + bundledVersion,
            _ => name + " (version unavailable)"
        };

    internal static string Numeric(string value) => Version.TryParse(value?.Trim(), out var version) ? version.ToString() : null;

    // Read-only registry/file metadata queries. Never query Win32_Product, which
    // can trigger MSI repair, and never substitute the bundled version for an
    // installed driver whose version could not be determined.
    internal static string Installed(string name)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (string child in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var product = uninstall.OpenSubKey(child);
                        string display = product?.GetValue("DisplayName") as string;
                        if (!string.Equals(display, name, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(display, "Nefarius " + name, StringComparison.OrdinalIgnoreCase)) continue;
                        string version = Numeric(product.GetValue("DisplayVersion") as string);
                        if (version != null) return version;
                    }
                    catch (Exception error) when (ReadFailure(error)) { }
                }
            }
            catch (Exception error) when (ReadFailure(error)) { }
        }
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var service = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            string image = Environment.ExpandEnvironmentVariables((service?.GetValue("ImagePath") as string ?? "").Trim());
            if (image.StartsWith("\""))
            {
                int end = image.IndexOf('"', 1);
                if (end < 0) return null;
                image = image.Substring(1, end - 1);
            }
            if (image.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                image = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), image.Substring(12));
            else if (image.StartsWith(@"\??\") || image.StartsWith(@"\\?\")) image = image.Substring(4);
            else if (!Path.IsPathRooted(image)) image = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), image);
            if (!image.EndsWith(".sys", StringComparison.OrdinalIgnoreCase) || !File.Exists(image)) return null;
            var info = FileVersionInfo.GetVersionInfo(image);
            return Numeric(info.ProductVersion) ?? Numeric(info.FileVersion);
        }
        catch (Exception error) when (ReadFailure(error)) { return null; }
    }

    private static bool ReadFailure(Exception error) => error is IOException or UnauthorizedAccessException or
        System.Security.SecurityException or ArgumentException or System.ComponentModel.Win32Exception;
}
