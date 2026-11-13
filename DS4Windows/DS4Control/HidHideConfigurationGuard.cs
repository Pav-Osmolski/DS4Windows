using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DS4WinWPF.DS4Control
{
    internal interface IHidHideConfigurationDevice : IDisposable
    {
        bool IsOpen();
        bool TryGetWhitelist(out List<string> paths);
        bool TryGetWhitelistInverseState(out bool inverse);
        bool TryGetActiveState(out bool active);
        bool TryGetBlacklist(out List<string> paths);
        bool SetWhitelist(List<string> paths);
    }

    internal sealed class HidHideConfigurationSnapshot
    {
        public IReadOnlyList<string> Applications { get; }
        public IReadOnlyList<string> Devices { get; }
        public bool Inverse { get; }
        public bool Active { get; }

        internal HidHideConfigurationSnapshot(IEnumerable<string> applications,
            IEnumerable<string> devices, bool inverse, bool active)
        {
            Applications = Array.AsReadOnly(applications.ToArray());
            Devices = Array.AsReadOnly(devices.ToArray());
            Inverse = inverse;
            Active = active;
        }

        internal bool Matches(HidHideConfigurationSnapshot other) =>
            other != null && Inverse == other.Inverse && Active == other.Active &&
            Applications.SequenceEqual(other.Applications, StringComparer.Ordinal) &&
            Devices.SequenceEqual(other.Devices, StringComparer.Ordinal);
    }

    internal sealed class HidHideConfigurationAudit
    {
        internal HidHideConfigurationSnapshot Snapshot { get; }
        internal IReadOnlyList<string> Aliases { get; }
        internal string Failure { get; }
        internal bool CanOpen => Failure == null && Aliases.Count == 0;

        internal HidHideConfigurationAudit(HidHideConfigurationSnapshot snapshot,
            IEnumerable<string> aliases, string failure = null)
        {
            Snapshot = snapshot;
            Aliases = Array.AsReadOnly(aliases.ToArray());
            Failure = failure;
        }
    }

    internal readonly record struct HidHideConfigurationRepairResult(
        bool Succeeded, string BackupPath, string Failure);

    /// <summary>
    /// Cold configuration work only. An error inspecting a file is not evidence
    /// that its permission may be removed. Only a positively identified Windows
    /// app-execution alias is repairable, after user confirmation and a backup.
    /// </summary>
    internal sealed class HidHideConfigurationGuard
    {
        internal delegate bool InspectPath(string path, out bool alias, out int error);
        private readonly Func<bool, IHidHideConfigurationDevice> open;
        private readonly InspectPath inspect;
        private readonly Func<HidHideConfigurationSnapshot, string> backup;

        internal HidHideConfigurationGuard(
            Func<bool, IHidHideConfigurationDevice> open, InspectPath inspect,
            Func<HidHideConfigurationSnapshot, string> backup)
        {
            this.open = open;
            this.inspect = inspect;
            this.backup = backup;
        }

        internal static HidHideConfigurationGuard Create(string settingsDirectory) =>
            new(repair => new HidHideAPIDevice(writeAccess: repair, exclusive: repair),
                HidHideApplicationPathGuard.TryInspect,
                snapshot => WriteBackup(settingsDirectory, snapshot));

        internal HidHideConfigurationAudit Inspect()
        {
            // Released HidHide drivers permit one control client at a time.
            // Never hold that handle during filesystem scans or a user prompt.
            HidHideConfigurationSnapshot snapshot;
            try
            {
                using (var device = open(false))
                {
                    if (!TryRead(device, out snapshot))
                        return FailedAudit("Close any open HidHide configuration window and try again. Its settings could not be read.");
                }

                var aliases = new List<string>();
                foreach (string path in snapshot.Applications)
                {
                    bool readable = inspect(path, out bool alias, out int error);
                    if (readable && alias)
                        aliases.Add(path);
                    else if (!readable && error != 2 && error != 3)
                        return FailedAudit("An application permission could not be checked safely. No settings were changed. Check the DS4Windows log for details.", path, error);
                    // A missing file is not this bug and is never pruned.
                }
                if (aliases.Count > 0 && snapshot.Inverse)
                    return new(snapshot, aliases,
                        "HidHide contains Windows app shortcuts, but its application list is inverted. Automatic repair is unavailable because removing entries would change which apps are blocked.");
                return new(snapshot, aliases);
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                return FailedAudit("HidHide settings could not be checked. No settings were changed. " + ex.Message);
            }
        }

        // Caller must obtain confirmation for this exact audit and serialize
        // with DS4Windows' existing HidHide mutation boundary.
        internal HidHideConfigurationRepairResult Repair(HidHideConfigurationAudit audit)
        {
            if (audit == null || audit.Failure != null || audit.Aliases.Count == 0 ||
                audit.Snapshot == null || audit.Snapshot.Inverse)
                return new(false, null, "No confirmed repair is available.");

            string backupPath = null;
            try
            {
                using var device = open(true);
                if (!TryRead(device, out var current))
                    return new(false, null, "Close any open HidHide configuration window and try again. No settings were changed.");
                if (!audit.Snapshot.Matches(current))
                    return new(false, null, "HidHide settings changed while the question was open. Nothing was removed; open the configuration tool again to recheck.");

                var remove = new HashSet<string>(audit.Aliases, StringComparer.Ordinal);
                var retained = current.Applications.Where(path => !remove.Contains(path)).ToList();
                if (retained.Count == current.Applications.Count)
                    return new(false, null, "The selected shortcuts are no longer present. Nothing was removed.");

                // The driver control handle excludes competing clients through
                // this read/backup/write/verify transaction. No restart or
                // active/inverse/hidden-device mutation is part of this repair.
                backupPath = backup(current);
                if (string.IsNullOrWhiteSpace(backupPath))
                    return new(false, null, "The HidHide backup could not be saved. Nothing was removed.");
                // Probe again after the potentially slow durable backup, not
                // just before the user confirmed the original snapshot.
                foreach (string path in audit.Aliases)
                {
                    if (!inspect(path, out bool alias, out _) || !alias)
                        return new(false, backupPath, "An application shortcut changed or could not be checked. Nothing was removed.");
                }
                if (!device.SetWhitelist(retained))
                    return new(false, backupPath, "HidHide did not confirm the repair. The original settings are backed up; no automatic retry was attempted.");
                var expected = new HidHideConfigurationSnapshot(retained,
                    current.Devices, current.Inverse, current.Active);
                if (!TryRead(device, out var after) || !expected.Matches(after))
                    return new(false, backupPath, "HidHide's resulting settings could not be verified. The original settings are backed up; no further changes were attempted.");
                return new(true, backupPath, null);
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                return new(false, backupPath, "HidHide repair could not be completed. " + ex.Message);
            }
        }

        internal static bool TryRead(IHidHideConfigurationDevice device,
            out HidHideConfigurationSnapshot snapshot)
        {
            snapshot = null;
            if (device == null || !device.IsOpen() ||
                !device.TryGetWhitelistInverseState(out bool inverse) ||
                !device.TryGetWhitelist(out var applications) ||
                !device.TryGetActiveState(out bool active) ||
                !device.TryGetBlacklist(out var devices) ||
                applications == null || devices == null)
                return false;
            snapshot = new(applications, devices, inverse, active);
            if (!HasLosslessPolicyText(snapshot))
            {
                // The legacy driver writer and JSON backup replace unpaired
                // UTF-16 surrogates. Refuse the transaction before either can
                // silently change a permission, including a stale file name.
                NLog.LogManager.GetCurrentClassLogger().Warn(
                    "HidHide settings contain text that cannot be preserved safely. No repair was attempted.");
                snapshot = null;
                return false;
            }
            return true;
        }

        private static bool HasLosslessPolicyText(HidHideConfigurationSnapshot snapshot)
        {
            if (snapshot == null) return false;
            foreach (string value in snapshot.Applications.Concat(snapshot.Devices))
            {
                if (string.IsNullOrEmpty(value)) return false;
                for (int i = 0; i < value.Length; i++)
                {
                    char current = value[i];
                    if (current == '\0') return false;
                    if (!char.IsSurrogate(current)) continue;
                    if (!char.IsHighSurrogate(current) || i + 1 >= value.Length ||
                        !char.IsLowSurrogate(value[i + 1])) return false;
                    i++;
                }
            }
            return true;
        }

        private static HidHideConfigurationAudit FailedAudit(string message,
            string path = null, int error = 0)
        {
            if (path != null)
                NLog.LogManager.GetCurrentClassLogger().Warn(
                    $"HidHide application check failed (Windows error {error}): {path}");
            return new(null, Array.Empty<string>(), message);
        }

        private static bool IsExpectedFailure(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or ArgumentException or
                System.ComponentModel.Win32Exception or NotSupportedException;

        internal static string WriteBackup(string settingsDirectory,
            HidHideConfigurationSnapshot snapshot, string reason = "Confirmed Windows app-execution alias repair")
        {
            if (!HasLosslessPolicyText(snapshot))
                throw new IOException("HidHide settings contain text that cannot be backed up without changes.");
            if (string.IsNullOrWhiteSpace(settingsDirectory) ||
                !Path.IsPathFullyQualified(settingsDirectory))
                throw new IOException("The settings folder is unavailable for a backup.");
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Schema = 1, CreatedUtc = DateTime.UtcNow,
                Reason = reason,
                snapshot.Applications, snapshot.Devices, snapshot.Inverse, snapshot.Active
            }, new JsonSerializerOptions { WriteIndented = true });
            string directory = Path.Combine(settingsDirectory, "Backups", "HidHide");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                $"before-hidhide-repair-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None);
            file.Write(bytes);
            file.Flush(flushToDisk: true);
            return path;
        }
    }
}
