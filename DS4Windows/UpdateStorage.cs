using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace DS4Windows;

// Only versioned packages in these known, non-recursive locations are eligible.
// Never remove installed drivers, live binaries, transaction journals, backups,
// user downloads outside our own folders, or unknown names/directories.
internal sealed class UpdateStorage
{
    internal sealed record StoredFile(string Path, string Component, string Release, Version Version,
        string Suffix, long Bytes, long LastWriteTicks);
    internal sealed record Snapshot(StoredFile[] Files, StoredFile[] Removable, string[] Warnings)
    {
        internal long TotalBytes => Files.Sum(file => file.Bytes);
        internal long RecoverableBytes => Removable.Sum(file => file.Bytes);
    }
    internal sealed record Result(int Deleted, long Bytes, int Skipped);
    private readonly string program;
    private readonly string[] folders;
    private readonly Dictionary<string, string> current;
    private readonly HashSet<string> managed;
    private static readonly Regex Package = new(
        @"^(?<component>DS4Windows|DS4Updater|VIIPER|USBip)[_-](?<version>\d+\.\d+\.\d+(?:\.\d+)?)(?<suffix>-[a-zA-Z0-9][a-zA-Z0-9.-]*)?(?<tail>_x(?:64|86)\.zip|(?:\.0)?_Setup_x(?:64|86)\.exe|-x(?:64|86)\.exe(?:\.sha256)?|-(?:SOURCE\.zip|BUILD-INFO\.json|BUILD-NOTES\.txt|LICENSES?\.txt|PROVENANCE\.txt))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    internal static UpdateStorage Current => new(Global.exedirpath,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DS4Windows", "Updates"),
        typeof(UpdateStorage).Assembly.GetName().Version,
        ViiperSetupManager.SupportedViiperReleaseTag.TrimStart('v'), ViiperSetupManager.RequiredUsbipVersion.ToString());

    internal UpdateStorage(string program, string userUpdates, Version appVersion, string viiperVersion, string usbipVersion)
    {
        this.program = Path.TrimEndingDirectorySeparator(Path.GetFullPath(program));
        if (this.program == Path.GetPathRoot(this.program)) throw new InvalidDataException("A drive root is not an update storage folder.");
        folders = new[] { this.program, Path.Combine(this.program, "Updates"), Path.Combine(this.program, "extras"),
            Path.GetFullPath(userUpdates) }.Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        current = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DS4Windows"] = appVersion.ToString(), ["VIIPER"] = viiperVersion, ["USBip"] = usbipVersion
        };
        string updater = Path.Combine(this.program, "DS4Updater.exe");
        if (File.Exists(updater))
        {
            PortableLabContext.ValidateNoReparsePoints(updater);
            var info = FileVersionInfo.GetVersionInfo(updater);
            if (info.FileMajorPart > 0) current["DS4Updater"] = new Version(info.FileMajorPart, info.FileMinorPart,
                info.FileBuildPart, Math.Max(0, info.FilePrivatePart)).ToString();
        }
        managed = new(StringComparer.OrdinalIgnoreCase);
    }

    private void ReadManagedFiles()
    {
        managed.Clear();
        string manifest = Path.Combine(this.program, ".ds4windows-managed-files.txt");
        if (File.Exists(manifest))
        {
            PortableLabContext.ValidateNoReparsePoints(manifest);
            foreach (string line in File.ReadLines(manifest))
            {
                // Entries are only used to exclude files, never to create deletion paths.
                if (line.Length < 1024 && !Path.IsPathRooted(line) && !line.Split('/', '\\').Contains(".."))
                    managed.Add(Path.GetFullPath(Path.Combine(this.program, line.Replace('/', Path.DirectorySeparatorChar))));
            }
        }
    }

    internal Snapshot Scan(int keep)
    {
        if (keep is < -1 or > 100) throw new ArgumentOutOfRangeException(nameof(keep));
        ReadManagedFiles();
        var files = new List<StoredFile>();
        var warnings = new List<string>();
        foreach (string folder in folders)
        {
            try
            {
                PortableLabContext.ValidateNoReparsePoints(folder);
                if (!Directory.Exists(folder)) continue;
                foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
                {
                    var match = Package.Match(Path.GetFileName(path));
                    if (!match.Success) continue;
                    PortableLabContext.ValidateNoReparsePoints(path);
                    var info = new FileInfo(path);
                    if (!Version.TryParse(match.Groups["version"].Value, out var parsed)) continue;
                    var version = Normalize(parsed);
                    string release = version.ToString(version.Revision == 0 ? 3 : 4) + match.Groups["suffix"].Value;
                    files.Add(new(path, match.Groups["component"].Value, release,
                        version, match.Groups["suffix"].Value, info.Length, info.LastWriteTimeUtc.Ticks));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { warnings.Add(folder + ": " + error.Message); }
        }
        var remove = new List<StoredFile>();
        if (keep >= 0)
        {
            foreach (var component in files.GroupBy(file => file.Component, StringComparer.OrdinalIgnoreCase))
            {
                // Unknown current version => display its packages, but never guess what is obsolete.
                if (!current.TryGetValue(component.Key, out string active) || !Version.TryParse(active, out var activeVersion)) continue;
                activeVersion = Normalize(activeVersion);
                var older = component.Where(file => file.Version < activeVersion ||
                    (file.Version == activeVersion && file.Suffix.Length > 0)).ToArray();
                var retained = older.GroupBy(file => file.Release, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group => group.First().Version)
                    .ThenByDescending(group => group.First().Suffix.Length == 0)
                    .ThenByDescending(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(keep).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                remove.AddRange(older.Where(file => !retained.Contains(file.Release) && !managed.Contains(file.Path)));
            }
        }
        return new(files.ToArray(), remove.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray(), warnings.ToArray());
    }

    internal Result Clean(Snapshot approved, int keep)
    {
        // Re-scan policy and ownership immediately before deletion. A preview is not
        // authority to delete files that have changed or become needed since then.
        var allowed = Scan(keep).Removable.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        int deleted = 0, skipped = 0;
        long bytes = 0;
        foreach (var file in approved.Removable)
        {
            try
            {
                if (!allowed.TryGetValue(file.Path, out var now) || file != now) { skipped++; continue; }
                PortableLabContext.ValidateNoReparsePoints(file.Path);
                // Exclusive access skips downloads/in-use packages. Delete the exact
                // opened file by handle, so a path replacement cannot change the target.
                using var handle = CreateFile(file.Path, 0x00010000 | 0x00000080, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) { skipped++; continue; }
                long size = ((long)info.SizeHigh << 32) | info.SizeLow;
                long written = ((long)info.WriteHigh << 32) | info.WriteLow;
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || size != file.Bytes ||
                    DateTime.FromFileTimeUtc(written).Ticks != file.LastWriteTicks) { skipped++; continue; }
                var disposition = new Disposition { Delete = true };
                if (!SetFileInformationByHandle(handle, 4, ref disposition, 1)) { skipped++; continue; }
                deleted++; bytes += size;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { skipped++; }
        }
        return new(deleted, bytes, skipped);
    }

    internal Result CleanAutomatically(SettingsStorage settings, Func<bool> backendReady)
    {
        var policy = settings.UpdateRetention;
        if (!policy.Automatic || policy.Keep < 0) return null;
        var plan = Scan(policy.Keep);
        if (plan.Removable.Length == 0 || !backendReady()) return null;
        // The user may have changed retention while the readiness probe ran.
        var latest = settings.UpdateRetention;
        return latest.Automatic && latest.Keep == policy.Keep ? Clean(plan, latest.Keep) : null;
    }

    private static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref Disposition information, uint size);
}
