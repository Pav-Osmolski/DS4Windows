using System.Xml;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class GenericJoystickUpgradeTests
{
    [TestMethod]
    public void ProfilesWithoutJoystickSettingsKeepEveryExistingOutputOnSaveAndReload()
    {
        foreach (var type in Enum.GetValues<OutContType>().Where(t => t != OutContType.ViiperGenericJoystick))
        {
            var dto = new ProfileDTO { DeviceIndex = 0, OutputContDevice = type };
            var store = new BackingStore();
            dto.MapTo(store);
            Assert.AreEqual(type.Normalize(), store.outputDevType[0]);
            Assert.AreEqual(GenericJoystickOptions.Default, store.genericJoystickSettings[0].Options);
            var reloaded = SaveAndReload(store);
            Assert.AreEqual(type.Normalize(), reloaded.outputDevType[0], type.ToString());
            Assert.IsTrue(reloaded.genericJoystickSettings[0].Valid);
        }
    }

    [TestMethod]
    public void DormantJoystickOptionsSurviveSavingAnXboxProfile()
    {
        var store = new BackingStore();
        store.outputDevType[0] = OutContType.ViiperX360;
        store.genericJoystickSettings[0].Options = GenericJoystickOptions.Default with
        {
            Layout = GenericJoystickLayout.Racing,
            TriggerMode = GenericJoystickTriggerMode.FullRange,
            InvertLeftTrigger = true,
            ButtonThreshold = 200
        };
        var reloaded = SaveAndReload(store);
        Assert.AreEqual(OutContType.ViiperX360, reloaded.outputDevType[0]);
        Assert.AreEqual(store.genericJoystickSettings[0].Options, reloaded.genericJoystickSettings[0].Options);
    }

    [TestMethod]
    public void InvalidDormantJoystickSectionDoesNotChangeAnExistingXboxOutput()
    {
        var document = new XmlDocument();
        document.LoadXml("<GenericJoystick Version='999'/>");
        var dto = new ProfileDTO { DeviceIndex = 0, OutputContDevice = OutContType.ViiperX360,
            GenericJoystickXml = document.DocumentElement };
        var store = new BackingStore();
        dto.MapTo(store);
        Assert.IsFalse(store.genericJoystickSettings[0].Valid);
        Assert.AreEqual(OutContType.ViiperX360, store.outputDevType[0]);
        // Existing save policy discards an invalid inactive section. It must
        // not opt into joystick output or treat malformed trigger options as valid.
        var reloaded = SaveAndReload(store);
        Assert.AreEqual(OutContType.ViiperX360, reloaded.outputDevType[0]);
        Assert.IsTrue(reloaded.genericJoystickSettings[0].Valid);
        Assert.AreEqual(GenericJoystickOptions.Default, reloaded.genericJoystickSettings[0].Options);
    }

    [TestMethod]
    public void UnsupportedJoystickSettingsCannotOverwriteAnExistingJoystickProfile()
    {
        var document = new XmlDocument();
        document.LoadXml("<GenericJoystick Version='999'/>");
        var dto = new ProfileDTO { DeviceIndex = 0, OutputContDevice = OutContType.ViiperGenericJoystick,
            GenericJoystickXml = document.DocumentElement };
        var store = new BackingStore();
        dto.MapTo(store);
        Assert.IsFalse(store.genericJoystickSettings[0].Valid);
        string previousRoot = Global.appdatapath;
        string directory = Directory.CreateTempSubdirectory("ds4w-joystick-upgrade-").FullName;
        try
        {
            Global.appdatapath = directory;
            string profiles = Directory.CreateDirectory(Path.Combine(directory, "Profiles")).FullName;
            string path = Path.Combine(profiles, "Candidate.xml");
            const string original = "<DS4Windows><OutputContDevice>ViiperGenericJoystick</OutputContDevice><GenericJoystick Version='999'/></DS4Windows>";
            File.WriteAllText(path, original);
            Assert.IsFalse(store.SaveProfileNew(0, "Candidate"));
            Assert.AreEqual(original, File.ReadAllText(path));
            Assert.AreEqual(OutContType.ViiperGenericJoystick, store.outputDevType[0]);
        }
        finally { Global.appdatapath = previousRoot; Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void SavedProfileKeepsTheSameGameFacingControlsAfterReload()
    {
        var original = new BackingStore();
        original.outputDevType[0] = OutContType.ViiperGenericJoystick;
        original.genericJoystickSettings[0].Options = (GenericJoystickOptions.Default with
        {
            Layout = GenericJoystickLayout.Racing,
            TriggerMode = GenericJoystickTriggerMode.FullRange,
            InvertLeftTrigger = true,
            ButtonMap = 0
        }).WithButtonSource(15, 1);

        var reopened = SaveAndReload(original);
        Assert.AreEqual(OutContType.ViiperGenericJoystick, reopened.outputDevType[0]);
        var pressed = new DS4State
        {
            LX = 255, LY = 128, RX = 0, RY = 128, L2 = 255,
            Cross = true, DpadUp = true, DpadRight = true
        };
        var released = new DS4State { LX = 128, LY = 128, RX = 128, RY = 128 };
        foreach (var store in new[] { original, reopened })
        {
            var options = store.genericJoystickSettings[0].Options;
            Assert.AreEqual(new GenericJoystickFrame(32767, 0, -32767, -32767, 0, -32767, 0x8000, 1),
                Project(pressed, options));
            Assert.AreEqual(new GenericJoystickFrame(0, 0, 32767, 0, 0, -32767, 0, 8),
                Project(released, options));
        }

        static GenericJoystickFrame Project(DS4State state, GenericJoystickOptions options) =>
            GenericJoystickMappedBridge.Build(
                ViiperStatePacketBuilder.BuildMappedState(state, -1, followDigitalTriggers: false), options);
    }

    private static BackingStore SaveAndReload(BackingStore source)
    {
        string previousRoot = Global.appdatapath;
        string directory = Directory.CreateTempSubdirectory("ds4w-joystick-upgrade-").FullName;
        try
        {
            Global.appdatapath = directory;
            Directory.CreateDirectory(Path.Combine(directory, "Profiles"));
            Assert.IsTrue(source.SaveProfileNew(0, "Candidate"));
            using var reader = File.OpenText(Path.Combine(directory, "Profiles", "Candidate.xml"));
            var dto = (ProfileDTO)ProfileDTO.Serializer.Deserialize(reader);
            dto.DeviceIndex = 0;
            var result = new BackingStore();
            dto.MapTo(result);
            return result;
        }
        finally { Global.appdatapath = previousRoot; Directory.Delete(directory, true); }
    }
}
