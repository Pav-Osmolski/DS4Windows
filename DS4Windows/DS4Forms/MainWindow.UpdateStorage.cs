using System;
using System.IO;
using System.Threading.Tasks;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

public partial class MainWindow
{
    private bool updateCleanupAttempted;
    private async void ScheduleUpdateCleanup()
    {
        if (updateCleanupAttempted || PortableLabContext.IsActive) return;
        updateCleanupAttempted = true;
        try
        {
            var result = await Task.Run(() => UpdateStorage.Current.CleanAutomatically(
                SettingsStorage.Current, () => ViiperSetupManager.GetStatus().Ready));
            if (result != null)
                AppLogger.LogToGui($"Update storage: removed {result.Deleted} obsolete files ({result.Bytes} bytes); skipped {result.Skipped} busy, changed or inaccessible files.", false);
            if (updateStorageControl != null) await updateStorageControl.RefreshAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        { AppLogger.LogToGui("Update storage cleanup deferred: " + error.Message, false); }
    }
}
