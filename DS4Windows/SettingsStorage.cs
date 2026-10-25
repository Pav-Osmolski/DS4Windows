using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace DS4Windows;

internal enum SettingsFolder { AppData, Program }

// The preference lives outside either settings directory, so choosing a protected
// program directory does not require elevation. All persisted paths are relative
// names from an allowlist; callers cannot select arbitrary directories.
internal sealed class SettingsStorage
{
    internal static SettingsStorage Current => new(Global.exedirpath, Global.appDataPpath,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DS4Windows", "Settings locations"));

    private static readonly string[] RootFiles = { "Profiles.xml", "Auto Profiles.xml", "Actions.xml",
        "LinkedProfiles.xml", "ControllerConfigs.xml", "OutputSlots.xml" };
    private static readonly string[] DataDirectories = { "Profiles", "Nintendo", "Switch2", "BatteryAlertSounds", "Macros" };
    private const long MaximumBytes = 512L * 1024 * 1024;
    private const int MaximumFiles = 10000;
    private readonly string program, appData, stateDirectory, preferencePath;
    internal Action<int> AfterPublishForTest;

    internal SettingsStorage(string program, string appData, string preferences)
    {
        this.program = Path.GetFullPath(program);
        this.appData = Path.GetFullPath(appData);
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.program.ToUpperInvariant())))[..24];
        stateDirectory = Path.Combine(Path.GetFullPath(preferences), identity);
        preferencePath = Path.Combine(stateDirectory, "location.json");
    }

    internal string Root(SettingsFolder folder) => folder switch
    {
        SettingsFolder.AppData => appData,
        SettingsFolder.Program => program,
        _ => throw new InvalidDataException("Unknown settings location.")
    };

    internal SettingsFolder FolderFor(string root) => Same(root, appData) ? SettingsFolder.AppData :
        Same(root, program) ? SettingsFolder.Program : throw new InvalidDataException("Unknown settings directory.");
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'),
        Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    internal sealed class Preference
    {
        public int Version { get; set; } = 1;
        public SettingsFolder? Active { get; set; }
        public PendingChange Pending { get; set; }
    }
    internal sealed class PendingChange
    {
        public string Id { get; set; }
        public SettingsFolder Source { get; set; }
        public SettingsFolder Target { get; set; }
        public bool Copy { get; set; }
        public bool ReplaceExisting { get; set; }
        public bool Restore { get; set; }
    }
    private sealed class Snapshot
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private sealed class Journal
    {
        public Snapshot Original { get; set; }
        public Snapshot Replacement { get; set; }
        public bool Committed { get; set; }
    }

    private Preference Read()
    {
        if (!File.Exists(preferencePath)) return new();
        RejectLink(preferencePath);
        var preference = JsonSerializer.Deserialize<Preference>(File.ReadAllText(preferencePath));
        if (preference == null || preference.Version != 1) throw new InvalidDataException("Unsupported settings-location preference.");
        if (preference.Active.HasValue) _ = Root(preference.Active.Value);
        return preference;
    }
    internal SettingsFolder? Selected => Read().Active;
    internal bool HasPending => Read().Pending != null;
    internal void Remember(SettingsFolder folder)
    {
        _ = Root(folder);
        var preference = Read();
        preference.Active = folder;
        Save(preferencePath, preference);
    }
    internal void CancelPending()
    {
        var preference = Read();
        if (preference.Pending is { } change)
        {
            ValidateId(change.Id);
            string operation = Path.Combine(stateDirectory, change.Id);
            string journalPath = Path.Combine(operation, "journal.json");
            if (File.Exists(journalPath))
            {
                var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath)) ?? throw new InvalidDataException("Invalid migration journal.");
                ValidateSnapshot(journal.Original); ValidateSnapshot(journal.Replacement);
                if (journal.Committed) { Verify(Root(change.Target), journal.Replacement); Complete(preference, change.Target); return; }
                Recover(Root(change.Target), Path.Combine(operation, "backup"), journal);
            }
        }
        preference.Pending = null;
        Save(preferencePath, preference);
    }
    internal void Queue(SettingsFolder source, SettingsFolder target, bool copy, bool replaceExisting)
    {
        _ = Root(source); _ = Root(target);
        if (Same(Root(source), Root(target))) throw new InvalidOperationException("The selected location is already active.");
        var preference = Read();
        if (preference.Pending != null) throw new InvalidOperationException("A settings-location change is already pending.");
        preference.Active = source;
        preference.Pending = new() { Id = Guid.NewGuid().ToString("N"), Source = source, Target = target,
            Copy = copy, ReplaceExisting = replaceExisting };
        Save(preferencePath, preference);
    }

    internal bool HasSettings(SettingsFolder folder) => Capture(Root(folder)).Files.Count > 0;
    internal static bool HasConfiguration(string root) => File.Exists(Path.Combine(root, "Profiles.xml")) ||
        File.Exists(Path.Combine(root, "Auto Profiles.xml")) ||
        (Directory.Exists(Path.Combine(root, "Profiles")) && Directory.EnumerateFiles(Path.Combine(root, "Profiles"), "*.xml").Any());

    internal static bool CanWrite(string root, out string reason)
    {
        string probe = null;
        try
        {
            // Do not create a missing selected directory merely to render a badge.
            string parent = Path.GetFullPath(root);
            while (!Directory.Exists(parent)) parent = Path.GetDirectoryName(parent) ?? throw new IOException("No existing parent directory.");
            RejectLink(parent);
            probe = Path.Combine(parent, ".ds4windows-write-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            reason = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            reason = error.Message;
            return false;
        }
        finally { if (probe != null && File.Exists(probe)) File.Delete(probe); }
    }

    // Called once, before any controller service or settings store is constructed.
    // A journal and verified originals make interrupted publication recoverable.
    internal string ApplyPending()
    {
        var preference = Read();
        var change = preference.Pending;
        if (change == null) return null;
        ValidateId(change.Id);
        string target = Root(change.Target);
        string operation = Path.Combine(stateDirectory, change.Id);
        string stage = Path.Combine(operation, "staged");
        string backup = Path.Combine(operation, "backup");
        string journalPath = Path.Combine(operation, "journal.json");
        if (File.Exists(journalPath))
        {
            var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath)) ?? throw new InvalidDataException("Invalid migration journal.");
            ValidateSnapshot(journal.Original); ValidateSnapshot(journal.Replacement);
            if (journal.Committed)
            {
                Verify(target, journal.Replacement);
                Complete(preference, change.Target);
                return backup;
            }
            Recover(target, backup, journal);
            // Do not silently retry a failed or interrupted migration.
            preference.Pending = null;
            Save(preferencePath, preference);
            throw new IOException("The interrupted settings change was rolled back. The previous location is still active. Backups: " + backup);
        }
        if (!CanWrite(target, out string reason)) throw new IOException("The destination is not writable: " + reason);
        if (!change.Copy)
        {
            if (!HasSettings(change.Target)) throw new IOException("The selected directory does not contain settings. Choose Copy current settings instead.");
            Complete(preference, change.Target);
            return null;
        }

        var original = Capture(target);
        if (original.Files.Count > 0 && !change.ReplaceExisting)
            throw new IOException("The destination contains settings. No files were replaced. Review the destination first.");
        string source = change.Restore ? Path.Combine(operation, "restore") : Root(change.Source);
        var replacement = Capture(source);
        if (replacement.Files.Count == 0) throw new IOException("No settings were found to copy.");
        CopyVerified(source, stage, replacement);
        CopyVerified(target, backup, original);
        Verify(source, replacement); // Catch files changing during snapshot creation.
        Verify(target, original);
        var transaction = new Journal { Original = original, Replacement = replacement };
        Save(journalPath, transaction);
        try
        {
            int count = 0;
            foreach (string relative in original.Files.Keys.Union(replacement.Files.Keys, StringComparer.OrdinalIgnoreCase))
            {
                string destination = SafePath(target, relative);
                bool expectedFile = original.Files.TryGetValue(relative, out string expectedHash);
                if (File.Exists(destination) != expectedFile || (expectedFile && Hash(destination) != expectedHash))
                    throw new IOException("The destination changed after verification. No further files were replaced: " + destination);
                if (replacement.Files.ContainsKey(relative)) Publish(SafePath(stage, relative), destination);
                else File.Delete(destination); // The verified original remains in the transaction backup.
                AfterPublishForTest?.Invoke(++count);
            }
            Verify(target, replacement);
            transaction.Committed = true;
            Save(journalPath, transaction);
        }
        catch
        {
            Recover(target, backup, transaction);
            preference.Pending = null;
            Save(preferencePath, preference);
            throw;
        }
        Complete(preference, change.Target);
        return backup;
    }

    private void Complete(Preference preference, SettingsFolder target)
    {
        preference.Active = target;
        preference.Pending = null;
        Save(preferencePath, preference);
    }
    private static void Recover(string target, string backup, Journal journal)
    {
        Verify(backup, journal.Original);
        var current = Capture(target);
        foreach (var item in current.Files)
        {
            bool original = journal.Original.Files.TryGetValue(item.Key, out string oldHash) && oldHash == item.Value;
            bool replacement = journal.Replacement.Files.TryGetValue(item.Key, out string newHash) && newHash == item.Value;
            if (!original && !replacement) throw new IOException("Settings changed during recovery. No further files were overwritten. Keep the transaction backup for manual recovery: " + backup);
        }
        foreach (string relative in journal.Original.Files.Keys.Union(journal.Replacement.Files.Keys, StringComparer.OrdinalIgnoreCase))
        {
            string destination = SafePath(target, relative);
            if (journal.Original.Files.ContainsKey(relative)) Publish(SafePath(backup, relative), destination);
            else if (File.Exists(destination)) File.Delete(destination);
        }
        Verify(target, journal.Original);
    }

    internal void BackUp(string root, string archive)
    {
        _ = FolderFor(root);
        var snapshot = Capture(root);
        using var stream = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (string relative in snapshot.Files.Keys)
        {
            using var input = File.OpenRead(SafePath(root, relative));
            using var output = zip.CreateEntry(relative.Replace('\\', '/')).Open();
            input.CopyTo(output);
        }
        using (var manifest = zip.CreateEntry("DS4Windows-settings-backup.json").Open()) JsonSerializer.Serialize(manifest, snapshot);
        Verify(root, snapshot);
    }

    internal void QueueRestore(string archive, SettingsFolder active)
    {
        var preference = Read();
        if (preference.Pending != null) throw new IOException("Cancel the pending settings change first.");
        string id = Guid.NewGuid().ToString("N");
        string restore = Path.Combine(stateDirectory, id, "restore");
        using var zip = ZipFile.OpenRead(archive);
        var entry = zip.GetEntry("DS4Windows-settings-backup.json") ?? throw new InvalidDataException("This is not a DS4Windows settings backup.");
        if (entry.Length > 4 * 1024 * 1024) throw new InvalidDataException("The backup manifest is too large.");
        Snapshot snapshot;
        using (var input = entry.Open()) snapshot = JsonSerializer.Deserialize<Snapshot>(input);
        ValidateSnapshot(snapshot);
        if (snapshot.Files.Count == 0) throw new InvalidDataException("The backup is empty.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in zip.Entries)
        {
            if (file.FullName == entry.FullName) { if (!seen.Add(file.FullName)) throw new InvalidDataException("Duplicate archive entry."); continue; }
            string relative = file.FullName.Replace('/', '\\');
            if (!seen.Add(relative) || !snapshot.Files.ContainsKey(relative) || (total += file.Length) > MaximumBytes)
                throw new InvalidDataException("Unexpected, duplicate or oversized backup entry.");
            string destination = SafePath(restore, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using var output = new FileStream(destination, FileMode.CreateNew);
            using var input = file.Open();
            var buffer = new byte[81920];
            long remaining = file.Length;
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                remaining -= read;
                if (remaining < 0) throw new InvalidDataException("Invalid archive entry length.");
                output.Write(buffer, 0, read);
            }
        }
        Verify(restore, snapshot);
        preference.Active = active;
        preference.Pending = new() { Id = id, Source = active, Target = active, Copy = true, ReplaceExisting = true, Restore = true };
        Save(preferencePath, preference);
    }

    private static Snapshot Capture(string root)
    {
        var snapshot = new Snapshot();
        if (!Directory.Exists(root)) return snapshot;
        RejectLink(root);
        var files = new List<string>();
        foreach (string name in RootFiles) if (File.Exists(Path.Combine(root, name))) files.Add(Path.Combine(root, name));
        foreach (string name in DataDirectories)
        {
            string directory = Path.Combine(root, name);
            if (Directory.Exists(directory)) Walk(directory, files);
        }
        long size = 0;
        foreach (string path in files)
        {
            RejectLink(path);
            string relative = Path.GetRelativePath(root, path);
            _ = SafePath(root, relative);
            if (snapshot.Files.Count >= MaximumFiles || (size += new FileInfo(path).Length) > MaximumBytes)
                throw new InvalidDataException("Settings exceed the backup size limit.");
            ValidateDocument(path);
            snapshot.Files.Add(relative, Hash(path));
        }
        return snapshot;
    }
    private static void Walk(string directory, List<string> files)
    {
        RejectLink(directory);
        foreach (string file in Directory.EnumerateFiles(directory)) { files.Add(file); if (files.Count > MaximumFiles) throw new InvalidDataException("Too many settings files."); }
        foreach (string child in Directory.EnumerateDirectories(directory)) Walk(child, files);
    }
    private static void ValidateDocument(string path)
    {
        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = MaximumBytes });
            while (reader.Read()) { }
        }
        else if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        { using var document = JsonDocument.Parse(File.ReadAllText(path)); }
    }
    private static void ValidateSnapshot(Snapshot snapshot)
    {
        if (snapshot == null || snapshot.Version != 1 || snapshot.Files == null || snapshot.Files.Count > MaximumFiles)
            throw new InvalidDataException("Invalid settings manifest.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in snapshot.Files)
        {
            _ = SafePath(Path.GetTempPath(), item.Key);
            if (!seen.Add(item.Key) || item.Value == null || item.Value.Length != 64 || !item.Value.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid settings manifest entry.");
        }
    }
    private static string SafePath(string root, string relative)
    {
        string normalized = relative.Replace('/', '\\');
        string[] parts = normalized.Split('\\');
        if (Path.IsPathRooted(relative) || parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".." ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.TrimEnd(' ', '.') != p) ||
            !(parts.Length == 1 ? RootFiles.Contains(parts[0], StringComparer.OrdinalIgnoreCase) :
                DataDirectories.Contains(parts[0], StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unexpected settings file: " + relative);
        if (new[] { ".exe", ".dll", ".sys", ".msi", ".msix", ".com", ".bat", ".cmd", ".ps1" }
            .Contains(Path.GetExtension(normalized), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Application files are not part of a settings backup: " + relative);
        string full = Path.GetFullPath(Path.Combine(root, normalized));
        string parent = full;
        while (parent != null)
        {
            RejectLink(parent);
            if (Same(parent, root)) return full;
            parent = Path.GetDirectoryName(parent);
        }
        throw new InvalidDataException("Settings path escapes its directory.");
    }
    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked settings files and folders cannot be migrated: " + path);
    }
    private static string Hash(string path)
    { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Verify(string root, Snapshot expected)
    {
        var actual = Capture(root);
        if (actual.Files.Count != expected.Files.Count || expected.Files.Any(item => !actual.Files.TryGetValue(item.Key, out string hash) || hash != item.Value))
            throw new IOException("Settings verification failed: " + root);
    }
    private static void CopyVerified(string source, string destination, Snapshot snapshot)
    {
        Directory.CreateDirectory(destination);
        foreach (string relative in snapshot.Files.Keys)
        {
            string target = SafePath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(SafePath(source, relative), target, false);
        }
        Verify(destination, snapshot);
    }
    private static void Publish(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary, false);
            using (var output = new FileStream(temporary, FileMode.Open, FileAccess.Write)) output.Flush(true);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            { JsonSerializer.Serialize(output, value); output.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void ValidateId(string id)
    { if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid settings operation identifier."); }
}
