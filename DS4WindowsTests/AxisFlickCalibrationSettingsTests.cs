using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.ViewModels;
using RuntimeFlickSettings = DS4Windows.FlickStickSettings;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class AxisFlickCalibrationSettingsTests
{
    private const int Slot = Global.TEST_PROFILE_INDEX;

    [TestMethod]
    public void NewAndResetStickSettingsLeaveCalibrationUnassigned()
    {
        var store = new BackingStore();
        Assert.AreEqual(DS4Controls.None, Left(store).calibrationTrigger);
        Assert.AreEqual(DS4Controls.None, Right(store).calibrationTrigger);
        Left(store).calibrationTrigger = DS4Controls.FnL;
        Right(store).calibrationTrigger = DS4Controls.Switch2C;
        store.lsOutputSettings[0].outputSettings.flickSettings.calibrationTrigger = DS4Controls.BLP;

        Left(store).Reset();
        Assert.AreEqual(DS4Controls.None, Left(store).calibrationTrigger);
        Assert.AreEqual(DS4Controls.Switch2C, Right(store).calibrationTrigger,
            "Resetting one stick must not clear the other stick's assignment.");

        typeof(BackingStore).GetMethod("ResetProfile", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(store, new object[] { Slot });
        Assert.AreEqual(DS4Controls.None, Left(store).calibrationTrigger);
        Assert.AreEqual(DS4Controls.None, Right(store).calibrationTrigger);
        Assert.AreEqual(DS4Controls.BLP,
            store.lsOutputSettings[0].outputSettings.flickSettings.calibrationTrigger);
    }

    [TestMethod]
    [DataRow(DS4Controls.None, DS4Controls.Cross)]
    [DataRow(DS4Controls.FnL, DS4Controls.FnR)]
    [DataRow(DS4Controls.BLP, DS4Controls.BRP)]
    [DataRow(DS4Controls.Switch2C, DS4Controls.Capture)]
    [DataRow(DS4Controls.Switch2JoyConLeftSL, DS4Controls.Switch2JoyConRightSR)]
    [DataRow(DS4Controls.L2FullPull, DS4Controls.TouchRight)]
    public void DtoRoundTripsIndependentCanonicalSourceControls(DS4Controls left, DS4Controls right)
    {
        var source = CreateConfiguredProfile(left, right);
        var serializer = Serializer();
        var dto = new ProfileDTO { DeviceIndex = Slot, SerializeAppAttrs = false };
        dto.MapFrom(source);
        using var writer = new StringWriter();
        serializer.Serialize(writer, dto);
        var xml = XDocument.Parse(writer.ToString());
        Assert.AreEqual(left.ToString(), TriggerElement(xml, "LS").Value);
        Assert.AreEqual(right.ToString(), TriggerElement(xml, "RS").Value);

        using var reader = new StringReader(writer.ToString());
        var restored = (ProfileDTO)serializer.Deserialize(reader);
        restored.DeviceIndex = Slot;
        var destination = new BackingStore();
        restored.MapTo(destination);
        AssertConfiguredProfile(destination, left, right);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("FutureControllerButton")]
    [DataRow("255")]
    [DataRow("18")]
    [DataRow("Cross, Circle")]
    [DataRow("cross")]
    public void MissingOrInvalidDtoTriggerClearsAnEarlierAssignment(string value)
    {
        var element = new XElement("FlickStickSettings",
            new XElement("RealWorldCalibration", "17.5"));
        if (value != null)
            element.Add(new XElement("CalibrationTrigger", value));
        var xml = new XDocument(new XElement("DS4Windows",
            new XAttribute("config_version", Global.CONFIG_VERSION),
            new XElement("LSOutputSettings", element)));
        using var reader = xml.CreateReader();
        var dto = (ProfileDTO)Serializer().Deserialize(reader);
        dto.DeviceIndex = Slot;
        var destination = CreateConfiguredProfile(DS4Controls.FnL, DS4Controls.FnR);
        dto.MapTo(destination);
        Assert.AreEqual(DS4Controls.None, Left(destination).calibrationTrigger);
        Assert.AreEqual(DS4Controls.None, Right(destination).calibrationTrigger,
            "A missing stick group must also clear a previous profile's trigger.");
        Assert.AreEqual(17.5, Left(destination).realWorldCalibration);
        Assert.AreEqual(DS4Controls.None, RuntimeFlickSettings.ParseCalibrationTrigger(value));
    }

    [TestMethod]
    public void ProfileWithoutEitherStickGroupLeavesBothTestsUnassigned()
    {
        using var reader = new StringReader("<DS4Windows config_version=\"5\" />");
        var dto = (ProfileDTO)Serializer().Deserialize(reader);
        dto.DeviceIndex = Slot;
        var destination = CreateConfiguredProfile(DS4Controls.FnL, DS4Controls.FnR);
        dto.MapTo(destination);
        Assert.AreEqual(DS4Controls.None, Left(destination).calibrationTrigger);
        Assert.AreEqual(DS4Controls.None, Right(destination).calibrationTrigger);
    }

    [TestMethod]
    [DataRow(false, "valid")]
    [DataRow(true, "valid")]
    [DataRow(false, "missing")]
    [DataRow(true, "missing")]
    [DataRow(false, "invalid")]
    [DataRow(true, "invalid")]
    public void BothProfileFilePathsSaveAndLoadCalibrationSafely(bool legacy, string variant)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ds4w-axis-calibration-{Guid.NewGuid():N}");
        string previousRoot = Global.appdatapath;
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "Profiles"));
            Global.appdatapath = directory;
            var source = CreateConfiguredProfile(DS4Controls.FnL, DS4Controls.Switch2C);
            Assert.IsTrue(legacy ? source.SaveProfileOld(Slot, "Calibration") :
                source.SaveProfileNew(Slot, "Calibration"));
            string path = Path.Combine(directory, "Profiles", "Calibration.xml");
            var xml = XDocument.Load(path);
            Assert.AreEqual("FnL", TriggerElement(xml, "LS").Value);
            Assert.AreEqual("Switch2C", TriggerElement(xml, "RS").Value);
            if (variant == "missing")
            {
                TriggerElement(xml, "LS").Remove();
                TriggerElement(xml, "RS").Remove();
            }
            else if (variant == "invalid")
            {
                TriggerElement(xml, "LS").Value = "UnknownButton";
                TriggerElement(xml, "RS").Value = "18";
            }
            xml.Save(path);

            var destination = CreateConfiguredProfile(DS4Controls.BLP, DS4Controls.BRP);
            // The test profile skips controller and mouse lifecycle operations.
            // No service constructor, output device, or input driver is used.
            var service = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
            Assert.IsTrue(legacy
                ? destination.LoadProfile(Slot, false, service, path, xinputChange: false, postLoad: false)
                : destination.LoadProfileNew(Slot, false, service, path, xinputChange: false, postLoad: false));
            AssertConfiguredProfile(destination,
                variant == "valid" ? DS4Controls.FnL : DS4Controls.None,
                variant == "valid" ? DS4Controls.Switch2C : DS4Controls.None);
        }
        finally
        {
            Global.appdatapath = previousRoot;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void AxisSelectorsUseTypedUniversalButtonChoicesAndKeepTheSticksIndependent()
    {
        // These selector properties do not need WPF, endpoint enumeration, or
        // the unrelated event subscriptions performed by the full constructor.
        var vm = (ProfileSettingsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ProfileSettingsViewModel));
        typeof(ProfileSettingsViewModel).GetField("device", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(vm, Slot);
        var choices = vm.FlickCalibrationTriggerChoices;
        Assert.AreEqual(DS4Controls.None, choices[0].Control);
        Assert.AreEqual("Not assigned", choices[0].Label);
        Assert.AreEqual(choices.Count, choices.Select(item => item.Control).Distinct().Count());
        foreach (var entry in SpecialActionTriggerCatalog.Entries.Where(entry =>
            entry.Group != "Stick directions" && entry.Group != "Swipe and tilt"))
            Assert.AreEqual(entry.Label, choices.Single(item => item.Control == entry.Control).Label);
        foreach (DS4Controls extra in Enum.GetValues<DS4Controls>().Where(control => control >= DS4Controls.Switch2C))
            Assert.IsTrue(choices.Any(item => item.Control == extra), extra.ToString());
        Assert.IsFalse(choices.Any(item => item.Control == DS4Controls.LXNeg ||
            item.Control == DS4Controls.GyroXNeg));

        DS4Controls oldLeft = vm.LSFlickCalibrationTrigger;
        DS4Controls oldRight = vm.RSFlickCalibrationTrigger;
        double oldCalibration = vm.LSFlickRWC;
        try
        {
            vm.LSFlickCalibrationTrigger = DS4Controls.FnL;
            vm.RSFlickCalibrationTrigger = DS4Controls.Switch2C;
            Assert.AreEqual(DS4Controls.FnL, vm.LSFlickCalibrationTrigger);
            Assert.AreEqual(DS4Controls.Switch2C, vm.RSFlickCalibrationTrigger);
            vm.LSFlickRWC = 27;
            Assert.AreEqual(DS4Controls.FnL, vm.LSFlickCalibrationTrigger);
            vm.LSFlickCalibrationTrigger = DS4Controls.None;
            Assert.AreEqual(DS4Controls.None,
                Global.LSOutputSettings[Slot].outputSettings.flickSettings.calibrationTrigger);
            Assert.AreEqual(DS4Controls.Switch2C,
                Global.RSOutputSettings[Slot].outputSettings.flickSettings.calibrationTrigger);
            Assert.AreEqual(27.0, vm.LSFlickRWC);
        }
        finally
        {
            vm.LSFlickCalibrationTrigger = oldLeft;
            vm.RSFlickCalibrationTrigger = oldRight;
            vm.LSFlickRWC = oldCalibration;
        }
    }

    private static BackingStore CreateConfiguredProfile(DS4Controls left, DS4Controls right)
    {
        var store = new BackingStore();
        store.lsOutputSettings[Slot].mode = StickMode.FlickStick;
        store.rsOutputSettings[Slot].mode = StickMode.FlickStick;
        Left(store).calibrationTrigger = left;
        Right(store).calibrationTrigger = right;
        Left(store).realWorldCalibration = 17.5;
        Right(store).realWorldCalibration = 28.75;
        Left(store).flickThreshold = 0.8;
        Right(store).flickTime = 0.2;
        store.ds4settings[Slot][(int)DS4Controls.Cross - 1].UpdateSettings(
            false, X360Controls.A, string.Empty, DS4KeyType.None);
        return store;
    }

    private static void AssertConfiguredProfile(BackingStore store, DS4Controls left, DS4Controls right)
    {
        Assert.AreEqual(left, Left(store).calibrationTrigger);
        Assert.AreEqual(right, Right(store).calibrationTrigger);
        Assert.AreEqual(17.5, Left(store).realWorldCalibration);
        Assert.AreEqual(28.75, Right(store).realWorldCalibration);
        Assert.AreEqual(0.8, Left(store).flickThreshold);
        Assert.AreEqual(0.2, Right(store).flickTime);
        Assert.AreEqual(StickMode.FlickStick, store.lsOutputSettings[Slot].mode);
        Assert.AreEqual(StickMode.FlickStick, store.rsOutputSettings[Slot].mode);
        Assert.AreEqual(X360Controls.A,
            store.ds4settings[Slot][(int)DS4Controls.Cross - 1].action.actionBtn);
    }

    private static RuntimeFlickSettings Left(BackingStore store) =>
        store.lsOutputSettings[Slot].outputSettings.flickSettings;

    private static RuntimeFlickSettings Right(BackingStore store) =>
        store.rsOutputSettings[Slot].outputSettings.flickSettings;

    private static XElement TriggerElement(XDocument xml, string stick) =>
        xml.Root.Element(stick + "OutputSettings").Element("FlickStickSettings").Element("CalibrationTrigger");

    private static XmlSerializer Serializer() =>
        new(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
}
