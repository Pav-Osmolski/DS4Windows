using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.ViewModels;
using DS4WinWPF.DS4Control.DTOXml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ProfileEditorSaveTests
{
    private string directory, previousRoot, previousProfile, previousOlderProfile;
    private byte previousRumble;
    private List<string> previousActions;
    private const string Original = "<DS4Windows><RumbleBoost>77</RumbleBoost></DS4Windows>";

    [TestInitialize]
    public void Initialize()
    {
        directory = Directory.CreateTempSubdirectory("ds4w-editor-save-").FullName;
        Directory.CreateDirectory(Path.Combine(directory, "Profiles"));
        previousRoot = Global.appdatapath;
        previousProfile = Global.ProfilePath[0];
        previousOlderProfile = Global.OlderProfilePath[0];
        previousRumble = Global.store.rumble[0];
        previousActions = Global.ProfileActions[0];
        Global.ProfileActions[0] = new List<string>();
        Global.appdatapath = directory;
        Global.ProfilePath[0] = Global.OlderProfilePath[0] = "Original";
        Global.store.rumble[0] = 88;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Global.appdatapath = previousRoot;
        Global.ProfilePath[0] = previousProfile;
        Global.OlderProfilePath[0] = previousOlderProfile;
        Global.store.rumble[0] = previousRumble;
        Global.ProfileActions[0] = previousActions;
        Global.CacheExtraProfileInfo(0);
        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    public void NewProfileXmlSuffixPublishesTheActualStoredName()
    {
        var editor = CreateEditor(null);
        string publishedName = null;
        editor.CreatedProfile += (_, name) => publishedName = name;
        Assert.IsTrue(editor.SaveProfileChanges("Candidate.xml"));
        Assert.IsTrue(File.Exists(ProfilePath("Candidate")));
        Assert.AreEqual("Candidate", publishedName, "Created event must identify the stored profile basename.");
        Assert.AreEqual("Candidate", Global.ProfilePath[0]);
    }

    [TestMethod]
    public void RenamedProfileXmlSuffixKeepsEntityAndSelectionConsistent()
    {
        WriteProfile("Original", Original);
        var entity = new ProfileEntity { Name = "Original" };
        Assert.IsTrue(CreateEditor(entity).SaveProfileChanges("Renamed.xml"));
        Assert.IsTrue(File.Exists(ProfilePath("Renamed")));
        Assert.AreEqual("Renamed", entity.Name, "Entity must identify the stored profile basename.");
        Assert.AreEqual("Renamed", Global.ProfilePath[0]);
    }

    [TestMethod]
    public void LockedSaveReportsFailureWithoutSavedEventsAndCanBeRetried()
    {
        string original = WriteProfile("Original", Original);
        var entity = new ProfileEntity { Name = "Original" };
        var editor = CreateEditor(entity);
        int saved = 0;
        entity.ProfileSaved += (_, _) => saved++;
        using (var held = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.IsFalse(editor.SaveProfileChanges("Original"));
            Assert.AreEqual(0, saved);
            Assert.AreEqual(Original, File.ReadAllText(original));
        }
        Assert.IsTrue(editor.SaveProfileChanges("Original"));
        Assert.AreEqual(1, saved);
        AssertSavedXml(original);
    }

    [TestMethod]
    public void ReadOnlySaveReportsFailureWithoutAnnouncingSuccess()
    {
        string original = WriteProfile("Original", Original);
        File.SetAttributes(original, FileAttributes.ReadOnly);
        var entity = new ProfileEntity { Name = "Original" };
        int saved = 0;
        entity.ProfileSaved += (_, _) => saved++;
        Assert.IsFalse(CreateEditor(entity).SaveProfileChanges("Original"));
        Assert.AreEqual(0, saved);
        Assert.AreEqual(Original, File.ReadAllText(original));
    }

    [TestMethod]
    public void RenameCollisionPreservesBothFilesNameSelectionAndEvents()
    {
        string original = WriteProfile("Original", Original);
        string collision = WriteProfile("Other", "other profile");
        var entity = new ProfileEntity { Name = "Original" };
        int events = 0;
        entity.NameChanged += (_, _) => events++;
        entity.ProfileSaved += (_, _) => events++;
        entity.ProfileDeleted += (_, _) => events++;
        Assert.IsFalse(CreateEditor(entity).SaveProfileChanges("Other"));
        Assert.AreEqual("Original", entity.Name);
        Assert.AreEqual("Original", Global.ProfilePath[0]);
        Assert.AreEqual("Original", Global.OlderProfilePath[0]);
        Assert.AreEqual(0, events);
        Assert.AreEqual(Original, File.ReadAllText(original));
        Assert.AreEqual("other profile", File.ReadAllText(collision));
    }

    [TestMethod]
    public void SuccessfulRenameUpdatesSelectionAndEventsAfterSavingWithoutDeletionEvent()
    {
        string original = WriteProfile("Original", Original);
        string target = ProfilePath("Renamed");
        var entity = new ProfileEntity { Name = "Original" };
        int renamed = 0, saved = 0, deleted = 0;
        entity.NameChanged += (_, _) => { renamed++; AssertSavedXml(target); };
        entity.ProfileSaved += (_, _) =>
        {
            saved++;
            Assert.AreEqual("Renamed", Global.ProfilePath[0]);
            AssertSavedXml(target);
        };
        entity.ProfileDeleted += (_, _) => deleted++;
        Assert.IsTrue(CreateEditor(entity).SaveProfileChanges("Renamed"));
        Assert.IsFalse(File.Exists(original));
        Assert.AreEqual("Renamed", entity.Name);
        Assert.AreEqual("Renamed", Global.OlderProfilePath[0]);
        Assert.AreEqual(1, renamed);
        Assert.AreEqual(1, saved);
        Assert.AreEqual(0, deleted);
    }

    [TestMethod]
    public void FailedOriginalRemovalKeepsOldSelectionAndReportsFailureWithBothCopiesPreserved()
    {
        string original = WriteProfile("Original", Original);
        var entity = new ProfileEntity { Name = "Original" };
        int events = 0;
        entity.NameChanged += (_, _) => events++;
        entity.ProfileSaved += (_, _) => events++;
        using var held = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.IsFalse(CreateEditor(entity).SaveProfileChanges("Renamed"));
        Assert.AreEqual("Original", entity.Name);
        Assert.AreEqual("Original", Global.ProfilePath[0]);
        Assert.AreEqual("Original", Global.OlderProfilePath[0]);
        Assert.AreEqual(0, events);
        Assert.AreEqual(Original, File.ReadAllText(original));
        AssertSavedXml(ProfilePath("Renamed"));
    }

    [TestMethod]
    public void NewProfileCollisionDoesNotOverwriteOrPublishCreatedEvent()
    {
        string target = WriteProfile("Other", Original);
        var editor = CreateEditor(null);
        int created = 0;
        editor.CreatedProfile += (_, _) => created++;
        Assert.IsFalse(editor.SaveProfileChanges("Other"));
        Assert.AreEqual(0, created);
        Assert.AreEqual("Original", Global.ProfilePath[0]);
        Assert.AreEqual(Original, File.ReadAllText(target));
    }

    [TestMethod]
    public void FailedNewProfileCanBeRetriedAndPublishesOnlyAfterSuccessfulWrite()
    {
        var editor = CreateEditor(null);
        int created = 0;
        editor.CreatedProfile += (_, name) => { created++; AssertSavedXml(ProfilePath(name)); };
        string profiles = Path.Combine(directory, "Profiles");
        Directory.Move(profiles, profiles + "-unavailable");
        Assert.IsFalse(editor.SaveProfileChanges("New"));
        Assert.AreEqual(0, created);
        Assert.AreEqual("Original", Global.ProfilePath[0]);
        Directory.Move(profiles + "-unavailable", profiles);
        Assert.IsTrue(editor.SaveProfileChanges("New"));
        Assert.AreEqual(1, created);
        Assert.AreEqual("New", Global.ProfilePath[0]);
        Assert.AreEqual("New", Global.OlderProfilePath[0]);
    }

    [TestMethod]
    public void ExistingBasenameEndingInXmlIsNotRedirectedToAnotherProfile()
    {
        string exact = WriteProfile("Existing.xml", Original);
        string other = WriteProfile("Existing", "other profile");
        var entity = new ProfileEntity { Name = "Existing.xml" };
        Assert.IsTrue(CreateEditor(entity).SaveProfileChanges("Existing.xml"));
        AssertSavedXml(exact);
        Assert.AreEqual("other profile", File.ReadAllText(other));
        Assert.AreEqual("Existing.xml", Global.ProfilePath[0]);
    }

    [TestMethod]
    public void ExistingBasenameWithTrailingDotRemainsEditable()
    {
        string path = WriteProfile("Legacy.", Original);
        Assert.IsTrue(CreateEditor(new ProfileEntity { Name = "Legacy." }).SaveProfileChanges("Legacy."));
        AssertSavedXml(path);
        Assert.AreEqual("Legacy.", Global.ProfilePath[0]);
    }

    [TestMethod]
    [DataRow("Candidate.XML", "Candidate")]
    [DataRow("Dotted.name", "Dotted.name")]
    public void UserEnteredSuffixIsNormalizedWithoutRemovingOtherDots(string input, string expected)
    {
        Assert.IsTrue(CreateEditor(null).SaveProfileChanges(input));
        AssertSavedXml(ProfilePath(expected));
        Assert.AreEqual(expected, Global.ProfilePath[0]);
    }

    [TestMethod]
    [DataRow(".xml")]
    [DataRow(" ")]
    [DataRow("Invalid/Name")]
    public void InvalidNameDoesNotPublishOrWrite(string input)
    {
        Assert.IsFalse(CreateEditor(null).SaveProfileChanges(input));
        Assert.AreEqual("Original", Global.ProfilePath[0]);
        Assert.AreEqual(0, Directory.GetFiles(Path.Combine(directory, "Profiles")).Length);
    }

    [TestMethod]
    [DataRow("DarkTheme", false)]
    [DataRow("DarkTheme", true)]
    [DataRow("DefaultTheme", false)]
    [DataRow("DefaultTheme", true)]
    public void CheckingExistingActionsInAnotherProfilePersistsTheVisibleSelection(string theme, bool existing)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var vm = new SpecialActionsListViewModel(0);
            foreach (string name in new[] { "test1", "test2", "test3" })
                vm.ActionCol.Add(vm.CreateActionItem(new SpecialAction(name, "Cross", "Macro", "65/65")));
            if (existing) WriteProfile("Second", Original);
            var editor = CreateEditor(existing ? new ProfileEntity { Name = "Second" } : null);
            typeof(ProfileEditor).GetField("specialActionsVM", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(editor, vm);
            var root = ProfileAdvancedLayoutTests.Load(theme, "Special Actions");
            var table = (DataGrid)root.FindName("specialActionsLV");
            table.ItemsSource = vm.ActionCol;
            root.Measure(new Size(1000, 700));
            root.Arrange(new Rect(0, 0, 1000, 700));
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            root.UpdateLayout();
            var checkboxes = Visuals(table).OfType<CheckBox>().ToArray();
            Assert.AreEqual(3, checkboxes.Length);
            foreach (var checkbox in checkboxes)
                checkbox.Click += (_, args) => typeof(ProfileEditor)
                    .GetMethod("SpecialActionCheckBox_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(editor, new object[] { checkbox, args });
            foreach (var checkbox in checkboxes.Take(2))
                typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(checkbox, null);
            Assert.IsTrue(checkboxes[0].IsChecked);
            Assert.IsTrue(checkboxes[1].IsChecked);
            Assert.IsTrue(vm.ActionCol[0].Active, "Checkbox edits must reach the model before the Click handler exports them.");
            CollectionAssert.AreEqual(new[] { "test1", "test2" }, Global.ProfileActions[0]);
            Assert.AreEqual(2, Global.getProfileActionCount(0));
            Assert.IsTrue(editor.SaveProfileChanges("Second"));
            Assert.AreEqual("test1/test2", XDocument.Load(ProfilePath("Second")).Root.Element("ProfileActions").Value);
            CollectionAssert.AreEqual(new[] { "test1", "test2" }, ReloadActions("Second"));

            // Deselecting every action must also survive saving an existing profile.
            foreach (var checkbox in checkboxes.Take(2))
                typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(checkbox, null);
            typeof(ProfileEditor).GetField("currentProfile", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(editor, new ProfileEntity { Name = "Second" });
            Assert.IsTrue(editor.SaveProfileChanges("Second"));
            Assert.AreEqual("", XDocument.Load(ProfilePath("Second")).Root.Element("ProfileActions").Value);
            Assert.AreEqual(0, ReloadActions("Second").Count);
            Assert.AreEqual(0, Global.getProfileActionCount(0));
        });
    }

    [TestMethod]
    public void SaveCapturesActionChangesWithoutDependingOnACheckboxClick()
    {
        var vm = new SpecialActionsListViewModel(0);
        var item = vm.CreateActionItem(new SpecialAction("Renamed action", "Cross", "Macro", "65/65"));
        item.Active = true;
        vm.ActionCol.Add(item);
        var editor = CreateEditor(null);
        typeof(ProfileEditor).GetField("specialActionsVM", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(editor, vm);
        Assert.AreEqual(0, Global.ProfileActions[0].Count);
        Assert.IsTrue(editor.SaveProfileChanges("Updated"));
        CollectionAssert.AreEqual(new[] { "Renamed action" }, ReloadActions("Updated"));
    }

    private List<string> ReloadActions(string profileName)
    {
        using var reader = File.OpenText(ProfilePath(profileName));
        var dto = (ProfileDTO)ProfileDTO.Serializer.Deserialize(reader);
        dto.DeviceIndex = 0;
        var store = new BackingStore();
        store.profileActions[0].Clear();
        dto.MapTo(store);
        return store.profileActions[0];
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private string ProfilePath(string name) => Path.Combine(directory, "Profiles", name + ".xml");
    private string WriteProfile(string name, string xml)
    {
        string path = ProfilePath(name);
        File.WriteAllText(path, xml);
        return path;
    }
    private static void AssertSavedXml(string path) =>
        Assert.AreEqual("88", XDocument.Load(path).Root.Element("RumbleBoost").Value);

    private static ProfileEditor CreateEditor(ProfileEntity entity)
    {
        // Exercise the editor's real persistence/event path without its UI
        // constructor, controller service, device discovery or preview output.
        var editor = (ProfileEditor)RuntimeHelpers.GetUninitializedObject(typeof(ProfileEditor));
        typeof(ProfileEditor).GetField("currentProfile", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(editor, entity);
        var actions = new SpecialActionsListViewModel(0);
        actions.LoadActions();
        typeof(ProfileEditor).GetField("specialActionsVM", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(editor, actions);
        return editor;
    }
}
