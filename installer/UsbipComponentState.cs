using System;
using Microsoft.Win32;

namespace DS4Windows.Installation;

internal static class UsbipComponentState
{
    internal static string Read()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"SOFTWARE\DS4Windows");
        return key?.GetValue("UsbipComponentState") as string;
    }

    internal static bool IsPaused(string state) => state == "Removed" ||
        state?.StartsWith("Removing:", StringComparison.Ordinal) == true ||
        state?.StartsWith("Restoring:", StringComparison.Ordinal) == true;
}
