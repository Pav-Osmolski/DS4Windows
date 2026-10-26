using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class UpdateStorageTests
{
    private string root, program, updates;
    private UpdateStorage storage;
    [TestInitialize]
    public void Initialize()
    {
        root = Directory.CreateTempSubdirectory("ds4w-update-storage-").FullName;
        program = Directory.CreateDirectory(Path.Combine(root, "Program")).FullName;
        updates = Directory.CreateDirectory(Path.Combine(root, "Local", "Updates")).FullName;
        storage = Create();
    }
    private UpdateStorage Create() => new(program, updates, new Version(5, 0, 50, 0), "0.1.17", "0.9.8.1");
    [TestCleanup]
    public void Cleanup()
    {
        Assert.IsTrue(Path.GetFileName(Path.GetFullPath(root)).StartsWith("ds4w-update-storage-"));
        Directory.Delete(root, true);
    }
    private string Put(string relative)
    {
        string path = Path.Combine(program, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "package bytes"); return path;
    }
    [TestMethod]
    public void KeepsOnePreviousReleasePerComponentAndAllFilesInThatRelease()
    {
        foreach (string name in new[] { "Updates/DS4Windows_5.0.48_x64.zip", "Updates/DS4Windows_5.0.49_x64.zip",
            "Updates/DS4Windows_5.0.49.0_Setup_x64.exe", "Updates/DS4Windows_5.0.50_x64.zip", "Updates/DS4Windows_5.0.51_x64.zip",
            "extras/VIIPER-0.1.14-x64.exe", "extras/VIIPER-0.1.16-x64.exe", "extras/VIIPER-0.1.16-SOURCE.zip",
            "extras/VIIPER-0.1.17-x64.exe", "extras/USBip-0.9.8.0-x64.exe", "extras/USBip-0.9.8.1-x64.exe" }) Put(name);
        var scan = storage.Scan(1);
        CollectionAssert.AreEquivalent(new[] { "DS4Windows_5.0.48_x64.zip", "VIIPER-0.1.14-x64.exe" },
            scan.Removable.Select(file => Path.GetFileName(file.Path)).ToArray());
        Assert.IsTrue(scan.TotalBytes > scan.RecoverableBytes);
    }
    [TestMethod]
    public void ZeroKeepsCurrentAndFutureButRemovesAllPreviousVersions()
    {
        Put("Updates/DS4Windows_5.0.49_x64.zip"); Put("Updates/DS4Windows_5.0.50_x64.zip"); Put("Updates/DS4Windows_5.0.51_x64.zip");
        Assert.AreEqual(1, storage.Scan(0).Removable.Length);
        Assert.AreEqual(0, storage.Scan(-1).Removable.Length);
    }
    [TestMethod]
    public void GroupsNormalizedThreeAndFourPartVersionsTogether()
    {
        Put("Updates/DS4Windows_5.0.49_x64.zip"); Put("Updates/DS4Windows_5.0.49.0_Setup_x64.exe"); Put("Updates/DS4Windows_5.0.48_x64.zip");
        Assert.AreEqual(1, storage.Scan(1).Removable.Length);
    }
    [TestMethod]
    public void RecognizesPreviewCompanionsAndKeepsStableAheadOfPreview()
    {
        foreach (string tail in new[] { "x64.exe", "x64.exe.sha256", "SOURCE.zip", "BUILD-INFO.json", "BUILD-NOTES.txt", "LICENSES.txt", "PROVENANCE.txt" })
            Put("extras/VIIPER-0.1.16-preview20261025-" + tail);
        Put("extras/VIIPER-0.1.16-x64.exe"); Put("extras/VIIPER-0.1.17-x64.exe");
        var scan = storage.Scan(1);
        Assert.AreEqual(7, scan.Removable.Length);
        Assert.IsTrue(scan.Removable.All(file => file.Release == "0.1.16-preview20261025"));
    }
    [TestMethod]
    public void IgnoresUnknownFilesNestedFoldersProfilesAndActiveBinaries()
    {
        foreach (string path in new[] { "DS4Windows.exe", "viiper.exe", "usbip.exe", "DS4Updater.exe",
            "Updates/package.zip", "Updates/custom.zip", "Updates/Assets/DS4Windows_5.0.1_x64.zip",
            "Profiles/DS4Windows_5.0.1_x64.zip", "Backups/DS4Windows_5.0.1_x64.zip", "extras/USBip-0.9.8.0-driver.sys" }) Put(path);
        Assert.AreEqual(0, storage.Scan(0).Files.Length);
    }
    [TestMethod]
    public void OwnershipManifestProtectsEvenOldVersionedPayloads()
    {
        Put("extras/VIIPER-0.1.14-x64.exe");
        File.WriteAllText(Path.Combine(program, ".ds4windows-managed-files.txt"), "extras/VIIPER-0.1.14-x64.exe\n../../never-delete");
        Assert.AreEqual(0, Create().Scan(0).Removable.Length);
    }
    [TestMethod]
    public void CleanupRechecksTheCurrentOwnershipManifest()
    {
        string old = Put("extras/VIIPER-0.1.14-x64.exe");
        var plan = storage.Scan(0);
        File.WriteAllText(Path.Combine(program, ".ds4windows-managed-files.txt"), "extras/VIIPER-0.1.14-x64.exe");
        Assert.AreEqual(0, storage.Clean(plan, 0).Deleted);
        Assert.IsTrue(File.Exists(old));
    }
    [TestMethod]
    public void RemovesOnlyApprovedUnchangedFilesAndReportsBytes()
    {
        string old = Put("Updates/DS4Windows_5.0.48_x64.zip");
        var plan = storage.Scan(0);
        Put("Updates/DS4Windows_5.0.47_x64.zip");
        var result = storage.Clean(plan, 0);
        Assert.AreEqual(1, result.Deleted); Assert.AreEqual(13L, result.Bytes); Assert.IsFalse(File.Exists(old));
        Assert.IsTrue(File.Exists(Path.Combine(program, "Updates/DS4Windows_5.0.47_x64.zip")));
    }
    [TestMethod]
    public void SkipsBusyChangedAndNoLongerEligibleFiles()
    {
        string busy = Put("Updates/DS4Windows_5.0.48_x64.zip"), changed = Put("Updates/DS4Windows_5.0.47_x64.zip");
        var plan = storage.Scan(0);
        File.AppendAllText(changed, " changed");
        using var handle = File.Open(busy, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = storage.Clean(plan, 0);
        Assert.AreEqual(0, result.Deleted); Assert.AreEqual(2, result.Skipped);
        Assert.AreEqual(0, storage.Clean(plan, -1).Deleted);
    }
    [TestMethod]
    public void NeverPrunesUpdaterPackagesWithoutKnownInstalledUpdaterVersion()
    {
        Put("Updates/DS4Updater_2.0.1_x64.zip"); Assert.AreEqual(1, storage.Scan(0).Files.Length);
        Assert.AreEqual(0, storage.Scan(0).Removable.Length);
    }
    [TestMethod]
    public void ScansLocalCacheAndRejectsLinkedDirectories()
    {
        string local = Path.Combine(updates, "DS4Windows_5.0.40_x64.zip"); File.WriteAllText(local, "cache");
        Assert.AreEqual(1, storage.Scan(0).Removable.Length);
        string outside = Directory.CreateDirectory(Path.Combine(root, "Outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "DS4Windows_5.0.1_x64.zip"), "untouched");
        // Directory junctions exercise the same reparse guard without requiring
        // the symbolic-link privilege or changing Developer Mode on the host.
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.Arguments = "/c mklink /J \"" + Path.Combine(program, "Updates") + "\" \"" + outside + "\"";
        using (var process = System.Diagnostics.Process.Start(start))
        { process.WaitForExit(); Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd()); }
        try
        {
            var plan = storage.Scan(0);
            Assert.AreEqual(1, plan.Removable.Length); Assert.AreEqual(1, plan.Warnings.Length);
        }
        finally { Directory.Delete(Path.Combine(program, "Updates")); }
    }
    [TestMethod]
    public void DefaultPreferencesAndUiStateSurviveStorageLocationChoices()
    {
        string appData = Path.Combine(root, "Roaming"), preferences = Path.Combine(root, "Preferences");
        var settings = new SettingsStorage(program, appData, preferences);
        Assert.IsFalse(settings.StorageExpanded); Assert.AreEqual((1, true), settings.UpdateRetention);
        settings.SetStorageExpanded(true); settings.SetUpdateRetention(5, false); settings.Remember(SettingsFolder.Program);
        var again = new SettingsStorage(program, appData, preferences);
        Assert.IsTrue(again.StorageExpanded); Assert.AreEqual((5, false), again.UpdateRetention);
        again.SetStorageExpanded(false); Assert.AreEqual(SettingsFolder.Program, again.Selected);
    }
    [TestMethod]
    public void AutomaticCleanupRequiresOptInPolicyAndHealthyStartup()
    {
        var settings = new SettingsStorage(program, Path.Combine(root, "Roaming"), Path.Combine(root, "Preferences"));
        string old = Put("Updates/DS4Windows_5.0.48_x64.zip");
        Put("Updates/DS4Windows_5.0.49_x64.zip");
        Assert.IsNull(storage.CleanAutomatically(settings, () => false)); Assert.IsTrue(File.Exists(old));
        settings.SetUpdateRetention(1, false);
        Assert.IsNull(storage.CleanAutomatically(settings, () => throw new Exception("Disabled cleanup must not probe.")));
        settings.SetUpdateRetention(-1, true);
        Assert.IsNull(storage.CleanAutomatically(settings, () => throw new Exception("Keep all must not probe.")));
        settings.SetUpdateRetention(1, true);
        Assert.IsNull(storage.CleanAutomatically(settings, () => { settings.SetUpdateRetention(0, false); return true; }));
        Assert.IsTrue(File.Exists(old), "A policy change during the readiness probe must cancel cleanup.");
        settings.SetUpdateRetention(1, true);
        Assert.AreEqual(1, storage.CleanAutomatically(settings, () => true).Deleted);
        Assert.IsFalse(File.Exists(old));
    }
}
