using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace DS4Windows.Installation;

internal sealed class ControllerStartupConflict
{
    public string Kind { get; set; }
    public string Id { get; set; }
    public string Target { get; set; }
    public string Description { get; set; }
    public string Snapshot { get; set; }
    public string BinaryHash { get; set; }
    public string RegistryScope { get; set; }
    public int RegistryView { get; set; }
    public string ValueName { get; set; }
    public int ValueKind { get; set; }
    public bool CanPause { get; set; }
    public bool TargetMissing { get; set; }
    public string Payload { get; set; }
    public string Identity { get; set; }
    public string ReviewReason { get; set; }
}

internal sealed class ControllerConflictBackup
{
    public int Schema { get; set; } = 1;
    public string UserSid { get; set; }
    public List<ControllerStartupConflict> Entries { get; set; } = new();
}

// Shared by the unelevated review page and the elevated preflight. Re-scan and
// compare the exact review fingerprint before making any machine-wide change.
internal static class ControllerStartupConflicts
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");
    private static readonly string ManagedRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DS4Windows");

    internal static string Fingerprint(IEnumerable<ControllerStartupConflict> entries) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            entries.OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray()))));

    internal static void ApplyReviewed(string expected, Func<List<ControllerStartupConflict>> scan,
        Action<List<ControllerStartupConflict>> backup, Action<ControllerStartupConflict> pause)
    {
        var entries = scan();
        if (entries.Any(e => !e.CanPause) || !string.Equals(Fingerprint(entries), expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Controller startup conflicts changed or need manual review. Reopen setup and review them before continuing.");
        if (entries.Count == 0) return;
        backup(entries); // A durable protected backup must precede every mutation.
        foreach (var entry in entries.OrderBy(e => e.Kind == "Process" ? 1 : 0)) pause(entry);
    }

    internal static List<ControllerStartupConflict> Scan(string sid)
    {
        StartupSetupStore.ValidateSid(sid);
        var entries = new List<ControllerStartupConflict>();
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (string scope in new[] { "Machine", sid })
        {
            // HKU\SID\Software\...\Run is shared between registry views.
            // Inspect it once so one value cannot be paused twice.
            if (scope != "Machine" && view == RegistryView.Registry32) continue;
            try
            {
                using var key = OpenRun(scope, view, false);
                if (key == null) continue;
                foreach (string name in key.GetValueNames())
                {
                    if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command) continue;
                    if (IsStartupDisabled(scope, view, name, false)) continue;
                    var entry = FromCommand("Run", scope + "|" + view + "|" + name, command);
                    if (entry == null) continue;
                    entry.RegistryScope = scope; entry.RegistryView = (int)view;
                    entry.ValueName = name; entry.ValueKind = (int)key.GetValueKind(name);
                    entry.Description = (scope == "Machine" ? "All users — " : "Your sign-in — ") + name + " — " + entry.Target;
                    entries.Add(entry);
                }
            }
            catch (Exception error) { entries.Add(InspectionFailure("Registry " + scope + " " + view, error)); }
        }
        object service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new IOException("Task Scheduler is unavailable."));
            ((dynamic)service).Connect();
            object root = ((dynamic)service).GetFolder(@"\");
            try { ReadTasks(root, sid, entries, 0); }
            finally { Release(root); }
        }
        catch (Exception error) { entries.Add(InspectionFailure("Task Scheduler", error)); }
        finally { Release(service); }

        ReadStartupFolders(sid, entries);

        try
        {
            using var search = new ManagementObjectSearcher("SELECT ProcessId,Name,ExecutablePath,CreationDate FROM Win32_Process");
            using var processes = search.Get();
            foreach (ManagementObject process in processes)
            using (process)
            {
                string name = process["Name"] as string ?? "";
                string path = process["ExecutablePath"] as string;
                if (!name.Equals("DS4Windows.exe", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("viiper.exe", StringComparison.OrdinalIgnoreCase) &&
                    !entries.Any(e => SamePath(e.Target, path))) continue;
                if (IsManaged(path)) continue;
                var version = ReadControllerVersion(path);
                string binaryHash = version == null ? "" : Hash(path);
                string id = Convert.ToString(process["ProcessId"], System.Globalization.CultureInfo.InvariantCulture);
                bool owned = false;
                try { owned = ReadProcessOwnerSid(Convert.ToUInt32(process["ProcessId"])) == sid; } catch { }
                entries.Add(new ControllerStartupConflict {
                    Kind = "Process", Id = id, Target = path ?? "<path unavailable>",
                    Snapshot = process["CreationDate"] as string ?? "", BinaryHash = binaryHash,
                    Description = "Running: " + name + " (PID " + id + ") — " + (path ?? "path unavailable"),
                    Identity = DescribeControllerBinary(version, binaryHash),
                    ReviewReason = version == null ? "Executable could not be verified" : "Process owner or start time could not be verified",
                    CanPause = version != null && owned && !string.IsNullOrEmpty(process["CreationDate"] as string)
                });
            }
        }
        catch (Exception error) { entries.Add(InspectionFailure("Running applications", error)); }
        if (entries.Count > 512) throw new IOException("Startup inspection limit exceeded.");
        foreach (var entry in entries)
        {
            if (entry.TargetMissing) entry.Description += " · Target file missing";
            if (entry.Identity != null) entry.Description += " · " + entry.Identity;
            if (!entry.CanPause)
            {
                entry.Description = "Manual review — " + entry.Description;
                if (entry.ReviewReason != null) entry.Description += " · " + entry.ReviewReason;
                if (entry.Kind == "Shortcut") entry.Description += "\nStartup shortcut: " + entry.Id;
            }
        }
        return entries.OrderBy(e => e.Kind).ThenBy(e => e.Id).ToList();
    }

    private static IEnumerable<(string Path, string Scope, bool AllowOrphanPause)> StartupFolders(string sid)
    {
        using var profiles = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
        string profile = profiles?.GetValue("ProfileImagePath") as string;
        if (string.IsNullOrEmpty(profile) || !Path.IsPathFullyQualified(profile))
            throw new IOException("The original user's startup folder could not be resolved.");
        // Read the original user's redirected Startup location, never the
        // administrator's AppData when elevation uses another account.
        using var user = Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        string startup = user?.GetValue("Startup", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        string roaming = user?.GetValue("AppData", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        roaming = (roaming ?? @"%USERPROFILE%\AppData\Roaming").Replace("%USERPROFILE%", profile, StringComparison.OrdinalIgnoreCase);
        startup = (startup ?? @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup")
            .Replace("%USERPROFILE%", profile, StringComparison.OrdinalIgnoreCase)
            .Replace("%APPDATA%", roaming, StringComparison.OrdinalIgnoreCase);
        if (startup.Contains('%') || !Path.IsPathFullyQualified(startup) || startup.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("The original user's startup folder needs manual inspection.");
        string startupPath = Path.GetFullPath(startup);
        string profilePrefix = Path.GetFullPath(profile).TrimEnd('\\') + '\\';
        yield return (startupPath, sid, startupPath.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase));
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Machine", false);
    }

    private static void ReadStartupFolders(string sid, List<ControllerStartupConflict> entries)
    {
        object shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            foreach (var folder in StartupFolders(sid))
            {
                if (!Directory.Exists(folder.Path)) continue;
                StartupSetupRecovery.RequirePlainPath(folder.Path);
                foreach (string path in Directory.EnumerateFiles(folder.Path))
                {
                    if (IsStartupDisabled(folder.Scope, RegistryView.Registry64, Path.GetFileName(path), true)) continue;
                    if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        var entry = ReadStartupShortcut(shell, path, folder.Scope, folder.AllowOrphanPause);
                        if (entry != null) entries.Add(entry);
                    }
                    else if (Path.GetFileName(path).Contains("DS4Windows", StringComparison.OrdinalIgnoreCase) ||
                             Path.GetFileName(path).Contains("VIIPER", StringComparison.OrdinalIgnoreCase))
                        entries.Add(InspectionFailure("Startup file " + path, new IOException()));
                }
            }
        }
        catch (Exception error) { entries.Add(InspectionFailure("Startup folders", error)); }
        finally { Release(shell); }
    }

    internal static ControllerStartupConflict ReadStartupShortcut(object shell, string path, string scope, bool allowOrphanPause)
    {
        StartupSetupRecovery.RequirePlainPath(path);
        if (new FileInfo(path).Length > 64 * 1024) throw new IOException("A startup shortcut exceeds its size limit.");
        byte[] original = File.ReadAllBytes(path);
        object shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(path);
            string targetPath = ((dynamic)shortcut).TargetPath;
            string command = '"' + targetPath + "\" " + (string)((dynamic)shortcut).Arguments;
            var entry = FromCommand("Shortcut", path, command);
            if (entry == null && string.IsNullOrWhiteSpace(targetPath) &&
                Path.GetFileName(path).Equals("DS4Windows.lnk", StringComparison.OrdinalIgnoreCase))
                entry = new ControllerStartupConflict { Kind = "Shortcut", Id = path, Target = "Target path unavailable",
                    Snapshot = command, ReviewReason = "Shortcut target could not be read" };
            if (entry == null) return null;
            if (!File.ReadAllBytes(path).SequenceEqual(original))
                throw new IOException("A startup shortcut changed during inspection.");

            entry.Payload = Convert.ToBase64String(original);
            entry.RegistryScope = scope;
            entry.Description = (scope == "Machine" ? "All users' startup folder: " : "Your startup folder: ") + Path.GetFileName(path) + " — " + entry.Target;
            if (entry.Identity == null && SamePath(entry.Target, targetPath) && original.Length >= 76 &&
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(original) == 76 &&
                new Guid(original.AsSpan(4, 16)) == ShellLinkClassId && IsMissingDs4WindowsTarget(entry.Target))
            {
                entry.TargetMissing = true;
                entry.CanPause = allowOrphanPause;
                entry.ReviewReason = entry.CanPause ? null : "Startup folder is outside your Windows profile";
            }
            return entry;
        }
        finally { Release(shortcut); }
    }

    private static void ReadTasks(object folder, string sid, List<ControllerStartupConflict> entries, int depth)
    {
        if (depth > 16 || entries.Count > 512) throw new IOException("Startup inspection limit exceeded.");
        object tasks = null, children = null;
        try
        {
            tasks = ((dynamic)folder).GetTasks(1);
            foreach (object task in (System.Collections.IEnumerable)tasks)
            try
            {
                if (!(bool)((dynamic)task).Enabled) continue;
                string xml = ((dynamic)task).Xml;
                var doc = ParseTask(xml);
                XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                var actions = doc.Root.Element(ns + "Actions")?.Elements().ToArray() ?? Array.Empty<XElement>();
                foreach (var exec in actions.Where(e => e.Name == ns + "Exec"))
                {
                    string command = exec.Element(ns + "Command")?.Value ?? "";
                    string args = exec.Element(ns + "Arguments")?.Value ?? "";
                    string action = command.TrimStart().StartsWith('"') ? command : '"' + command + '"';
                    var entry = FromCommand("Task", (string)((dynamic)task).Path, action + " " + args);
                    if (entry == null) continue;
                    entry.CanPause &= CanPauseTask(xml, sid);
                    entry.Snapshot = xml;
                    entry.Description = "Scheduled task: " + entry.Id + " — " + entry.Target;
                    if (!entry.CanPause) entry.ReviewReason ??= "Task actions or owner could not be verified";
                    entries.Add(entry);
                    break;
                }
            }
            finally { Release(task); }
            children = ((dynamic)folder).GetFolders(0);
            foreach (object child in (System.Collections.IEnumerable)children)
            try { ReadTasks(child, sid, entries, depth + 1); }
            finally { Release(child); }
        }
        finally { Release(tasks); Release(children); }
    }

    private static ControllerStartupConflict FromCommand(string kind, string id, string command)
    {
        string target = DirectExecutable(command);
        if (IsManaged(target)) return null;
        var version = ReadControllerVersion(target);
        if (version == null && command.IndexOf("DS4Windows", StringComparison.OrdinalIgnoreCase) < 0 &&
                command.IndexOf("VIIPER", StringComparison.OrdinalIgnoreCase) < 0) return null;
        string binaryHash = version == null ? "" : Hash(target);
        return new ControllerStartupConflict { Kind = kind, Id = id, Target = target ?? command,
            Snapshot = command, BinaryHash = binaryHash, CanPause = version != null,
            Identity = DescribeControllerBinary(version, binaryHash),
            ReviewReason = version == null ? "Executable could not be verified" : null };
    }

    internal static string DirectExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string input = command.Trim();
        string target;
        if (input[0] == '"')
        {
            int end = input.IndexOf('"', 1);
            if (end < 0) return null;
            target = input.Substring(1, end - 1);
        }
        else { target = input.Split(new[] { ' ', '\t' }, 2)[0]; }
        // Do not expand another user's profile through the administrator's
        // environment, resolve wrappers, search PATH, or inspect UNC targets.
        if (target.Contains('%') || target.Length < 4 || !char.IsLetter(target[0]) ||
                target[1] != ':' || target[2] != '\\' || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        try { return Path.GetFullPath(target); } catch { return null; }
    }

    private static bool IsManaged(string path) => SamePath(path, Path.Combine(ManagedRoot, "DS4Windows.exe")) ||
        SamePath(path, Path.Combine(ManagedRoot, "VIIPER", "viiper.exe"));
    internal static string ReadProcessOwnerSid(uint processId)
    {
        // SELECT projections do not retain an invocable WMI instance path.
        // Bind the numeric key explicitly before invoking this read-only method.
        using var instance = new ManagementObject("Win32_Process.Handle='" +
            processId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "'");
        using var owner = instance.InvokeMethod("GetOwnerSid", null, null);
        return Convert.ToUInt32(owner?["ReturnValue"]) == 0 ? owner?["Sid"] as string : null;
    }
    private static bool SamePath(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static FileVersionInfo ReadControllerVersion(string path)
    {
        try
        {
            if (path == null || !File.Exists(path)) return null;
            StartupSetupRecovery.RequirePlainPath(path);
            var version = FileVersionInfo.GetVersionInfo(path);
            return string.Equals(version.ProductName, "DS4Windows", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(version.ProductName, "VIIPER", StringComparison.OrdinalIgnoreCase) ||
                (version.ProductName?.Contains("DS4Windows", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (version.FileDescription?.StartsWith("VIIPER", StringComparison.OrdinalIgnoreCase) ?? false) ? version : null;
        }
        catch { return null; }
    }

    private static string DescribeControllerBinary(FileVersionInfo version, string binaryHash)
    {
        if (version == null) return null;
        string reportedVersion = version.ProductVersion?.Split('+', 2)[0].Trim();
        if (string.IsNullOrEmpty(reportedVersion) || reportedVersion.Length > 40 ||
            !reportedVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ' '))
            reportedVersion = version.FileMajorPart > 0 ? $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}" : "version unknown";
        if (string.Equals(version.ProductName, "VIIPER", StringComparison.OrdinalIgnoreCase) ||
            (version.FileDescription?.StartsWith("VIIPER", StringComparison.OrdinalIgnoreCase) ?? false))
            return "VIIPER " + reportedVersion;
        return $"DS4Windows {reportedVersion} · {DescribeFork(version.CompanyName, binaryHash)}";
    }

    internal static string DescribeFork(string company, string binaryHash)
    {
        // These archives ship with empty or inherited CompanyName fields.
        // Match the published EXE bytes, not a version range or copyright credit.
        switch (binaryHash)
        {
            // Jays2Kings/DS4Windows releases v1.4.52, v1.4.5, v1.4.401, v1.4.4 and v1.4.321.
            case "EB34F8552E8685BD013933BA6F5D8E7BF2B77B1E8EA43BC2287A0532B55C02E9":
            case "2C827980645361E6F5819BAA5718D5A57B2241231011F5A1C17701966FC17602":
            case "42E28B7FCA7CE1F537B34F4B1C4D0516637D8FB46C04D894E430035BF973B606":
            case "1BEFF78BDF00A7F3EE556A3D519AD90A4CF012D012469D758E78663E3EB2B2A2":
            case "3DCD7F9EE9373525287606A67408DA6FA27583A67C5A61D295C73EE066090169":
                return "Jays2Kings (release match)";
            // ds4windowsapp/DS4Windows release Official, both published 3.5 ZIPs.
            case "6267CBA17B87ADA8F13EC6E187B309E3C76AA087ACF2C255AB19DC2DB6799A34":
            case "AFD11142CF19FBD0727537B95A0DA49F0895B840C659FCF29252912772B74CCB":
                return "ds4windowsapp (release match)";
        }
        string fork = company?.Trim().ToUpperInvariant() switch
        {
            "PAV-OSMOLSKI" => "Pav-Osmolski",
            "HBASHTON" => "hbashton",
            "SCHMALDEO" => "schmaldeo",
            "RYOCHAN7" => "Ryochan7",
            "JAYS2KINGS" => "Jays2Kings",
            "EMUCORES" => "EmuCores (Nexus)",
            "DS4WINDOWSAPP" => "ds4windowsapp",
            _ => null
        };
        return fork == null ? "Fork unknown" : "Reported fork: " + fork;
    }

    internal static bool IsMissingDs4WindowsTarget(string target)
    {
        // A missing or inaccessible target must never grant permission to act on a running process or task.
        if (!string.Equals(Path.GetFileName(target), "DS4Windows.exe", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            string root = Path.GetPathRoot(target);
            if (root == null || root.Length != 3 || root[1] != ':' || root[2] != '\\' ||
                target.AsSpan(root.Length).Contains(':') || new DriveInfo(root).DriveType != DriveType.Fixed ||
                ReadPathAttributes(target) != null) return false;
            for (string parent = Path.GetDirectoryName(target); parent != null; parent = Path.GetDirectoryName(parent))
            {
                var attributes = ReadPathAttributes(parent);
                if (attributes.HasValue && (attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                    return false;
                if (SamePath(root, parent)) return attributes.HasValue;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return false;
    }

    private static FileAttributes? ReadPathAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string ResolveSid(string name)
    {
        try { return new SecurityIdentifier(name).Value; }
        catch { try { return ((SecurityIdentifier)new NTAccount(name ?? "").Translate(typeof(SecurityIdentifier))).Value; } catch { return ""; } }
    }
    private static RegistryKey OpenRun(string scope, RegistryView view, bool write)
    {
        using var hive = RegistryKey.OpenBaseKey(scope == "Machine" ? RegistryHive.LocalMachine : RegistryHive.Users, view);
        return hive.OpenSubKey((scope == "Machine" ? "" : scope + "\\") + RunKey, write);
    }
    private static bool IsStartupDisabled(string scope, RegistryView view, string name, bool folder)
    {
        using var hive = RegistryKey.OpenBaseKey(scope == "Machine" ? RegistryHive.LocalMachine : RegistryHive.Users, view);
        using var key = hive.OpenSubKey((scope == "Machine" ? "" : scope + "\\") +
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" +
            (folder ? "StartupFolder" : view == RegistryView.Registry32 ? "Run32" : "Run"));
        return key?.GetValue(name) is byte[] state && state.Length >= 12 && (state[0] == 3 || state[0] == 7);
    }
    private static ControllerStartupConflict InspectionFailure(string source, Exception error) => new() {
        Kind = "Inspection", Id = source, Target = "", Snapshot = error.GetType().Name,
        Description = source + " could not be inspected. Resolve access or close competing applications before continuing.", CanPause = false };
    private static XDocument ParseTask(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
    internal static bool CanPauseTask(string xml, string sid)
    {
        var doc = ParseTask(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var actions = doc.Root.Element(ns + "Actions")?.Elements().ToArray();
        var principals = doc.Root.Element(ns + "Principals")?.Elements().ToArray();
        if (actions?.Length != 1 || actions[0].Name != ns + "Exec" || principals?.Length != 1) return false;
        string owner = ResolveSid(principals[0].Element(ns + "UserId")?.Value);
        string logon = principals[0].Element(ns + "LogonType")?.Value;
        return owner == sid && (logon == "InteractiveToken" || logon == "S4U");
    }
    internal static bool TaskCanBeRestoredOver(string originalXml, string currentXml, string sid)
    {
        if (!CanPauseTask(originalXml, sid)) return false;
        var original = ParseTask(originalXml); var current = ParseTask(currentXml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        foreach (var doc in new[] { original, current }) doc.Root.Element(ns + "Settings")?.Element(ns + "Enabled")?.Remove();
        return XNode.DeepEquals(original, current);
    }
    private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }

    internal static string Pause(string sid, string expected, Action<string> log = null)
    {
        string backupDirectory = null;
        ApplyReviewed(expected, () => Scan(sid), entries => {
            byte[] contents = JsonSerializer.SerializeToUtf8Bytes(new ControllerConflictBackup { UserSid = sid, Entries = entries });
            if (contents.Length > 4 * 1024 * 1024) throw new IOException("Startup backup exceeds its size limit; no entries were paused.");
            string root = StartupSetupRecovery.EnsureProtectedStagingRoot(sid);
            backupDirectory = Path.Combine(root, "controller-conflicts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupDirectory);
            StartupSetupRecovery.ProtectSnapshot(backupDirectory, sid);
            string receipt = Path.Combine(backupDirectory, "conflicts.json");
            using (var stream = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(contents);
                stream.Flush(true);
            }
            string helper = Path.Combine(backupDirectory, "DS4Windows.ConflictRecovery.exe");
            File.Copy(Environment.ProcessPath, helper, false);
            File.WriteAllText(Path.Combine(backupDirectory, "RESTORE.txt"),
                "To restore these startup entries, close DS4Windows and VIIPER, open Command Prompt as administrator, and run:\r\n\"" + helper + "\" restore-conflicts\r\nExisting or changed entries are never overwritten. This does not launch either application.\r\nIf a newer DS4Windows installation now owns RunDS4Windows or RunVIIPER, uninstall it before restoring a previous task with that same name.\r\n");
            log?.Invoke("Competing controller startup backup and restore instructions: " + backupDirectory);
        }, entry => PauseEntry(entry, sid));
        return backupDirectory;
    }

    private static void PauseEntry(ControllerStartupConflict entry, string sid)
    {
        if (!entry.CanPause || !TargetStillMatches(entry, sid))
            throw new IOException("A competing executable changed after review. Startup backup was retained; reopen setup.");
        if (entry.Kind == "Run")
        {
            using var key = OpenRun(entry.RegistryScope, (RegistryView)entry.RegistryView, true);
            if (key == null || !Equals(key.GetValue(entry.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames), entry.Snapshot) ||
                    (int)key.GetValueKind(entry.ValueName) != entry.ValueKind) throw new IOException("A startup entry changed after review.");
            key.DeleteValue(entry.ValueName, true);
        }
        else if (entry.Kind == "Task") WithTask(entry.Id, (folder, name, task) => {
            if (task == null || !string.Equals((string)((dynamic)task).Xml, entry.Snapshot, StringComparison.Ordinal))
                throw new IOException("A scheduled task changed after review.");
            ((dynamic)task).Enabled = false;
            // Preserve the original XML in the protected receipt while freeing
            // reserved root names for this installer's independently owned tasks.
            if (IsReserved(entry.Id)) ((dynamic)folder).DeleteTask(name, 0);
        });
        else if (entry.Kind == "Shortcut")
        {
            if (entry.TargetMissing && !StartupFolders(sid).Any(folder => folder.AllowOrphanPause &&
                folder.Scope == entry.RegistryScope && SamePath(folder.Path, Path.GetDirectoryName(entry.Id))))
                throw new IOException("The user Startup folder changed after review.");
            StartupSetupRecovery.RequirePlainPath(entry.Id);
            if (!File.ReadAllBytes(entry.Id).SequenceEqual(Convert.FromBase64String(entry.Payload)))
                throw new IOException("A startup shortcut changed after review.");
            File.Delete(entry.Id);
        }
        else if (entry.Kind == "Process")
        {
            Process candidate;
            try { candidate = Process.GetProcessById(int.Parse(entry.Id, System.Globalization.CultureInfo.InvariantCulture)); }
            catch (ArgumentException) { return; } // It already exited; never substitute another process.
            using var process = candidate;
            if (!SamePath(process.MainModule?.FileName, entry.Target) ||
                    ManagementDateTimeConverter.ToDateTime(entry.Snapshot).ToUniversalTime().Ticks / 10 != process.StartTime.ToUniversalTime().Ticks / 10)
                throw new IOException("A competing process changed after review.");
            if (!process.CloseMainWindow() || !process.WaitForExit(3000)) { process.Kill(); if (!process.WaitForExit(5000)) throw new IOException("A competing process could not be stopped."); }
        }
        else throw new IOException("This startup entry requires manual review.");
    }

    internal static bool TargetStillMatches(ControllerStartupConflict entry, string sid) => entry.TargetMissing
        ? entry.Kind == "Shortcut" && entry.RegistryScope == sid && IsMissingDs4WindowsTarget(entry.Target)
        : ReadControllerVersion(entry.Target) != null && Hash(entry.Target) == entry.BinaryHash;

    private static bool IsReserved(string path) => path.Equals(@"\RunVIIPER", StringComparison.OrdinalIgnoreCase) || path.Equals(@"\RunDS4Windows", StringComparison.OrdinalIgnoreCase);
    private static void WithTask(string path, Action<object, string, object> action)
    {
        object service = null, folder = null, task = null;
        try {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); ((dynamic)service).Connect();
            int split = path.LastIndexOf('\\');
            folder = ((dynamic)service).GetFolder(split == 0 ? @"\" : path.Substring(0, split));
            string name = path.Substring(split + 1);
            try { task = ((dynamic)folder).GetTask(name); }
            catch (COMException error) when (error.HResult == unchecked((int)0x80070002)) { }
            action(folder, name, task);
        }
        finally { Release(task); Release(folder); Release(service); }
    }

    internal static void Restore(string directory)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(directory), "^controller-conflicts-[0-9a-f]{32}$"))
            throw new IOException("Unexpected startup backup location.");
        StartupSetupRecovery.RequireProtectedPath(directory, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        string path = Path.Combine(directory, "conflicts.json");
        StartupSetupRecovery.RequireProtectedPath(path, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Startup backup exceeds its size limit.");
        var backup = JsonSerializer.Deserialize<ControllerConflictBackup>(File.ReadAllText(path));
        if (backup?.Schema != 1 || backup.Entries == null || backup.Entries.Count > 512) throw new IOException("Unsupported startup backup.");
        StartupSetupStore.ValidateSid(backup.UserSid);
        foreach (var entry in backup.Entries.Where(e => e.Kind != "Process"))
        {
            if (entry.Kind == "Run")
            {
                if (entry.RegistryScope != "Machine" && entry.RegistryScope != backup.UserSid) throw new IOException("Unexpected startup owner.");
                if ((entry.RegistryView != (int)RegistryView.Registry64 && entry.RegistryView != (int)RegistryView.Registry32) ||
                    (entry.ValueKind != (int)RegistryValueKind.String && entry.ValueKind != (int)RegistryValueKind.ExpandString))
                    throw new IOException("Unexpected startup registry value type.");
                using var key = OpenRun(entry.RegistryScope, (RegistryView)entry.RegistryView, true);
                if (key == null) throw new IOException("Startup registry location is unavailable.");
                object current = key.GetValue(entry.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (Equals(current, entry.Snapshot) && (int)key.GetValueKind(entry.ValueName) == entry.ValueKind) continue;
                if (current != null) throw new IOException("A newer startup value exists; it was not overwritten.");
                key.SetValue(entry.ValueName, entry.Snapshot, (RegistryValueKind)entry.ValueKind);
            }
            else if (entry.Kind == "Shortcut")
            {
                if (!StartupFolders(backup.UserSid).Any(f => f.Scope == entry.RegistryScope && SamePath(f.Path, Path.GetDirectoryName(entry.Id))) ||
                    !Path.GetExtension(entry.Id).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Unexpected startup shortcut location.");
                StartupSetupRecovery.RequirePlainPath(Path.GetDirectoryName(entry.Id));
                byte[] original = Convert.FromBase64String(entry.Payload);
                if (original.Length > 64 * 1024) throw new IOException("Startup shortcut exceeds its size limit.");
                if (File.Exists(entry.Id))
                {
                    StartupSetupRecovery.RequirePlainPath(entry.Id);
                    if (!File.ReadAllBytes(entry.Id).SequenceEqual(original)) throw new IOException("A newer startup shortcut exists; it was not overwritten.");
                    continue;
                }
                using var stream = new FileStream(entry.Id, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(original); stream.Flush(true);
            }
            else if (entry.Kind == "Task") WithTask(entry.Id, (folder, name, task) => {
                if (task != null)
                {
                    if (!TaskCanBeRestoredOver(entry.Snapshot, (string)((dynamic)task).Xml, backup.UserSid))
                        throw new IOException("A newer task exists; it was not overwritten.");
                    ((dynamic)task).Enabled = true;
                }
                else {
                    var doc = ParseTask(entry.Snapshot);
                    XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                    string owner = doc.Descendants(ns + "UserId").FirstOrDefault()?.Value;
                    string logon = doc.Descendants(ns + "LogonType").FirstOrDefault()?.Value;
                    if (ResolveSid(owner) != backup.UserSid || (logon != "InteractiveToken" && logon != "S4U"))
                        throw new IOException("Unexpected task owner or logon type in startup backup.");
                    ((dynamic)folder).RegisterTask(name, entry.Snapshot, 2, owner, null, logon == "S4U" ? 2 : 3, null);
                }
            });
            else throw new IOException("Unsupported startup backup entry.");
        }
    }
}
