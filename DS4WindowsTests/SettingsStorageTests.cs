using System.IO.Compression;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class SettingsStorageTests
{
    private string root, program, appData, preferences;
    private SettingsStorage storage;

    [TestInitialize]
    public void Initialize()
    {
        root = Directory.CreateTempSubdirectory("ds4w-storage-tests-").FullName;
        program = Path.Combine(root, "Program");
        appData = Path.Combine(root, "AppData");
        preferences = Path.Combine(root, "Preference");
        Directory.CreateDirectory(program);
        Directory.CreateDirectory(appData);
        storage = new(program, appData, preferences);
    }

    [TestCleanup]
    public void Cleanup()
    {
        string resolved = Path.GetFullPath(root);
        Assert.IsTrue(Path.GetFileName(resolved).StartsWith("ds4w-storage-tests-"));
        Directory.Delete(resolved, true);
    }

    private void Put(string directory, string relative, string text = "<DS4Windows><Name>Example</Name></DS4Windows>")
    {
        string path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    [TestMethod]
    public void DisclosurePreferenceKeepsLegacyChoiceWithoutRewritingLocationOrRetention()
    {
        storage.Remember(SettingsFolder.Program);
        storage.SetUpdateRetention(5, false);
        string location = Directory.GetFiles(preferences, "location.json", SearchOption.AllDirectories).Single();
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(location));
        legacy["StorageExpanded"] = true;
        File.WriteAllText(location, legacy.ToJsonString());
        byte[] original = File.ReadAllBytes(location);
        Assert.IsTrue(storage.StorageExpanded, "Existing releases' remembered choices must still load.");
        storage.SetStorageExpanded(false);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(location), "UI-state saves must not race migration or retention preference writes.");
        var again = new SettingsStorage(program, appData, preferences);
        Assert.IsFalse(again.StorageExpanded);
        Assert.AreEqual(SettingsFolder.Program, again.Selected);
        Assert.AreEqual((5, false), again.UpdateRetention);
        again.Queue(SettingsFolder.Program, SettingsFolder.AppData, false, false);
        again.SetStorageExpanded(true);
        Assert.IsTrue(again.HasPending);
        again.CancelPending();
        Assert.IsTrue(again.StorageExpanded, "Migration changes must not overwrite a newer presentation choice.");
    }

    [TestMethod]
    public void MigrationPreservesSourceAndCopiesAllSettingsButNeverApplicationFiles()
    {
        foreach (string name in new[] { "Profiles.xml", "Auto Profiles.xml", "Actions.xml", "LinkedProfiles.xml", "ControllerConfigs.xml", "OutputSlots.xml", "Profiles/Example.xml" }) Put(program, name);
        Put(program, "Nintendo/Pairs.json", "{\"pair\":1}");
        Put(program, "Switch2/Assignments.json", "{\"assignment\":2}");
        Put(program, "BatteryAlertSounds/low.wav", "sound bytes");
        Put(program, "Macros/example.txt", "macro bytes");
        Put(program, "DS4Windows.exe", "not settings");
        Put(program, "Logs/secret.log", "not settings");
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, copy: true, replaceExisting: false);
        Assert.AreEqual(SettingsFolder.Program, storage.Selected);
        Assert.IsFalse(File.Exists(Path.Combine(appData, "Profiles.xml")), "Queueing must not change running stores.");
        string backup = storage.ApplyPending();
        Assert.IsNotNull(backup);
        foreach (string path in Directory.EnumerateFiles(program, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(program, path);
            if (relative.EndsWith(".exe") || relative.StartsWith("Logs")) continue;
            CollectionAssert.AreEqual(File.ReadAllBytes(path), File.ReadAllBytes(Path.Combine(appData, relative)));
        }
        Assert.IsFalse(File.Exists(Path.Combine(appData, "DS4Windows.exe")));
        Assert.IsFalse(Directory.Exists(Path.Combine(appData, "Logs")));
        Assert.AreEqual(SettingsFolder.AppData, storage.Selected);
        Assert.IsFalse(storage.HasPending);
    }

    [TestMethod]
    public void ExistingDestinationRequiresExplicitReplacementAndIsBackedUp()
    {
        Put(program, "Profiles/New.xml");
        Put(appData, "Profiles/Old.xml", "<DS4Windows><Old/></DS4Windows>");
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, false);
        Assert.Throws<IOException>(() => storage.ApplyPending());
        Assert.IsTrue(File.Exists(Path.Combine(appData, "Profiles/Old.xml")));
        storage.CancelPending();
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, true);
        string backup = storage.ApplyPending();
        Assert.AreEqual("<DS4Windows><Old/></DS4Windows>", File.ReadAllText(Path.Combine(backup, "Profiles/Old.xml")));
        Assert.IsFalse(File.Exists(Path.Combine(appData, "Profiles/Old.xml")), "Whole settings sets must not be silently merged.");
        Assert.IsTrue(File.Exists(Path.Combine(appData, "Profiles/New.xml")));
    }

    [TestMethod]
    public void PublicationFailureRollsBackTheEntireDestinationAndKeepsSourceSelected()
    {
        Put(program, "Profiles/New.xml");
        Put(program, "Actions.xml");
        Put(appData, "Profiles/Old.xml", "<DS4Windows><Old/></DS4Windows>");
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, true);
        storage.AfterPublishForTest = _ => throw new IOException("Injected disk failure");
        Assert.Throws<IOException>(() => storage.ApplyPending());
        Assert.IsTrue(File.Exists(Path.Combine(appData, "Profiles/Old.xml")));
        Assert.IsFalse(File.Exists(Path.Combine(appData, "Profiles/New.xml")));
        Assert.IsFalse(File.Exists(Path.Combine(appData, "Actions.xml")));
        Assert.IsTrue(File.Exists(Path.Combine(program, "Profiles/New.xml")));
        Assert.AreEqual(SettingsFolder.Program, storage.Selected);
        Assert.IsFalse(storage.HasPending);
    }

    [TestMethod]
    public void RecoveryRefusesToOverwriteUnexpectedNewData()
    {
        Put(program, "Profiles/New.xml");
        Put(appData, "Profiles/Old.xml");
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, true);
        storage.AfterPublishForTest = _ =>
        {
            Put(appData, "Profiles/Unexpected.xml", "<DS4Windows><KeepMe/></DS4Windows>");
            throw new IOException("Injected competing writer");
        };
        Assert.Throws<IOException>(() => storage.ApplyPending());
        Assert.AreEqual("<DS4Windows><KeepMe/></DS4Windows>", File.ReadAllText(Path.Combine(appData, "Profiles/Unexpected.xml")));
        Assert.AreEqual(SettingsFolder.Program, storage.Selected);
        Assert.IsTrue(storage.HasPending, "Keep the journal for recovery instead of claiming success.");
    }

    [TestMethod]
    public void BackupRestoreRoundTripPreservesSettingsAndBacksUpCurrentDestination()
    {
        Put(program, "Profiles/Example.xml");
        Put(program, "BatteryAlertSounds/sound.mp3", "audio");
        string archive = Path.Combine(root, "backup.zip");
        storage.BackUp(program, archive);
        Put(program, "Profiles/Changed.xml");
        storage.QueueRestore(archive, SettingsFolder.Program);
        Assert.IsTrue(File.Exists(Path.Combine(program, "Profiles/Changed.xml")));
        string backup = storage.ApplyPending();
        Assert.IsTrue(File.Exists(Path.Combine(backup, "Profiles/Changed.xml")));
        Assert.IsFalse(File.Exists(Path.Combine(program, "Profiles/Changed.xml")));
        Assert.IsTrue(File.Exists(Path.Combine(program, "Profiles/Example.xml")));
        Assert.AreEqual("audio", File.ReadAllText(Path.Combine(program, "BatteryAlertSounds/sound.mp3")));
    }

    [TestMethod]
    public void RestoreRejectsTraversalAndUnexpectedFilesWithoutChangingSettings()
    {
        Put(program, "Profiles/Example.xml");
        string archive = Path.Combine(root, "backup.zip");
        storage.BackUp(program, archive);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry("../escaped.xml").Open())) writer.Write("<evil/>");
        Assert.Throws<InvalidDataException>(() => storage.QueueRestore(archive, SettingsFolder.Program));
        Assert.IsFalse(File.Exists(Path.Combine(root, "escaped.xml")));
        Assert.IsTrue(File.Exists(Path.Combine(program, "Profiles/Example.xml")));
        Assert.IsFalse(storage.HasPending);
    }

    [TestMethod]
    public void InvalidXmlOrExternalEntityCannotBeMigrated()
    {
        Put(program, "Profiles/Invalid.xml", "<!DOCTYPE x [<!ENTITY external SYSTEM 'file:///secret'>]><x>&external;</x>");
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, false);
        Assert.Throws<System.Xml.XmlException>(() => storage.ApplyPending());
        Assert.IsFalse(File.Exists(Path.Combine(appData, "Profiles/Invalid.xml")));
        Assert.AreEqual(SettingsFolder.Program, storage.Selected);
    }

    [TestMethod]
    public void ExplicitPreferenceSurvivesBothSettingsFoldersAndCancellation()
    {
        Put(program, "Auto Profiles.xml");
        Put(appData, "Auto Profiles.xml");
        storage.Remember(SettingsFolder.AppData);
        Assert.AreEqual(SettingsFolder.AppData, new SettingsStorage(program, appData, preferences).Selected);
        storage.Queue(SettingsFolder.AppData, SettingsFolder.Program, false, false);
        storage.CancelPending();
        Assert.AreEqual(SettingsFolder.AppData, storage.Selected);
        Assert.IsFalse(storage.HasPending);
        Assert.Throws<InvalidDataException>(() => storage.Root((SettingsFolder)42));
    }

    [TestMethod]
    public void NextStartupRecoversAnUncommittedJournalBeforeAnyNewCopy()
    {
        Put(program, "Profiles/New.xml");
        Put(appData, "Profiles/Old.xml");
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, true);
        storage.AfterPublishForTest = _ =>
        {
            Put(appData, "Profiles/Unexpected.xml");
            throw new IOException("Stop during publication and recovery");
        };
        Assert.Throws<IOException>(() => storage.ApplyPending());
        File.Delete(Path.Combine(appData, "Profiles/Unexpected.xml"));
        var nextStartup = new SettingsStorage(program, appData, preferences);
        Assert.Throws<IOException>(() => nextStartup.ApplyPending());
        Assert.IsTrue(File.Exists(Path.Combine(appData, "Profiles/Old.xml")));
        Assert.IsFalse(File.Exists(Path.Combine(appData, "Profiles/New.xml")));
        Assert.AreEqual(SettingsFolder.Program, nextStartup.Selected);
        Assert.IsFalse(nextStartup.HasPending);
    }

    [TestMethod]
    public void UseExistingChangesOnlyTheRememberedLocation()
    {
        Put(program, "Profiles/Source.xml");
        Put(appData, "Profiles/Destination.xml");
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, false, false);
        storage.ApplyPending();
        Assert.AreEqual(SettingsFolder.AppData, storage.Selected);
        Assert.IsTrue(File.Exists(Path.Combine(program, "Profiles/Source.xml")));
        Assert.IsFalse(File.Exists(Path.Combine(appData, "Profiles/Source.xml")));
        Assert.IsTrue(File.Exists(Path.Combine(appData, "Profiles/Destination.xml")));
    }

    [TestMethod]
    public void ReadOnlyAccessIsDetectedWithoutAssumingAdministratorStatus()
    {
        var directory = new DirectoryInfo(appData);
        var original = System.IO.FileSystemAclExtensions.GetAccessControl(directory);
        var restricted = System.IO.FileSystemAclExtensions.GetAccessControl(directory);
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        restricted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(identity,
            System.Security.AccessControl.FileSystemRights.WriteData, System.Security.AccessControl.AccessControlType.Deny));
        try
        {
            System.IO.FileSystemAclExtensions.SetAccessControl(directory, restricted);
            Assert.IsFalse(SettingsStorage.CanWrite(appData, out string reason));
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason));
            Put(program, "Profiles/Example.xml");
            storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, false);
            Assert.Throws<IOException>(() => storage.ApplyPending());
            Assert.AreEqual(SettingsFolder.Program, storage.Selected);
        }
        finally
        {
            var restore = new System.Security.AccessControl.DirectorySecurity();
            restore.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), System.Security.AccessControl.AccessControlSections.Access);
            System.IO.FileSystemAclExtensions.SetAccessControl(directory, restore);
        }
        Assert.IsTrue(SettingsStorage.CanWrite(appData, out string restoredReason), restoredReason);
    }

    [TestMethod]
    public void ACompetingChangeToAnUnpublishedFileIsNotOverwritten()
    {
        Put(program, "Profiles.xml");
        Put(program, "Actions.xml");
        Put(appData, "Profiles.xml");
        Put(appData, "Actions.xml");
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, true);
        storage.AfterPublishForTest = count =>
        {
            if (count == 1) Put(appData, "Actions.xml", "<DS4Windows><ExternalEdit/></DS4Windows>");
        };
        Assert.Throws<IOException>(() => storage.ApplyPending());
        Assert.AreEqual("<DS4Windows><ExternalEdit/></DS4Windows>", File.ReadAllText(Path.Combine(appData, "Actions.xml")));
        Assert.AreEqual(SettingsFolder.Program, storage.Selected);
    }
}
