using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Serialization;
using DS4WinWPF;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class AutoProfileWorkflowTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public async Task DirectoryDiscoveryAddsOneExeWithoutReadingItsIconAndDeduplicates()
    {
        using var files = new Files();
        for (int i = 0; i < 57; i++) File.WriteAllText(Path.Combine(files.Root, $"file{i}.txt"), "fixture");
        string executable = Path.Combine(files.Root, "game.exe");
        File.WriteAllText(executable, "Not a PE file: discovery must not extract an associated icon.");
        AutoProfilesViewModel vm = null;
        Task<ProgramScanResult> scan = null;
        var clock = Stopwatch.StartNew();
        HeadlessWpfDispatcher.Invoke(() =>
        {
            vm = Model();
            scan = vm.AddProgramsFromDir(files.Root, CancellationToken.None, null);
        });
        ProgramScanResult result = await scan;
        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(1, result.Programs.Count);
        TestContext.WriteLine($"58-file directory, one executable: {clock.ElapsedMilliseconds} ms.");
        HeadlessWpfDispatcher.Invoke(() => scan = vm.AddProgramsFromDir(files.Root, CancellationToken.None, null));
        Assert.AreEqual(0, (await scan).Added);
        HeadlessWpfDispatcher.Invoke(() => Assert.AreEqual(1, vm.ProgramColl.Count));
        string renamed = Path.Combine(files.Root, "renamed.exe");
        File.Move(executable, renamed);
        HeadlessWpfDispatcher.Invoke(() =>
        {
            vm.ProgramColl[0].Path = renamed;
            scan = vm.AddProgramsFromDir(files.Root, CancellationToken.None, null);
        });
        Assert.AreEqual(0, (await scan).Added, "Editing a path must not leave stale duplicate-detection state.");
    }

    [TestMethod]
    public void StartMenuDiscoverySkipsBrokenNonExeAndNetworkShortcutsAndReportsProgress()
    {
        using var files = new Files();
        string exe = Path.Combine(files.Root, "game.exe");
        File.WriteAllText(exe, "fixture");
        foreach (string name in new[] { "a", "b", "broken", "network", "text" })
            File.WriteAllText(Path.Combine(files.Root, name + ".lnk"), "fixture");
        var reports = new List<ProgramScanProgress>();
        var result = ProgramScanner.Scan(new[] { files.Root, files.Root }, true, path => Path.GetFileNameWithoutExtension(path) switch
        {
            "broken" => throw new COMException("Broken shortcut"),
            "network" => @"\\unavailable\share\game.exe",
            "text" => Path.Combine(files.Root, "readme.txt"),
            _ => exe
        }, CancellationToken.None, new InlineProgress(reports.Add));
        Assert.AreEqual(5, result.Checked);
        Assert.AreEqual(3, result.Skipped);
        CollectionAssert.AreEqual(new[] { exe }, result.Programs.ToArray());
        Assert.AreEqual(0, reports[0].Checked);
        Assert.AreEqual(5, reports[^1].Checked);
        Assert.AreEqual(1, reports[^1].Found);
    }

    [TestMethod]
    public void DiscoveryHonoursCancellationWithoutPublishingPartialPrograms()
    {
        using var files = new Files();
        File.WriteAllText(Path.Combine(files.Root, "game.exe"), "fixture");
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsExactly<OperationCanceledException>(() => ProgramScanner.Directory(files.Root, cancellation.Token,
            new InlineProgress(_ => cancellation.Cancel())));
    }

    [TestMethod]
    public void MoveAndDeleteWorkForSavedAndUnsavedRowsAndPersistTheirVisibleOrder()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var vm = Model();
            var z = vm.AddProgramExeLocation(@"C:\Games\zebra.exe"); vm.CreateAutoProfileEntry(z);
            var draft = vm.AddProgramExeLocation(@"C:\Games\draft.exe");
            var a = vm.AddProgramExeLocation(@"C:\Games\alpha.exe"); vm.CreateAutoProfileEntry(a);
            Assert.IsTrue(vm.MoveItemUpDown(a, -1));
            Assert.IsTrue(vm.MoveItemUpDown(a, -1));
            CollectionAssert.AreEqual(new[] { a, z, draft }, vm.ProgramColl.ToArray());
            CollectionAssert.AreEqual(new[] { a.MatchedAutoProfile, z.MatchedAutoProfile }, vm.AutoProfileHolder.AutoProfileColl.ToArray());
            Assert.IsFalse(vm.MoveItemUpDown(a, -1));
            Assert.IsTrue(vm.MoveItemUpDown(draft, -1));
            vm.RemoveAutoProfileEntry(draft);
            Assert.IsFalse(vm.ProgramColl.Contains(draft));
            Assert.AreEqual(2, vm.AutoProfileHolder.AutoProfileColl.Count);
            vm.RemoveAutoProfileEntry(a);
            Assert.IsFalse(vm.ProgramColl.Contains(a));
            Assert.AreEqual(1, vm.AutoProfileHolder.AutoProfileColl.Count);
            var unsaved = vm.AddProgramExeLocation(@"C:\Games\new.exe");
            vm.RemoveUnchecked();
            CollectionAssert.AreEqual(new[] { z }, vm.ProgramColl.ToArray());
        });
    }

    [TestMethod]
    public void DraftEditsDoNotChangeTheRunningRuleUntilSavedAndDisabledRulesNeverMatch()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var vm = Model();
            var item = vm.AddProgramExeLocation(@"C:\Games\old.exe");
            item.Title = "Old title";
            vm.CreateAutoProfileEntry(item);
            var saved = item.MatchedAutoProfile;
            item.Path = @"C:\Games\new.exe"; item.Title = "New title";
            item.Enabled = false; item.Turnoff = true;
            item.DeviceOption = AutoProfileDeviceOption.DualSense; item.ApplyToAllControllers = true;
            Assert.IsTrue(saved.IsMatch(@"C:\Games\old.exe", "Old title"));
            Assert.IsFalse(saved.Turnoff);
            Assert.IsFalse(saved.ApplyToAllControllers);
            Assert.AreEqual(AutoProfileDeviceOption.Any, saved.DeviceOption);
            vm.PersistAutoProfileEntry(item);
            Assert.IsFalse(saved.Enabled);
            Assert.IsTrue(saved.Turnoff);
            Assert.IsTrue(saved.ApplyToAllControllers);
            Assert.AreEqual(AutoProfileDeviceOption.DualSense, saved.DeviceOption);
            Assert.IsFalse(saved.IsMatch(item.Path, item.Title));
            Assert.AreEqual("Disabled", item.StateText);
            item.Enabled = true; vm.PersistAutoProfileEntry(item);
            Assert.IsTrue(saved.IsMatch(item.Path, item.Title));
        });
    }

    [TestMethod]
    public void DuplicateIsAnUnsavedCopyAndDoesNotApplyEditsToItsSource()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var vm = Model();
            var source = vm.AddProgramExeLocation(@"C:\Games\game.exe");
            source.Title = "Saved title";
            vm.CreateAutoProfileEntry(source);
            source.Title = "Edited title"; source.Enabled = false;
            var copy = vm.DuplicateAutoProfileEntry(source);
            Assert.IsNull(copy.MatchedAutoProfile);
            Assert.AreEqual("Not saved", copy.StateText);
            Assert.AreEqual("Edited title", copy.Title);
            Assert.IsFalse(copy.Enabled);
            Assert.AreEqual("Saved title", source.MatchedAutoProfile.Title);
            Assert.IsTrue(source.MatchedAutoProfile.Enabled);
            Assert.AreEqual(1, vm.AutoProfileHolder.AutoProfileColl.Count);
        });
    }

    [TestMethod]
    public void LegacyRulesStayEnabledAndNewDisabledRulesRoundTripAllControllerAssignments()
    {
        var serializer = new XmlSerializer(typeof(AutoProfilesDTO));
        var legacy = (AutoProfilesDTO)serializer.Deserialize(new StringReader("<Programs><Program path='game.exe'><Controller4>Fourth</Controller4><Controller8>Eighth</Controller8></Program></Programs>"));
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var holder = new AutoProfileHolder(false);
            legacy.MapTo(holder);
            var rule = holder.AutoProfileColl.Single();
            Assert.IsTrue(rule.Enabled);
            rule.Enabled = false;
            using var files = new Files();
            string path = Path.Combine(files.Root, "Auto Profiles.xml");
            Assert.IsTrue(holder.Save(path));
            var read = (AutoProfilesDTO)serializer.Deserialize(new StringReader(File.ReadAllText(path)));
            var restored = new AutoProfileHolder(false); read.MapTo(restored);
            var copy = restored.AutoProfileColl.Single();
            Assert.IsFalse(copy.Enabled);
            Assert.AreEqual("Fourth", copy.ProfileNames[3]);
            Assert.AreEqual("Eighth", copy.ProfileNames[7]);
            Assert.AreEqual(1, Directory.GetFiles(files.Root).Length, "Atomic save must clean up its temporary file.");
        });
    }

    internal static AutoProfilesViewModel Model() => new(new AutoProfileHolder(false), new ProfileList());
    private sealed class InlineProgress(Action<ProgramScanProgress> action) : IProgress<ProgramScanProgress>
    { public void Report(ProgramScanProgress value) => action(value); }
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "DS4W-AutoProfile-" + Guid.NewGuid().ToString("N"));
        public Files() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            // Only the unique directory created by this fixture is removed.
            string full = Path.GetFullPath(Root);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(full).StartsWith("DS4W-AutoProfile-", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    }
}
