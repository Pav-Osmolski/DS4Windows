namespace DS4Windows;

internal static class ProfileSwitchFeedback
{
    // Run only from the accepted successful load's completion callback, never
    // from the button edge: missing/superseded profiles must not be announced.
    internal static void ReportApplied(bool loaded, int slot, string name, int battery)
    {
        if (!loaded) return;
        string message = string.Format(DS4WinWPF.Properties.Resources.UsingProfile,
            (slot + 1).ToString(), name, battery.ToString());
        AppLogger.LogToGui(message, false);
        if (Global.ProfileChangedNotification)
            AppLogger.LogToTray(message);
    }
}
