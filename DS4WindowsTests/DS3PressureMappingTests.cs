using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.DS4Control;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class DS3PressureMappingTests
{
    private const int Slot = 7;
    private static readonly FieldInfo StoreField = typeof(Global).GetField(
        "m_Config", BindingFlags.Static | BindingFlags.NonPublic)!;
    private BackingStore previousStore;
    private VirtualKBMBase previousHandler;
    private VirtualKBMMapping previousMapping;
    private Mapping.SyntheticState previousGlobal;
    private Mapping.SyntheticState[] previousDevices;
    private DS4StateFieldMapping[] previousFields, previousOutputFields;
    private Mapping.TwoStageTriggerMappingData[] previousLeftStages, previousRightStages;
    private NoHidDevice device;
    private Mouse mouse;
    private ControlService service;
    private RecordingHandler handler;

    [TestInitialize]
    public void Initialize()
    {
        previousStore = Global.store;
        previousHandler = Global.outputKBMHandler;
        previousMapping = Global.outputKBMMapping;
        previousGlobal = Mapping.globalState;
        previousDevices = Mapping.deviceState;
        previousFields = Mapping.fieldMappings;
        previousOutputFields = Mapping.outputFieldMappings;
        previousLeftStages = Mapping.l2TwoStageMappingData;
        previousRightStages = Mapping.r2TwoStageMappingData;
        StoreField.SetValue(null, new BackingStore());
        Global.outputKBMHandler = handler = new RecordingHandler();
        var mapping = new SendInputMapping();
        mapping.PopulateConstants();
        mapping.PopulateMappings();
        Global.outputKBMMapping = mapping;
        Mapping.globalState = new();
        Mapping.deviceState = Enumerable.Range(0, Global.MAX_DS4_CONTROLLER_COUNT)
            .Select(_ => new Mapping.SyntheticState()).ToArray();
        Mapping.fieldMappings = Enumerable.Range(0, Global.MAX_DS4_CONTROLLER_COUNT)
            .Select(_ => new DS4StateFieldMapping()).ToArray();
        Mapping.outputFieldMappings = Enumerable.Range(0, Global.MAX_DS4_CONTROLLER_COUNT)
            .Select(_ => new DS4StateFieldMapping()).ToArray();
        Mapping.l2TwoStageMappingData = Enumerable.Range(0, Global.MAX_DS4_CONTROLLER_COUNT)
            .Select(_ => new Mapping.TwoStageTriggerMappingData()).ToArray();
        Mapping.r2TwoStageMappingData = Enumerable.Range(0, Global.MAX_DS4_CONTROLLER_COUNT)
            .Select(_ => new Mapping.TwoStageTriggerMappingData()).ToArray();
        device = new NoHidDevice();
        mouse = new Mouse(Slot, device);
        service = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        service.DS4Controllers = new DS4Device[Global.MAX_DS4_CONTROLLER_COUNT];
        service.DS4Controllers[Slot] = device;
        Global.store.profileActions[Slot].Clear();
        Global.store.profileActionCount[Slot] = 0;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Mapping.DiscardPostMapStickData(Slot);
        StoreField.SetValue(null, previousStore);
        Global.outputKBMHandler = previousHandler;
        Global.outputKBMMapping = previousMapping;
        Mapping.globalState = previousGlobal;
        Mapping.deviceState = previousDevices;
        Mapping.fieldMappings = previousFields;
        Mapping.outputFieldMappings = previousOutputFields;
        Mapping.l2TwoStageMappingData = previousLeftStages;
        Mapping.r2TwoStageMappingData = previousRightStages;
    }

    [TestMethod]
    [DataRow(0, 100)]
    [DataRow(10, 80)]
    [DataRow(40, 41)]
    public void RealProfileMappingsUsePressureAndReleaseWithoutChangingDigitalActions(int startPercent, int fullPercent)
    {
        Global.store.ds3PressureSettings[Slot].SetRange(startPercent, fullPercent);
        Global.store.GetDS4CSetting(Slot, DS4Controls.Cross).UpdateSettings(false, X360Controls.RT, string.Empty, DS4KeyType.None);
        Global.store.GetDS4CSetting(Slot, DS4Controls.Square).UpdateSettings(false, X360Controls.LT, string.Empty, DS4KeyType.None);
        Global.store.GetDS4CSetting(Slot, DS4Controls.DpadUp).UpdateSettings(false, X360Controls.LYNeg, string.Empty, DS4KeyType.None);
        Global.store.GetDS4CSetting(Slot, DS4Controls.DpadDown).UpdateSettings(false, X360Controls.LYPos, string.Empty, DS4KeyType.None);
        Global.store.GetDS4CSetting(Slot, DS4Controls.L1).UpdateSettings(false, X360Controls.RXNeg, string.Empty, DS4KeyType.None);
        Global.store.GetDS4CSetting(Slot, DS4Controls.R1).UpdateSettings(false, X360Controls.RXPos, string.Empty, DS4KeyType.None);
        Global.store.GetDS4CSetting(Slot, DS4Controls.Triangle).UpdateSettings(false, X360Controls.B, string.Empty, DS4KeyType.None);
        DS4State source = device.getCurrentStateRef();
        foreach (byte pressure in new byte[] { 0, 1, 31, 64, 127, 128, 191, 254, 255, 128, 1, 0 })
        {
            byte expected = (byte)Math.Clamp(Math.Round((pressure * 100d - startPercent * 255d) / (fullPercent - startPercent), MidpointRounding.AwayFromZero), 0, 255);
            var report = DS3PressureTests.Report(pressure);
            report[19] = report[20] = 0; // native L2/R2 must not supply the mapped trigger values
            report[17] = report[22] = 0; // down and R1 must not compete with the negative directions
            Assert.IsTrue(DS3InputReportParser.TryReadControls(report, 1, source));
            source.elapsedTime = .004;
            var mapped = new DS4State(); source.CopyExtrasTo(mapped);
            Mapping.MapCustom(Slot, source, mapped, new DS4StateExposed(source), mouse, service);
            Assert.AreEqual(expected, mapped.R2, "Cross -> right trigger");
            Assert.AreEqual(expected, mapped.L2, "Square -> left trigger");
            byte[] xbox360 = ViiperStatePacketBuilder.Build(ViiperVirtualDeviceType.Xbox360, mapped, Slot);
            Assert.AreEqual(expected, xbox360[4]); Assert.AreEqual(expected, xbox360[5]);
            byte[] xboxOne = ViiperStatePacketBuilder.Build(ViiperVirtualDeviceType.XboxOne, mapped, Slot);
            ushort expanded = (ushort)((expected * 1023u + 127u) / 255u);
            Assert.AreEqual(expanded, BinaryPrimitives.ReadUInt16LittleEndian(xboxOne.AsSpan(8, 2)));
            Assert.AreEqual(expanded, BinaryPrimitives.ReadUInt16LittleEndian(xboxOne.AsSpan(10, 2)));
            byte[] ds4 = ViiperStatePacketBuilder.Build(ViiperVirtualDeviceType.DualShock4, mapped, Slot);
            Assert.AreEqual(expected, ds4[7]); Assert.AreEqual(expected, ds4[8]);
            foreach (var type in new[] { ViiperVirtualDeviceType.DualSense, ViiperVirtualDeviceType.DualSenseEdge })
            {
                byte[] sony = ViiperStatePacketBuilder.Build(type, mapped, Slot);
                Assert.AreEqual(expected, sony[9]); Assert.AreEqual(expected, sony[10]);
            }
            // Switch 2 has digital triggers, but its stick directions retain partial pressure.
            var nintendo = ViiperStatePacketBuilder.BuildSwitch2State(mapped, Slot);
            Assert.AreEqual((ushort)Math.Round(2048d - expected * (2048d / 255d), MidpointRounding.AwayFromZero),
                nintendo.LeftStickY);
            if (expected > 0 && expected < 255) Assert.IsTrue(nintendo.LeftStickY > 0 && nintendo.LeftStickY < 2048);
            Assert.AreEqual(128d - expected * (128d / 255d), mapped.LYAxis.ProfileCoordinate, 1e-10, "D-pad -> left stick");
            Assert.AreEqual(128d - expected * (128d / 255d), mapped.RXAxis.ProfileCoordinate, 1e-10, "L1 -> right stick");
            Assert.AreEqual(pressure > 0, mapped.Circle, "Triangle -> B remains an ordinary digital action");
            Assert.IsFalse(mapped.Cross || mapped.Square || mapped.Triangle || mapped.L1, "Custom mappings must still remove their original default buttons.");
        }
    }

    [TestMethod]
    public void PressureChangesDoNotPulseOrScaleKeyboardAssignments()
    {
        Global.store.ds3PressureSettings[Slot].SetRange(10, 80);
        var keySetting = Global.store.GetDS4CSetting(Slot, DS4Controls.Cross);
        keySetting.UpdateSettings(false, 0x4D, string.Empty, DS4KeyType.None);
        keySetting.action.actionAlias = 0x4D; // populated by profile loading for the selected keyboard backend
        DS4State source = device.getCurrentStateRef();
        foreach (byte pressure in new byte[] { 0, 1, 127, 255, 64, 0 })
        {
            Assert.IsTrue(DS3InputReportParser.TryReadControls(DS3PressureTests.Report(pressure), 1, source));
            source.elapsedTime = .004;
            var mapped = new DS4State(); source.CopyExtrasTo(mapped);
            Mapping.MapCustom(Slot, source, mapped, new DS4StateExposed(source), mouse, service);
            Mapping.Commit(Slot);
            Assert.AreEqual(pressure > 0, handler.Keys.Contains(0x4D), "A keyboard key stays digital throughout a changing-pressure hold.");
        }
        Assert.AreEqual(0, handler.Keys.Count, "Release must not leave a stuck key.");
        Assert.AreEqual(1, handler.KeyPresses, "Changing pressure must not re-press the held key.");
        Assert.AreEqual(1, handler.KeyReleases);
    }

    [TestMethod]
    public void RangeDoesNotScaleNativeTriggersOrDigitalControllerFallback()
    {
        Global.store.ds3PressureSettings[Slot].SetRange(10, 80);
        var input = new DS4State { Cross = true, L2 = 64, R2 = 128 };
        var fields = new DS4StateFieldMapping(input, new DS4StateExposed(input), mouse);
        Assert.AreEqual((byte)255, Mapping.GetByteMapping(Slot, DS4Controls.Cross, input, new DS4StateExposed(input), mouse, fields, true));
        Assert.AreEqual((byte)64, Mapping.GetByteMapping(Slot, DS4Controls.L2, input, new DS4StateExposed(input), mouse, fields, true));
        Assert.AreEqual((byte)128, Mapping.GetByteMapping(Slot, DS4Controls.R2, input, new DS4StateExposed(input), mouse, fields, true));
        Assert.AreEqual(0d, Mapping.GetXYAxisMapping(Slot, DS4Controls.Cross, input, new DS4StateExposed(input), mouse, fields).ProfileCoordinate);
    }

    [TestMethod]
    public void MouseMagnitudeStaysRawAndConsumedPressureCannotBeResurrected()
    {
        Global.store.ds3PressureSettings[Slot].SetRange(10, 80);
        var input = new DS4State();
        Assert.IsTrue(DS3InputReportParser.TryReadControls(DS3PressureTests.Report(64), 1, input));
        var fields = new DS4StateFieldMapping(input, new DS4StateExposed(input), mouse);
        Assert.AreEqual((byte)64, Mapping.GetByteMapping(Slot, DS4Controls.Cross, input, new DS4StateExposed(input), mouse, fields));
        fields.buttons[(int)DS4Controls.Cross] = false;
        Assert.AreEqual((byte)0, Mapping.GetByteMapping(Slot, DS4Controls.Cross, input, new DS4StateExposed(input), mouse, fields, true));
    }

    [TestMethod]
    public void EveryPressureButtonUsesTheRangeForTriggersAndBothStickDirections()
    {
        Global.store.ds3PressureSettings[Slot].SetRange(10, 80);
        var input = new DS4State();
        var fields = new DS4StateFieldMapping();
        for (int raw = 0; raw <= 255; raw++)
        {
            Assert.IsTrue(DS3InputReportParser.TryReadControls(DS3PressureTests.Report((byte)raw), 1, input));
            var exposed = new DS4StateExposed(input); fields.PopulateFieldMapping(input, exposed, mouse);
            byte expected = (byte)Math.Clamp(Math.Round((raw * 100d - 2550) / 70d, MidpointRounding.AwayFromZero), 0, 255);
            foreach (DS4Controls control in DS3PressureTests.Controls)
            {
                Assert.AreEqual(expected, Mapping.GetByteMapping(Slot, control, input, exposed, mouse, fields, true));
                Assert.AreEqual(128d - expected * (128d / 255d), Mapping.GetXYAxisMapping(Slot, control, input, exposed, mouse, fields).ProfileCoordinate, 1e-10);
                Assert.AreEqual(128d + expected * (127d / 255d), Mapping.GetXYAxisMapping(Slot, control, input, exposed, mouse, fields, true).ProfileCoordinate, 1e-10);
            }
        }
    }

    private sealed class NoHidDevice : DS4Device
    {
        internal NoHidDevice() : base("Synthetic DS3 pressure mapping", InputDeviceType.DS3,
            ConnectionType.BT) { }
    }

    private sealed class RecordingHandler : VirtualKBMBase
    {
        internal readonly HashSet<uint> Keys = new();
        internal int KeyPresses, KeyReleases;
        public override bool Connect() => throw new AssertFailedException("No system input allowed.");
        public override bool Disconnect() => throw new AssertFailedException("No system input allowed.");
        public override void MoveRelativeMouse(int x, int y) => Assert.Fail("Unexpected mouse movement.");
        public override void MoveAbsoluteMouse(double x, double y) => Assert.Fail("Unexpected absolute mouse.");
        public override void PerformMouseWheelEvent(int vertical, int horizontal) => Assert.Fail("Unexpected wheel.");
        public override void PerformMouseButtonEvent(uint button) { }
        public override void PerformMouseButtonPress(uint button) { }
        public override void PerformMouseButtonRelease(uint button) { }
        public override void PerformKeyPress(uint key) { Keys.Add(key); KeyPresses++; }
        public override void PerformKeyPressAlt(uint key) { Keys.Add(key); KeyPresses++; }
        public override void PerformKeyRelease(uint key) { Keys.Remove(key); KeyReleases++; }
        public override void PerformKeyReleaseAlt(uint key) { Keys.Remove(key); KeyReleases++; }
        public override string GetDisplayName() => "Synthetic DS3 pressure mapping";
        public override string GetIdentifier() => GetDisplayName();
        public override string GetFullDisplayName() => GetDisplayName();
    }
}
