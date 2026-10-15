using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DS4Windows;

internal static class ViiperTrayPreference
{
    internal const string RegistryPath = @"Software\DS4Windows";
    internal const string RegistryValue = "ShowViiperTrayIcon";

    // Installed VIIPER can start at logon before DS4Windows loads Profiles.xml.
    // Mirror only this per-user UI preference; do not rewrite tasks or processes.
    internal static void WriteStartupPreference(bool visible, string path = RegistryPath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException("Windows could not save the VIIPER tray preference.");
        key.SetValue(RegistryValue, visible ? 1 : 0, RegistryValueKind.DWord);
    }

    internal static void Apply(bool previous, bool requested,
        Action<bool> saveApplication, Action<bool> saveStartup)
    {
        saveStartup(requested);
        try { saveApplication(requested); }
        catch
        {
            try { saveStartup(previous); }
            catch { /* Preserve the application-save failure. Startup resyncs. */ }
            throw;
        }
    }

    internal static void AddArgument(ProcessStartInfo start, bool visible) =>
        start.ArgumentList.Add(visible ? "--no-tray=false" : "--no-tray=true");
}
