using System.Text;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms.ViewModels.SpecialActions;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class SpecialActionPersistenceAndGestureTests
{
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(8)]
    public void ProfileXmlRoundTripPreservesPunctuationAndEmptySelections(int device)
    {
        var source = new BackingStore();
        string[] names = { "Play/Pause", "A & B <test> \"quoted\"", "日本語 / é" };
        source.profileActions[device].Clear(); source.profileActions[device].AddRange(names);
        var dto = new ProfileDTO { DeviceIndex = device }; dto.MapFrom(source);
        var serializer = ProfileDTO.Serializer;
        using var xml = new StringWriter(); serializer.Serialize(xml, dto);
        using var input = new StringReader(xml.ToString());
        var reloaded = (ProfileDTO)serializer.Deserialize(input); reloaded.DeviceIndex = device;
        var destination = new BackingStore(); reloaded.MapTo(destination);
        CollectionAssert.AreEqual(names, destination.profileActions[device]);
        reloaded.ProfileActionNames = Array.Empty<string>(); reloaded.ProfileActions = "stale/legacy";
        reloaded.MapTo(destination);
        Assert.AreEqual(0, destination.profileActions[device].Count, "Explicit empty selection must override legacy text.");
    }

    [TestMethod]
    public void LegacyNamesRecoverOnlyOneUnambiguousPartition()
    {
        CollectionAssert.AreEqual(new[] { "Disconnect Controller", "Play/Pause" },
            ProfileActionNames.Read("Disconnect Controller/Play/Pause", null, new[] { "Play/Pause", "Disconnect Controller" }));
        CollectionAssert.AreEqual(new[] { "Play", "Pause" },
            ProfileActionNames.Read("Play/Pause", null, new[] { "Play/Pause", "Play", "Pause" }));
        var serializer = ProfileDTO.Serializer;
        using var input = new StringReader("<DS4Windows><ProfileActions>Play/Pause</ProfileActions></DS4Windows>");
        var dto = (ProfileDTO)serializer.Deserialize(input);
        Assert.IsNull(dto.ProfileActionNames, "Absent structured names must not hide legacy selections.");
        dto.DeviceIndex = 0;
        var store = new BackingStore();
        store.actions.Add(new SpecialAction("Play/Pause", "Share/Cross", "Key", "179"));
        dto.MapTo(store);
        CollectionAssert.AreEqual(new[] { "Play/Pause" }, store.profileActions[0]);
    }

    [TestMethod]
    public void SingleDoubleTripleAndHoldAreExclusive()
    {
        var options = new MultiActionConfiguration("65/65,66/66,67/67,68/68,500,150");
        var state = new MultiActionGesture(); state.Update(false, 0, options);
        Assert.AreEqual(-1, state.Update(true, 10, options));
        Assert.AreEqual(-1, state.Update(false, 50, options));
        Assert.AreEqual(0, state.Update(false, 201, options));
        state.Update(true, 300, options); state.Update(false, 340, options);
        state.Update(true, 400, options); state.Update(false, 440, options);
        Assert.AreEqual(2, state.Update(false, 591, options));
        state.Update(true, 700, options); state.Update(false, 740, options);
        state.Update(true, 800, options); state.Update(false, 840, options);
        state.Update(true, 900, options);
        Assert.AreEqual(3, state.Update(false, 940, options));
        Assert.AreEqual(-1, state.Update(false, 1200, options));
        state.Update(true, 1300, options);
        Assert.AreEqual(1, state.Update(true, 1800, options));
        Assert.AreEqual(-1, state.Update(true, 2000, options));
        Assert.AreEqual(-1, state.Update(false, 2200, options));
        Assert.AreEqual(-1, state.Update(false, 2500, options));
    }

    [TestMethod]
    public void ExistingDoubleTapHasNoExtraTripleWaitAndHoldTimingIsConfigurable()
    {
        var legacy = new MultiActionConfiguration("65/65,66/66,67/67");
        Assert.AreEqual(500, legacy.HoldMilliseconds); Assert.AreEqual(150, legacy.TapMilliseconds);
        var state = new MultiActionGesture(); state.Update(false, 0, legacy);
        state.Update(true, 10, legacy); state.Update(false, 40, legacy);
        Assert.AreEqual(2, state.Update(true, 80, legacy));
        Assert.AreEqual(-1, state.Update(true, 1000, legacy));
        var custom = new MultiActionConfiguration(",66/66,,,1250,250");
        state.Reset(); state.Update(false, 0, custom); state.Update(true, 10, custom);
        Assert.AreEqual(-1, state.Update(true, 1259, custom));
        Assert.AreEqual(1, state.Update(true, 1260, custom));
    }

    [TestMethod]
    public void ResetAndControllerIsolationCannotReplayAnOldGesture()
    {
        var options = new MultiActionConfiguration("65/65,66/66,67/67,68/68");
        var state = new Mapping.ActionState();
        state.MultiGestures[0].Update(false, 0, options); state.MultiGestures[0].Update(true, 10, options);
        state.MultiGestures[0].Update(false, 30, options);
        Assert.AreEqual(-1, state.MultiGestures[1].Update(false, 500, options));
        state.MultiGestures[0].Reset();
        Assert.AreEqual(-1, state.MultiGestures[0].Update(true, 1000, options));
        Assert.AreEqual(-1, state.MultiGestures[0].Update(true, 2000, options));
        Assert.AreEqual(-1, state.MultiGestures[0].Update(false, 2100, options));
    }

    [TestMethod]
    public void ProfileResetRetiresPendingTapsOnTheInputOwnerWithoutMutatingOtherControllers()
    {
        var options = new MultiActionConfiguration("65/65,66/66,67/67,68/68");
        var state = new Mapping.ActionState();
        var first = Mapping.GetMultiActionGesture(state, 0);
        var second = Mapping.GetMultiActionGesture(state, 1);
        first.Update(false, 0, options); first.Update(true, 10, options); first.Update(false, 30, options);
        second.Update(false, 0, options); second.Update(true, 10, options); second.Update(false, 30, options);
        Mapping.ResetMultiActionGestures(0);
        Assert.AreEqual(-1, Mapping.GetMultiActionGesture(state, 0).Update(false, 500, options));
        Assert.AreEqual(0, Mapping.GetMultiActionGesture(state, 1).Update(false, 500, options));
    }

    [TestMethod]
    public void EditorLoadsOldAndExtendedSettingsWithoutAccumulatingMacros()
    {
        var model = new MultiActButtonViewModel();
        var action = new SpecialAction("Triple", "Cross", "MultiAction", "65/65,66/66,67/67,68/68,750,200");
        model.LoadAction(action); model.LoadAction(action);
        CollectionAssert.AreEqual(new[] { 68, 68 }, model.TripleTapMacro);
        Assert.AreEqual(.75, model.HoldSeconds); Assert.AreEqual(.2, model.TapSeconds);
        Assert.IsTrue(model.IsValid(action));
        model.TapSeconds = 1;
        Assert.IsFalse(model.IsValid(action));
        model.LoadAction(new SpecialAction("Old", "Cross", "MultiAction", "65/65,,"));
        Assert.AreEqual(0, model.TripleTapMacro.Count); Assert.AreEqual(.5, model.HoldSeconds);
        Assert.IsTrue(model.IsValid(action), "Individual gestures are optional.");
    }

    [TestMethod]
    public void ImportRenamesCollisionsAndPreservesActionsAcrossAtomicMerge()
    {
        const string xml = "<Actions><Action Name='Play/Pause'><Trigger>Cross</Trigger><Type>MultiAction</Type><Details>65/65,66/66,67/67,68/68,750,200</Details></Action></Actions>";
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var imported = SpecialActionImport.Read(source, new[] { "Play/Pause" });
        Assert.AreEqual("Play/Pause (imported 1)", imported.Single().name);
        string directory = Path.Combine(Path.GetTempPath(), "DS4W-action-import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Actions.xml");
            File.WriteAllText(path, "original");
            SpecialActionImport.SaveMerged(path, new[] { new SpecialAction("Existing", "PS", "DisconnectBT", "0") }, imported);
            Assert.AreEqual("original", File.ReadAllText(Directory.GetFiles(directory, "*.bak").Single()));
            using var file = File.OpenRead(path);
            var roundtrip = SpecialActionImport.Read(file, Array.Empty<string>());
            Assert.AreEqual(2, roundtrip.Count);
            Assert.AreEqual(750, roundtrip[1].MultiActionOptions.HoldMilliseconds);
            Assert.AreEqual("68/68", roundtrip[1].MultiActionOptions.Macros[3]);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("<!DOCTYPE Actions [<!ENTITY x SYSTEM 'file:///C:/missing'>]><Actions>&x;</Actions>")]
    [DataRow("<Actions><Action Name='Invalid'><Trigger>Not a button</Trigger><Type>Macro</Type><Details>65/65</Details></Action></Actions>")]
    [DataRow("<Actions><Action Name='Invalid'><Trigger>Cross</Trigger><Type>Unknown</Type><Details /></Action></Actions>")]
    [DataRow("<Actions><Action Name='Invalid'><Trigger>Cross</Trigger><Type>Key</Type><Details>not-a-key</Details></Action></Actions>")]
    public void ImportRejectsUnsafeOrUnsupportedFiles(string xml)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        Assert.Throws<Exception>(() => { try { SpecialActionImport.Read(input, Array.Empty<string>()); }
            catch (Exception e) { throw new Exception("Rejected", e); } });
    }
}
