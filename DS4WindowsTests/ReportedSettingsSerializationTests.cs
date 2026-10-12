using System.Globalization;
using System.Xml.Serialization;
using DS4Windows;
using DS4Windows.Switch2;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class ReportedSettingsSerializationTests
{
    [TestMethod]
    [DataRow("en-US", "12/05/2023 00:24:15", 12, 5)]
    [DataRow("en-GB", "12/05/2023 00:24:15", 12, 5)]
    [DataRow("fr-FR", "12/05/2023 00:24:15", 12, 5)]
    [DataRow("en-GB", "12/23/2023 00:24:15", 12, 23)]
    [DataRow("fr-FR", "12/23/2023 00:24:15", 12, 23)]
    [DataRow("th-TH", "12/05/2023 00:24:15", 12, 5)]
    public void SavedUpdateCheckDateUsesItsCanonicalFormatAcrossLocales(
        string cultureName, string savedDate, int month, int day)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            AppSettingsDTO dto = ReadSettings(savedDate);

            Assert.AreEqual(new DateTime(2023, month, day, 0, 24, 15),
                dto.LastChecked);
            Assert.AreEqual(savedDate, dto.LastCheckString);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    [DataRow("en-GB")]
    [DataRow("fr-FR")]
    [DataRow("fi-FI")]
    [DataRow("th-TH")]
    public void UpdateCheckDateWriterDoesNotUseLocalSeparatorsOrCalendar(
        string cultureName)
    {
        const string savedDate = "12/05/2023 00:24:15";
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            AppSettingsDTO dto = ReadSettings(savedDate);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            var serializer = new XmlSerializer(typeof(AppSettingsDTO));
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            serializer.Serialize(writer, dto);
            StringAssert.Contains(writer.ToString(),
                $"<LastChecked>{savedDate}</LastChecked>");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void OlderLocalizedUpdateCheckDateRemainsReadable()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            AppSettingsDTO dto = ReadSettings("23.12.2023 18:21:05");

            Assert.AreEqual(new DateTime(2023, 12, 23, 18, 21, 5), dto.LastChecked);
            Assert.AreEqual("12/23/2023 18:21:05", dto.LastCheckString);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void InvalidUpdateCheckDateDoesNotReplaceTheLastValidValue()
    {
        AppSettingsDTO dto = ReadSettings("12/23/2023 00:24:15");
        DateTime previous = dto.LastChecked;

        dto.LastCheckString = "not a date";

        Assert.AreEqual(previous, dto.LastChecked);
    }

    [TestMethod]
    [DataRow("<Red>17</Red><Green>83</Green><Blue>201</Blue>", 17, 83, 201)]
    [DataRow("<Green>83</Green><Blue>201</Blue>", 0, 83, 201)]
    [DataRow("<Red>17</Red><Green>invalid</Green><Blue>201</Blue>", 17, 0, 201)]
    [DataRow("<Red>17</Red><Green>83</Green><Blue>invalid</Blue>", 17, 83, 0)]
    [DataRow("<Red>17</Red><Green>83</Green><Blue>201</Blue><Color>41,72,103</Color>", 41, 72, 103)]
    public void LegacyProfileLightbarChannelsMapIndependently(
        string colorElements, int red, int green, int blue)
    {
        var serializer = new XmlSerializer(typeof(ProfileDTO),
            ProfileDTO.GetAttributeOverrides());
        using var reader = new StringReader(
            $"<DS4Windows config_version=\"5\">{colorElements}</DS4Windows>");
        var dto = (ProfileDTO)serializer.Deserialize(reader);
        dto.DeviceIndex = Global.TEST_PROFILE_INDEX;
        BackingStore store = BackingStore.CreateProfileValidationStore();

        dto.MapTo(store);

        DS4Color actual = store.lightbarSettingInfo[Global.TEST_PROFILE_INDEX]
            .ds4winSettings.m_Led;
        Assert.AreEqual((byte)red, actual.red);
        Assert.AreEqual((byte)green, actual.green);
        Assert.AreEqual((byte)blue, actual.blue);
    }

    [TestMethod]
    [DataRow(DS4ControlSettings.ActionType.Default)]
    [DataRow(DS4ControlSettings.ActionType.Button)]
    [DataRow(DS4ControlSettings.ActionType.Key)]
    [DataRow(DS4ControlSettings.ActionType.Macro)]
    public void NormalAndShiftExtrasSurviveRepeatedProfileSaveAndLoad(
        DS4ControlSettings.ActionType shiftActionType)
    {
        const string normalExtras = "10,20,0,0,0,0,0,0,0";
        const string shiftExtras = "30,40,0,0,0,0,0,0,0";
        BackingStore store = BackingStore.CreateProfileValidationStore();
        DS4ControlSettings original = store.GetDS4CSetting(
            Global.TEST_PROFILE_INDEX, DS4Controls.Cross);
        original.extras = normalExtras;
        original.shiftExtras = shiftExtras;
        original.shiftTrigger = 12;
        original.shiftActionType = shiftActionType;
        original.shiftAction.actionBtn = X360Controls.B;
        original.shiftAction.actionKey = 66;
        original.shiftAction.actionMacro = new[] { 66, 66 };

        for (int cycle = 0; cycle < 2; cycle++)
        {
            string xml = WriteProfile(store);
            StringAssert.Contains(xml,
                $"<Cross Trigger=\"12\">{shiftExtras}</Cross>");
            store = ReadProfile(xml);
            DS4ControlSettings actual = store.GetDS4CSetting(
                Global.TEST_PROFILE_INDEX, DS4Controls.Cross);
            Assert.AreEqual(normalExtras, actual.extras);
            Assert.AreEqual(shiftExtras, actual.shiftExtras);
            Assert.AreEqual(12, actual.shiftTrigger);
            Assert.AreEqual(shiftActionType, actual.shiftActionType);
            if (shiftActionType == DS4ControlSettings.ActionType.Button)
                Assert.AreEqual(X360Controls.B, actual.shiftAction.actionBtn);
            else if (shiftActionType == DS4ControlSettings.ActionType.Key)
                Assert.AreEqual(66, actual.shiftAction.actionKey);
            else if (shiftActionType == DS4ControlSettings.ActionType.Macro)
                CollectionAssert.AreEqual(new[] { 66, 66 },
                    actual.shiftAction.actionMacro);
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" Trigger=\"13\"")]
    [DataRow(" Trigger=\"0\"")]
    [DataRow(" Trigger=\"invalid\"")]
    public void LegacyShiftExtrasPreserveNormalExtrasAndActionTrigger(
        string extrasTrigger)
    {
        const string normalExtras = "10,20,0,0,0,0,0,0,0";
        const string shiftExtras = "30,40,0,0,0,0,0,0,0";
        BackingStore store = ReadProfile($"""
            <DS4Windows config_version="5">
              <Control><Extras><Cross>{normalExtras}</Cross></Extras></Control>
              <ShiftControl>
                <Button><Cross Trigger="12">B</Cross></Button>
                <Extras><Cross{extrasTrigger}>{shiftExtras}</Cross></Extras>
              </ShiftControl>
            </DS4Windows>
            """);

        DS4ControlSettings actual = store.GetDS4CSetting(
            Global.TEST_PROFILE_INDEX, DS4Controls.Cross);
        Assert.AreEqual(normalExtras, actual.extras);
        Assert.AreEqual(shiftExtras, actual.shiftExtras);
        Assert.AreEqual(12, actual.shiftTrigger,
            "The action's trigger takes precedence over extras metadata.");
        Assert.AreEqual(X360Controls.B, actual.shiftAction.actionBtn);
    }

    [TestMethod]
    public void LegacyModeShiftExtrasOnlyProfileMigratesAndResaves()
    {
        const string extras = "0,0,0,0,0,0,0,1,7";
        BackingStore store = ReadProfile($"""
            <DS4Windows config_version="5">
              <ShiftControl><Extras>
                <Cross Trigger="37">{extras}</Cross>
              </Extras></ShiftControl>
            </DS4Windows>
            """);

        for (int cycle = 0; cycle < 2; cycle++)
        {
            DS4ControlSettings actual = store.GetDS4CSetting(
                Global.TEST_PROFILE_INDEX, DS4Controls.Cross);
            Assert.AreEqual(Mapping.SWITCH2_MODE_SHIFT_TRIGGER,
                actual.shiftTrigger);
            Assert.IsNull(actual.extras);
            Assert.IsNull(actual.shiftExtras);
            foreach (Switch2ModeShiftScope scope in
                Enum.GetValues<Switch2ModeShiftScope>())
            {
                Switch2ModeShiftAction lane =
                    actual.GetSwitch2ModeShiftAction(scope);
                Assert.AreEqual(DS4ControlSettings.ActionType.Default,
                    lane.ActionType);
                Assert.AreEqual(extras, lane.Extras);
            }
            store = ReadProfile(WriteProfile(store));
        }
    }

    private static string WriteProfile(BackingStore store)
    {
        var dto = new ProfileDTO { DeviceIndex = Global.TEST_PROFILE_INDEX };
        dto.MapFrom(store);
        var serializer = new XmlSerializer(typeof(ProfileDTO),
            ProfileDTO.GetAttributeOverrides());
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        serializer.Serialize(writer, dto);
        return writer.ToString();
    }

    private static BackingStore ReadProfile(string xml)
    {
        var serializer = new XmlSerializer(typeof(ProfileDTO),
            ProfileDTO.GetAttributeOverrides());
        using var reader = new StringReader(xml);
        var dto = (ProfileDTO)serializer.Deserialize(reader);
        dto.DeviceIndex = Global.TEST_PROFILE_INDEX;
        BackingStore store = BackingStore.CreateProfileValidationStore();
        dto.MapTo(store);
        return store;
    }

    private static AppSettingsDTO ReadSettings(string savedDate)
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var reader = new StringReader(
            $"<Profile><LastChecked>{savedDate}</LastChecked></Profile>");
        return (AppSettingsDTO)serializer.Deserialize(reader);
    }
}
