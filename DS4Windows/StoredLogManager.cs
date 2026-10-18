using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DS4WinWPF;

internal readonly record struct StoredLogCleanupResult(int Deleted, int Failed);

// Only application-owned archives directly inside the resolved Logs directory
// are eligible. Never recurse, follow a link, or remove the active session log.
internal static class StoredLogManager
{
    internal const int DefaultRetentionDays = 30;
    internal const int MaximumArchiveFiles = 20;
    internal const long ArchiveBytes = 10 * 1024 * 1024;
    internal static readonly int[] RetentionChoices = { 1, 7, 14, 30, 90 };
    private static readonly Regex ArchiveName = new(
        @"\Ads4windows_log_(?:[0-9]+(?:\.[0-9]+)?)\.txt\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static int NormalizeRetentionDays(int days) =>
        Array.IndexOf(RetentionChoices, days) >= 0 ? days : DefaultRetentionDays;

    internal static StoredLogCleanupResult Cleanup(string directory, int days,
        DateTime utcNow, bool clearAll = false)
    {
        int deleted = 0, failed = 0;
        try
        {
            var root = new DirectoryInfo(Path.GetFullPath(directory));
            if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0)
                return default;
            var candidates = new List<FileInfo>();
            foreach (FileInfo file in root.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (ArchiveName.IsMatch(file.Name) ||
                    file.Name.Equals("startup_failure.log", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(file);
            }
            DateTime cutoff = utcNow.AddDays(-NormalizeRetentionDays(days));
            int retainedArchives = 0;
            foreach (FileInfo file in candidates.OrderByDescending(file => file.LastWriteTimeUtc))
            {
                bool expired = file.LastWriteTimeUtc < cutoff;
                bool beyondLimit = ArchiveName.IsMatch(file.Name) && ++retainedArchives > MaximumArchiveFiles;
                if (!clearAll && !expired && !beyondLimit) continue;
                try { file.Delete(); deleted++; }
                catch (IOException) { failed++; }
                catch (UnauthorizedAccessException) { failed++; }
            }
        }
        catch (IOException) { failed++; }
        catch (UnauthorizedAccessException) { failed++; }
        return new(deleted, failed);
    }
}
