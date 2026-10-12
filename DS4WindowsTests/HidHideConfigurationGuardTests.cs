using DS4WinWPF.DS4Control;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace DS4WindowsTests;

[TestClass]
public class HidHideConfigurationGuardTests
{
    private const string Alias = @"\Device\HarddiskVolume3\Users\test\AppData\Local\Microsoft\WindowsApps\python.exe";
    private const string Real = @"\Device\HarddiskVolume3\Program Files\DS4Windows\DS4Windows.exe";
    private const string Missing = @"\Device\HarddiskVolume3\missing\app.exe";
    private const string Symlink = @"\Device\HarddiskVolume3\links\app.exe";

    [TestMethod]
    public void InspectClosesDriverBeforeFilesystemScanAndDoesNotMutate()
    {
        var fixture = new Fixture();
        fixture.OnInspect = () => Assert.AreEqual(0, fixture.OpenHandles);
        var audit = fixture.Guard.Inspect();
        Assert.IsNull(audit.Failure);
        CollectionAssert.AreEqual(new[] { Alias }, audit.Aliases.ToArray());
        Assert.IsFalse(audit.CanOpen);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Backups);
        Assert.AreEqual(0, fixture.OpenHandles);
    }

    [TestMethod]
    public void ReadyAndDeclinedAuditNeverBackUpOrWrite()
    {
        var fixture = new Fixture();
        fixture.Applications.Remove(Alias);
        Assert.IsTrue(fixture.Guard.Inspect().CanOpen);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Backups);
        Assert.IsFalse(fixture.Guard.Repair(fixture.Guard.Inspect()).Succeeded);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.OpenHandles);
    }

    [TestMethod]
    [DataRow("open")]
    [DataRow("inverse")]
    [DataRow("applications")]
    [DataRow("active")]
    [DataRow("devices")]
    public void AnyFailedReadBlocksOpeningAndNeverMutates(string failure)
    {
        var fixture = new Fixture { ReadFailure = failure };
        var audit = fixture.Guard.Inspect();
        Assert.IsFalse(audit.CanOpen);
        Assert.IsNotNull(audit.Failure);
        Assert.IsFalse(fixture.Guard.Repair(audit).Succeeded);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Backups);
        Assert.AreEqual(0, fixture.OpenHandles);
    }

    [TestMethod]
    [DataRow(false, 0xD800)]
    [DataRow(false, 0xDC00)]
    [DataRow(true, 0xD800)]
    [DataRow(true, 0xDC00)]
    public void NonRoundTrippablePolicyTextNeverReachesInspectionBackupOrWrite(bool device, int codeUnit)
    {
        var fixture = new Fixture();
        string unusualPath = Missing + (char)codeUnit;
        (device ? fixture.Devices : fixture.Applications).Add(unusualPath);
        fixture.OnInspect = () => Assert.Fail("Unsafe policy must be rejected before path inspection.");
        var audit = fixture.Guard.Inspect();
        Assert.IsNotNull(audit.Failure);
        Assert.IsFalse(audit.CanOpen);
        Assert.IsFalse(fixture.Guard.Repair(audit).Succeeded);
        Assert.AreEqual(0, fixture.Backups);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.OpenHandles);
        CollectionAssert.Contains(device ? fixture.Devices : fixture.Applications, unusualPath);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NonRoundTrippableTextAddedAfterConfirmationPreventsBackupAndWrite(bool device)
    {
        var fixture = new Fixture();
        var audit = fixture.Guard.Inspect();
        (device ? fixture.Devices : fixture.Applications).Add(Missing + '\uD800');
        Assert.IsFalse(fixture.Guard.Repair(audit).Succeeded);
        Assert.AreEqual(0, fixture.Backups);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.OpenHandles);
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(1920)]
    [DataRow(87)]
    public void UnknownFileErrorsAreNotPermissionRemovalEvidence(int error)
    {
        var fixture = new Fixture { UnknownError = error };
        var audit = fixture.Guard.Inspect();
        Assert.IsNotNull(audit.Failure);
        Assert.IsFalse(fixture.Guard.Repair(audit).Succeeded);
        CollectionAssert.Contains(fixture.Applications, Real);
        Assert.AreEqual(0, fixture.Writes);
    }

    [TestMethod]
    public void InverseModeIsNeverRepaired()
    {
        var fixture = new Fixture { Inverse = true };
        var audit = fixture.Guard.Inspect();
        Assert.IsNotNull(audit.Failure);
        Assert.IsFalse(fixture.Guard.Repair(audit).Succeeded);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Backups);
    }

    [TestMethod]
    public void RepairRemovesOnlyProvenAliasesAndPreservesEveryOtherSetting()
    {
        var fixture = new Fixture();
        var audit = fixture.Guard.Inspect();
        fixture.OnInspect = () => Assert.AreEqual(1, fixture.OpenHandles);
        fixture.OnBackup = () => Assert.AreEqual(1, fixture.OpenHandles);
        var result = fixture.Guard.Repair(audit);
        Assert.IsTrue(result.Succeeded, result.Failure);
        Assert.AreEqual("backup.json", result.BackupPath);
        CollectionAssert.AreEqual(new[] { Real, Missing, Symlink }, fixture.Applications);
        CollectionAssert.AreEqual(new[] { "user-owned-HID", "DS4-owned-HID" }, fixture.Devices);
        CollectionAssert.AreEqual(new[] { Real, Alias, Missing, Symlink }, fixture.Saved.Applications.ToArray());
        Assert.IsTrue(fixture.Active);
        Assert.IsFalse(fixture.Inverse);
        Assert.AreEqual(1, fixture.Backups);
        Assert.AreEqual(1, fixture.Writes);
        Assert.AreEqual(0, fixture.OpenHandles);
        fixture.OnInspect = null;
        Assert.IsTrue(fixture.Guard.Inspect().CanOpen);
    }

    [TestMethod]
    [DataRow("applications")]
    [DataRow("order")]
    [DataRow("case")]
    [DataRow("inverse")]
    [DataRow("active")]
    [DataRow("devices")]
    public void ChangedSnapshotAfterConfirmationAborts(string field)
    {
        var fixture = new Fixture();
        var audit = fixture.Guard.Inspect();
        switch (field)
        {
            case "applications": fixture.Applications.Add("another-app"); break;
            case "order": fixture.Applications.Reverse(); break;
            case "case": fixture.Applications[0] = Real.ToUpperInvariant(); break;
            case "inverse": fixture.Inverse = true; break;
            case "active": fixture.Active = false; break;
            case "devices": fixture.Devices.Add("new-controller"); break;
        }
        Assert.IsFalse(fixture.Guard.Repair(audit).Succeeded);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Backups);
        Assert.AreEqual(0, fixture.OpenHandles);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedOrEmptyBackupNeverMutates(bool empty)
    {
        var fixture = new Fixture { EmptyBackup = empty, ThrowBackup = !empty };
        var result = fixture.Guard.Repair(fixture.Guard.Inspect());
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.OpenHandles);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(5)]
    public void RevalidateAfterBackupRejectsReplacedOrUnreadableAlias(int error)
    {
        var fixture = new Fixture();
        var audit = fixture.Guard.Inspect();
        fixture.OnBackup = () => { fixture.AliasPresent = false; fixture.AliasError = error; };
        var result = fixture.Guard.Repair(audit);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("backup.json", result.BackupPath);
        Assert.AreEqual(0, fixture.Writes);
        CollectionAssert.Contains(fixture.Applications, Alias);
    }

    [TestMethod]
    public void FailedWriteRemainsUncertainEvenIfDriverAppliedTheList()
    {
        var fixture = new Fixture { WriteReturnsFalse = true };
        var result = fixture.Guard.Repair(fixture.Guard.Inspect());
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("backup.json", result.BackupPath);
        Assert.AreEqual(1, fixture.Writes);
        Assert.IsFalse(fixture.Applications.Contains(Alias));
        Assert.AreEqual(0, fixture.OpenHandles);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PostWriteFailureRetainsBackupWithoutRollbackOrRetry(bool unreadable)
    {
        var fixture = new Fixture();
        fixture.OnWrite = () =>
        {
            if (unreadable) fixture.ReadFailure = "applications";
            else fixture.Devices.Add("unexpected-state");
        };
        var result = fixture.Guard.Repair(fixture.Guard.Inspect());
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("backup.json", result.BackupPath);
        Assert.AreEqual(1, fixture.Writes);
        Assert.AreEqual(0, fixture.OpenHandles);
    }

    [TestMethod]
    public void DuplicateAliasEntriesAreRemovedWithoutDeduplicatingUnrelatedEntries()
    {
        var fixture = new Fixture();
        fixture.Applications.Add(Alias);
        fixture.Applications.Add(Real);
        Assert.IsTrue(fixture.Guard.Repair(fixture.Guard.Inspect()).Succeeded);
        CollectionAssert.AreEqual(new[] { Real, Missing, Symlink, Real }, fixture.Applications);
    }

    [TestMethod]
    public void SnapshotCannotBeModifiedThroughOriginalListsOrExposedProperties()
    {
        var fixture = new Fixture();
        var audit = fixture.Guard.Inspect();
        fixture.Applications.Clear();
        Assert.AreEqual(4, audit.Snapshot.Applications.Count);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<string>)audit.Snapshot.Applications)[0] = "replacement");
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<string>)audit.Aliases)[0] = "replacement");
    }

    [TestMethod]
    public void DurableBackupContainsExactPolicyAndUsesUniqueFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "ds4-hidhide-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            string supplementaryPath = Missing + "\U0001F3AE";
            var snapshot = new HidHideConfigurationSnapshot(new[] { Real, Alias, supplementaryPath },
                new[] { "user-device" }, false, true);
            string first = HidHideConfigurationGuard.WriteBackup(root, snapshot);
            string second = HidHideConfigurationGuard.WriteBackup(root, snapshot);
            Assert.AreNotEqual(first, second);
            using var json = JsonDocument.Parse(File.ReadAllBytes(first));
            Assert.AreEqual(1, json.RootElement.GetProperty("Schema").GetInt32());
            Assert.AreEqual(Alias, json.RootElement.GetProperty("Applications")[1].GetString());
            Assert.AreEqual(supplementaryPath, json.RootElement.GetProperty("Applications")[2].GetString());
            Assert.AreEqual("user-device", json.RootElement.GetProperty("Devices")[0].GetString());
            Assert.IsTrue(json.RootElement.GetProperty("Active").GetBoolean());
            Assert.IsFalse(json.RootElement.GetProperty("Inverse").GetBoolean());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DirectBackupRejectsNonRoundTrippableTextBeforeCreatingFiles(bool device)
    {
        string root = Path.Combine(Path.GetTempPath(), "ds4-hidhide-invalid-backup-" + Guid.NewGuid().ToString("N"));
        var snapshot = new HidHideConfigurationSnapshot(new[] { device ? Real : Missing + '\uD800' },
            new[] { device ? "device-\uDC00" : "user-device" }, false, true);
        Assert.ThrowsExactly<IOException>(() => HidHideConfigurationGuard.WriteBackup(root, snapshot));
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void RealLaunchHandlerChecksAndConfirmsBeforeRepairOrLaunching()
    {
        string source = RepositorySource("DS4Windows", "DS4Forms", "MainWindow.xaml.cs");
        int begin = source.IndexOf("private async void HidHideBtn_Click", StringComparison.Ordinal);
        int end = source.IndexOf("private void FakeExeNameExplainBtn_Click", begin, StringComparison.Ordinal);
        string handler = source.Substring(begin, end - begin);
        Assert.IsTrue(handler.IndexOf("try", StringComparison.Ordinal) <
            handler.IndexOf("Util.GetHidHideClientPath()", StringComparison.Ordinal));
        Assert.IsTrue(handler.IndexOf("InspectHidHideConfiguration()", StringComparison.Ordinal) <
            handler.IndexOf("Process.Start(", StringComparison.Ordinal));
        StringAssert.Contains(handler, "MessageBoxResult.No) != MessageBoxResult.Yes)");
        Assert.IsTrue(handler.IndexOf("MessageBoxResult.No) != MessageBoxResult.Yes)", StringComparison.Ordinal) <
            handler.IndexOf("RepairHidHideConfiguration(audit)", StringComparison.Ordinal));
        StringAssert.Contains(handler, "finally { hidHideClientLaunchPending = false; }");
        StringAssert.Contains(handler, "repair.BackupPath");
        Assert.IsFalse(handler.Contains("catch { }", StringComparison.Ordinal));
    }

    [TestMethod]
    public void StartupAndAutoProfileRegistrationAreGuardedBeforeDriverAccess()
    {
        string source = RepositorySource("DS4Windows", "DS4Control", "ControlService.cs");
        int begin = source.IndexOf("public void CheckHidHidePresence", StringComparison.Ordinal);
        int end = source.IndexOf("internal static bool CanRegisterHidHideApplication", begin, StringComparison.Ordinal);
        string registration = source.Substring(begin, end - begin);
        StringAssert.Contains(registration, "AddExe && !CanRegisterHidHideApplication(ExePath");
        Assert.IsTrue(registration.IndexOf("CanRegisterHidHideApplication(ExePath", StringComparison.Ordinal) <
            registration.IndexOf("new HidHideAPIDevice()", StringComparison.Ordinal));
        string autoProfile = RepositorySource("DS4Windows", "DS4Forms", "ViewModels", "AutoProfilesViewModel.cs");
        StringAssert.Contains(autoProfile, "CheckHidHidePresence(autoProf.Path, autoProf.Filename, addExe)");
    }

    private static string RepositorySource(params string[] relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(new[] { directory.FullName }.Concat(relative).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("Source not found", Path.Combine(relative));
    }

    private sealed class Fixture
    {
        internal readonly List<string> Applications = new() { Real, Alias, Missing, Symlink };
        internal readonly List<string> Devices = new() { "user-owned-HID", "DS4-owned-HID" };
        internal bool Active = true, Inverse, AliasPresent = true;
        internal bool EmptyBackup, ThrowBackup, WriteReturnsFalse;
        internal string ReadFailure;
        internal int OpenHandles, Writes, Backups, AliasError, UnknownError;
        internal Action OnInspect, OnBackup, OnWrite;
        internal HidHideConfigurationSnapshot Saved;
        internal HidHideConfigurationGuard Guard { get; }

        internal Fixture()
        {
            Guard = new HidHideConfigurationGuard(repair => new Device(this, repair),
                Inspect, snapshot =>
                {
                    Backups++;
                    Saved = snapshot;
                    OnBackup?.Invoke();
                    if (ThrowBackup) throw new IOException("Backup write failed");
                    return EmptyBackup ? null : "backup.json";
                });
        }

        private bool Inspect(string path, out bool alias, out int error)
        {
            OnInspect?.Invoke();
            alias = path == Alias && AliasPresent;
            error = path == Missing ? 2 : path == Real ? UnknownError : path == Alias ? AliasError : 0;
            return error == 0;
        }

        private sealed class Device : IHidHideConfigurationDevice
        {
            private readonly Fixture fixture;
            private readonly bool repair;
            private bool disposed;
            internal Device(Fixture fixture, bool repair)
            {
                this.fixture = fixture;
                this.repair = repair;
                fixture.OpenHandles++;
            }
            public bool IsOpen() => fixture.ReadFailure != "open";
            public bool TryGetWhitelist(out List<string> paths)
            { paths = fixture.Applications.ToList(); return fixture.ReadFailure != "applications"; }
            public bool TryGetWhitelistInverseState(out bool inverse)
            { inverse = fixture.Inverse; return fixture.ReadFailure != "inverse"; }
            public bool TryGetActiveState(out bool active)
            { active = fixture.Active; return fixture.ReadFailure != "active"; }
            public bool TryGetBlacklist(out List<string> paths)
            { paths = fixture.Devices.ToList(); return fixture.ReadFailure != "devices"; }
            public bool SetWhitelist(List<string> paths)
            {
                Assert.IsTrue(repair);
                Assert.AreEqual(1, fixture.OpenHandles);
                Assert.AreEqual(1, fixture.Backups);
                fixture.Writes++;
                fixture.Applications.Clear();
                fixture.Applications.AddRange(paths);
                fixture.OnWrite?.Invoke();
                return !fixture.WriteReturnsFalse;
            }
            public void Dispose()
            {
                Assert.IsFalse(disposed);
                disposed = true;
                fixture.OpenHandles--;
            }
        }
    }
}
