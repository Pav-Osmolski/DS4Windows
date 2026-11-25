using DS4Windows.Installation;
using System.IO;
using System.Runtime.InteropServices;

namespace DS4WindowsTests;

[TestClass]
public sealed class ControllerStartupConflictsTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private static ControllerStartupConflict Entry(string kind, string id = "example") => new()
    {
        Kind = kind, Id = id, Target = @"C:\Other\DS4Windows.exe", Snapshot = "original",
        BinaryHash = "verified", CanPause = true
    };

    [TestMethod]
    public void ProcessOwnershipUsesAnInvocableKeyedWmiInstance()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        Assert.AreEqual(identity.User.Value, ControllerStartupConflicts.ReadProcessOwnerSid((uint)Environment.ProcessId));
    }

    [TestMethod]
    public void BackupPrecedesAllChangesAndStartupsArePausedBeforeProcesses()
    {
        var entries = new List<ControllerStartupConflict> { Entry("Process"), Entry("Task"), Entry("Run"), Entry("Shortcut") };
        var calls = new List<string>();
        ControllerStartupConflicts.ApplyReviewed(ControllerStartupConflicts.Fingerprint(entries), () => entries,
            _ => calls.Add("backup"), e => calls.Add(e.Kind));
        Assert.AreEqual("backup", calls[0]);
        Assert.AreEqual("Process", calls[^1]);
        CollectionAssert.AreEquivalent(new[] { "backup", "Process", "Task", "Run", "Shortcut" }, calls);
    }

    [TestMethod]
    public void BackupFailureMakesNoChanges()
    {
        var entries = new List<ControllerStartupConflict> { Entry("Task") };
        int changes = 0;
        Assert.ThrowsExactly<IOException>(() => ControllerStartupConflicts.ApplyReviewed(
            ControllerStartupConflicts.Fingerprint(entries), () => entries, _ => throw new IOException("disk full"), _ => changes++));
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    [DataRow("new-process")]
    [DataRow("changed-command")]
    [DataRow("changed-binary")]
    [DataRow("reused-pid")]
    [DataRow("unverified")]
    public void ChangedOrUnverifiedInventoryCannotBeApproved(string scenario)
    {
        var entries = new List<ControllerStartupConflict> { Entry("Process", "42") };
        string approved = ControllerStartupConflicts.Fingerprint(entries);
        switch (scenario)
        {
            case "new-process": entries.Add(Entry("Process", "43")); break;
            case "changed-command": entries[0].Target = @"C:\New\DS4Windows.exe"; break;
            case "changed-binary": entries[0].BinaryHash = "new hash"; break;
            case "reused-pid": entries[0].Snapshot = "new creation time"; break;
            case "unverified": entries[0].CanPause = false; approved = ControllerStartupConflicts.Fingerprint(entries); break;
        }
        bool backedUp = false, changed = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => ControllerStartupConflicts.ApplyReviewed(approved,
            () => entries, _ => backedUp = true, _ => changed = true));
        Assert.IsFalse(backedUp); Assert.IsFalse(changed);
    }

    [TestMethod]
    public void FingerprintDoesNotDependOnEnumerationOrderButIncludesShortcutBytes()
    {
        var one = Entry("Shortcut", "one"); one.Payload = "old";
        var two = Entry("Task", "two");
        string before = ControllerStartupConflicts.Fingerprint(new[] { one, two });
        Assert.AreEqual(before, ControllerStartupConflicts.Fingerprint(new[] { two, one }));
        one.Payload = "changed";
        Assert.AreNotEqual(before, ControllerStartupConflicts.Fingerprint(new[] { one, two }));
    }

    [TestMethod]
    [DataRow("Pav-Osmolski", "Reported fork: Pav-Osmolski")]
    [DataRow(" hbashton ", "Reported fork: hbashton")]
    [DataRow("schmaldeo", "Reported fork: schmaldeo")]
    [DataRow("Ryochan7", "Reported fork: Ryochan7")]
    [DataRow("EmuCores", "Reported fork: EmuCores (Nexus)")]
    [DataRow("", "Fork unknown")]
    [DataRow("unrelated hbashton fork", "Fork unknown")]
    public void IdentifiesOnlyExactReportedForkNames(string company, string expected) =>
        Assert.AreEqual(expected, ControllerStartupConflicts.DescribeFork(company, "unknown hash"));

    [TestMethod]
    public void PublishedExecutableMatchesOverrideInheritedOrMissingMetadata()
    {
        Assert.AreEqual("Jays2Kings (release match)", ControllerStartupConflicts.DescribeFork("",
            "EB34F8552E8685BD013933BA6F5D8E7BF2B77B1E8EA43BC2287A0532B55C02E9"));
        Assert.AreEqual("ds4windowsapp (release match)", ControllerStartupConflicts.DescribeFork("schmaldeo",
            "6267CBA17B87ADA8F13EC6E187B309E3C76AA087ACF2C255AB19DC2DB6799A34"));
    }

    [TestMethod]
    public void OrphanedStartupLinkCanBePausedOnlyForItsUserWhileItsTargetIsMissing()
    {
        var temp = Directory.CreateTempSubdirectory("ds4-startup-conflict-");
        string target = Path.Combine(temp.FullName, "DS4Windows.exe");
        string shortcutPath = Path.Combine(temp.FullName, "DS4Windows.lnk");
        object shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            try
            {
                ((dynamic)shortcut).TargetPath = target;
                ((dynamic)shortcut).Arguments = "-m";
                ((dynamic)shortcut).Save();
            }
            finally { Marshal.FinalReleaseComObject(shortcut); }

            var entry = ControllerStartupConflicts.ReadStartupShortcut(shell, shortcutPath, Sid, allowOrphanPause: true);
            Assert.IsNotNull(entry);
            Assert.AreEqual(target, entry.Target);
            Assert.IsTrue(entry.TargetMissing);
            Assert.IsTrue(entry.CanPause);
            Assert.IsTrue(ControllerStartupConflicts.TargetStillMatches(entry, Sid));
            Assert.IsFalse(ControllerStartupConflicts.TargetStillMatches(entry, "S-1-5-18"));
            CollectionAssert.AreEqual(File.ReadAllBytes(shortcutPath), Convert.FromBase64String(entry.Payload));
            Assert.IsFalse(ControllerStartupConflicts.ReadStartupShortcut(shell, shortcutPath, "Machine", allowOrphanPause: false).CanPause);

            File.WriteAllText(target, "different file appeared");
            Assert.IsFalse(ControllerStartupConflicts.TargetStillMatches(entry, Sid));
            var changed = ControllerStartupConflicts.ReadStartupShortcut(shell, shortcutPath, Sid, allowOrphanPause: true);
            Assert.IsFalse(changed.TargetMissing);
            Assert.IsFalse(changed.CanPause);
        }
        finally
        {
            if (shell != null) Marshal.FinalReleaseComObject(shell);
            File.Delete(shortcutPath);
            File.Delete(target);
            temp.Delete();
        }
    }

    [TestMethod]
    [DataRow("\"C:\\Other Folder\\DS4Windows.exe\" -m", "C:\\Other Folder\\DS4Windows.exe")]
    [DataRow("C:\\Other\\viiper.exe server", "C:\\Other\\viiper.exe")]
    [DataRow("DS4Windows.exe", null)]
    [DataRow("%LOCALAPPDATA%\\DS4Windows.exe", null)]
    [DataRow("\\\\server\\folder\\DS4Windows.exe", null)]
    [DataRow("powershell.exe -File DS4Windows.ps1", null)]
    [DataRow("\"C:\\Other\\viiper.exe", null)]
    public void OnlyDirectLocalExecutablesAreResolved(string command, string expected) =>
        Assert.AreEqual(expected, ControllerStartupConflicts.DirectExecutable(command));

    [TestMethod]
    [DataRow("InteractiveToken", true)]
    [DataRow("S4U", true)]
    [DataRow("Password", false)]
    [DataRow("ServiceAccount", false)]
    public void TaskRestoreDoesNotRequireOrCapturePasswords(string logon, bool expected)
    {
        string xml = TaskXml(logon);
        Assert.AreEqual(expected, ControllerStartupConflicts.CanPauseTask(xml, Sid));
        Assert.IsFalse(ControllerStartupConflicts.CanPauseTask(xml.Replace(Sid, "S-1-5-18"), Sid));
        Assert.IsFalse(ControllerStartupConflicts.CanPauseTask(xml.Replace("</Actions>", "<Exec><Command>C:\\Other.exe</Command></Exec></Actions>"), Sid));
    }

    [TestMethod]
    public void TaskInspectionRejectsExternalEntities() => Assert.ThrowsExactly<System.Xml.XmlException>(() =>
        ControllerStartupConflicts.CanPauseTask("<!DOCTYPE Task [<!ENTITY x SYSTEM 'file:///C:/private'>]><Task>&x;</Task>", Sid));

    [TestMethod]
    public void RestoreOnlyReenablesAnUnchangedTaskAndCannotOverwriteNewOwnerOrActions()
    {
        string original = TaskXml("InteractiveToken").Replace("</Task>", "<Settings><Enabled>true</Enabled></Settings></Task>");
        string paused = original.Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>");
        Assert.IsTrue(ControllerStartupConflicts.TaskCanBeRestoredOver(original, paused, Sid));
        Assert.IsFalse(ControllerStartupConflicts.TaskCanBeRestoredOver(original, paused.Replace("Other", "NewCopy"), Sid));
        Assert.IsFalse(ControllerStartupConflicts.TaskCanBeRestoredOver(original, paused.Replace(Sid, "S-1-5-18"), Sid));
        Assert.IsFalse(ControllerStartupConflicts.TaskCanBeRestoredOver(original, paused.Replace("</Actions>", "<Exec><Command>extra.exe</Command></Exec></Actions>"), Sid));
        Assert.IsFalse(ControllerStartupConflicts.TaskCanBeRestoredOver(original, paused, "S-1-5-18"));
    }

    private static string TaskXml(string logon) =>
        $"<Task xmlns='http://schemas.microsoft.com/windows/2004/02/mit/task'><Principals><Principal><UserId>{Sid}</UserId><LogonType>{logon}</LogonType></Principal></Principals><Actions><Exec><Command>C:\\Other\\DS4Windows.exe</Command></Exec></Actions></Task>";
}
