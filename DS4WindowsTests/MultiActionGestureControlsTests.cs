using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels.SpecialActions;

namespace DS4WindowsTests;

[TestClass]
public class MultiActionGestureControlsTests
{
    private static readonly (bool Pressed, long Time)[] Triple = {
        (false,0), (true,10), (false,50), (true,90), (false,130), (true,170), (false,210), (false,400)
    };

    [TestMethod]
    public void SeparateActionsWithMatchingChordAndTimingEmitOnlyTriple()
    {
        var configs = new[] {
            new MultiActionConfiguration("1,,,,500,150", "Cross/Circle"),
            new MultiActionConfiguration(",,2,,500,150", "Circle/Cross"),
            new MultiActionConfiguration(",,,3,500,150", "Cross/Circle")
        };
        CollectionAssert.AreEqual(new[] { "3" }, Simulate(configs, Triple));
        CollectionAssert.AreEqual(new[] { "2" }, Simulate(configs, Triple.Take(5).Append((false, 400L))));
        CollectionAssert.AreEqual(new[] { "1" }, Simulate(configs, Triple.Take(3).Append((false, 400L))));
    }

    [TestMethod]
    public void DifferentChordsOrTimingDoNotSuppressIndependentActions()
    {
        var configs = new[] {
            new MultiActionConfiguration("1,,,,500,150", "Cross"),
            new MultiActionConfiguration(",,2,,500,150", "Cross"),
            new MultiActionConfiguration(",,,3,500,150", "Circle")
        };
        CollectionAssert.AreEqual(new[] { "2", "3", "1" }, Simulate(configs, Triple));
        Assert.AreNotEqual(configs[0].GroupKey, new MultiActionConfiguration(",,,3,500,200", "Cross").GroupKey);
    }

    [TestMethod]
    public void DisabledMacrosAndNamesSurviveEditorAndConfigurationRoundTrip()
    {
        var model = new MultiActButtonViewModel();
        model.LoadAction(new SpecialAction("Test", "Cross", "MultiAction", "49/49,52/52,50/50,51/51,750,200"));
        model.TapName = "One, single / один 🎮";
        model.HoldName = "Hold"; model.DoubleTapName = "Double"; model.TripleTapName = "Triple";
        model.TapEnabled = false; model.TripleTapEnabled = false;
        string details = model.SerializeDetails();
        // Older clients see empty disabled slots, never execute a retained recording.
        Assert.AreEqual("", details.Split(',')[0]); Assert.AreEqual("", details.Split(',')[3]);
        var action = new SpecialAction("Reload", "Cross", "MultiAction", details);
        var loaded = new MultiActButtonViewModel(); loaded.LoadAction(action);
        Assert.IsFalse(loaded.TapEnabled); Assert.IsFalse(loaded.TripleTapEnabled);
        Assert.IsTrue(loaded.HoldEnabled); Assert.IsTrue(loaded.DoubleTapEnabled);
        Assert.AreEqual(model.TapName, loaded.TapName);
        CollectionAssert.AreEqual(model.TripleTapMacro, loaded.TripleTapMacro);
        Assert.AreEqual(750, action.MultiActionOptions.HoldMilliseconds);
        Assert.IsFalse(action.MultiActionOptions.HasTriple);
        loaded.TripleTapEnabled = true;
        var restored = new MultiActionConfiguration(loaded.SerializeDetails());
        Assert.IsTrue(restored.HasTriple); Assert.AreEqual("51/51", restored.Macros[3]);
        Assert.AreEqual("Triple", restored.Names[3]);
        string folder = Directory.CreateTempSubdirectory("ds4w-gesture-import-").FullName;
        try
        {
            string file = Path.Combine(folder, "Actions.xml");
            SpecialActionImport.SaveMerged(file, new[] { action }, Array.Empty<SpecialAction>());
            using var input = File.OpenRead(file);
            var imported = SpecialActionImport.Read(input, Array.Empty<string>()).Single().MultiActionOptions;
            Assert.IsFalse(imported.Enabled[0]); Assert.AreEqual(model.TapName, imported.Names[0]);
            Assert.AreEqual("51/51", imported.Macros[3]); Assert.IsFalse(imported.Enabled[3]);
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void LegacyAndMalformedMetadataRemainSafeAndAllGesturesMayBeDisabled()
    {
        var legacy = new MultiActionConfiguration("49/49,,50/50");
        Assert.IsTrue(legacy.Enabled.All(v => v)); Assert.IsFalse(legacy.HasTriple);
        var disabled = new MultiActionConfiguration(MultiActionConfiguration.Serialize(
            new[] { "1", "4", "2", "3" }, new bool[4], new string[4], 500, 150));
        Assert.IsFalse(disabled.HasEnabledMacro);
        var invalid = new MultiActionConfiguration("1,,2,,500,150,v2:invalid,,,,,,,,");
        Assert.AreEqual("1", invalid.Macros[0]); Assert.IsFalse(invalid.HasTriple);
    }

    private static string[] Simulate(MultiActionConfiguration[] configs, IEnumerable<(bool Pressed, long Time)> sequence)
    {
        var groups = configs.Where(c => c.HasTriple).Select(c => c.GroupKey).ToHashSet(StringComparer.Ordinal);
        var states = configs.Select(_ => new MultiActionGesture()).ToArray();
        var emitted = new List<string>();
        foreach (var frame in sequence)
            for (int i = 0; i < configs.Length; i++)
            {
                int gesture = states[i].Update(frame.Pressed, frame.Time, configs[i], reserveTriple: groups.Contains(configs[i].GroupKey));
                if (gesture >= 0 && configs[i].IsEnabled(gesture)) emitted.Add(configs[i].Macros[gesture]);
            }
        return emitted.ToArray();
    }
}
