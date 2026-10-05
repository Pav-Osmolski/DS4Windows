using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.DS4Control;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ViiperTouchpadClickTests
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

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void WireClickFollowsMappedStateRatherThanPhysicalClick(bool physical, bool mapped)
    {
        AssertWireClick(new DS4State { TouchButton = physical, OutputTouchButton = mapped }, mapped);
    }

    [DataTestMethod]
    [DataRow(TouchpadOutMode.Controls, false)]
    [DataRow(TouchpadOutMode.Controls, true)]
    [DataRow(TouchpadOutMode.Mouse, false)]
    [DataRow(TouchpadOutMode.Mouse, true)]
    [DataRow(TouchpadOutMode.MouseJoystick, false)]
    [DataRow(TouchpadOutMode.MouseJoystick, true)]
    [DataRow(TouchpadOutMode.AbsoluteMouse, false)]
    [DataRow(TouchpadOutMode.AbsoluteMouse, true)]
    [DataRow(TouchpadOutMode.Passthru, false)]
    [DataRow(TouchpadOutMode.Passthru, true)]
    public void TouchpadPolicySurvivesPacketSerialization(TouchpadOutMode mode, bool clickPassthrough)
    {
        Global.TouchOutMode[Slot] = mode;
        Global.TouchClickPassthru[Slot] = clickPassthrough;
        DS4State source = device.getCurrentStateRef();
        source.TouchButton = source.OutputTouchButton = true;
        mouse.touchButtonDown(null, new TouchpadEventArgs(DateTime.UtcNow, true, false, null));
        Assert.IsTrue(source.TouchButton, "The physical observation must remain available.");
        AssertWireClick(Map(source), mode == TouchpadOutMode.Passthru || clickPassthrough);
    }

    [DataTestMethod]
    [DataRow(DS4Controls.TouchLeft, false)]
    [DataRow(DS4Controls.TouchLeft, true)]
    [DataRow(DS4Controls.TouchRight, false)]
    [DataRow(DS4Controls.TouchRight, true)]
    [DataRow(DS4Controls.TouchUpper, false)]
    [DataRow(DS4Controls.TouchUpper, true)]
    [DataRow(DS4Controls.TouchMulti, false)]
    [DataRow(DS4Controls.TouchMulti, true)]
    public void RemappedTouchRegionEmitsOnlyItsSelectedOutput(DS4Controls region, bool controllerClick)
    {
        Global.TouchOutMode[Slot] = TouchpadOutMode.Controls;
        Global.TouchClickPassthru[Slot] = false;
        DS4ControlSettings setting = Global.store.GetDS4CSetting(Slot, region);
        setting.UpdateSettings(false, controllerClick ? (object)X360Controls.TouchpadClick : 0x4D,
            string.Empty, DS4KeyType.None);
        if (!controllerClick) setting.action.actionAlias = 0x4D;
        DS4State source = device.getCurrentStateRef();
        source.TouchButton = source.OutputTouchButton = source.Touch1 = true;
        Touch first = region == DS4Controls.TouchUpper ? null :
            new Touch(region == DS4Controls.TouchRight ? 1500 : 400, 400, 0, null);
        Touch second = region == DS4Controls.TouchMulti ? new Touch(1500, 400, 1, null) : null;
        mouse.touchButtonDown(null, new TouchpadEventArgs(DateTime.UtcNow, true, first != null, first, second));
        Assert.IsTrue(region switch
        {
            DS4Controls.TouchLeft => mouse.leftDown,
            DS4Controls.TouchRight => mouse.rightDown,
            DS4Controls.TouchUpper => mouse.upperDown,
            _ => mouse.multiDown,
        }, "The real touch handler must select the requested region.");
        DS4State mapped = Map(source);
        Mapping.Commit(Slot);
        AssertWireClick(mapped, controllerClick);
        Assert.AreEqual(!controllerClick, handler.Keys.Contains(0x4D));
        source.TouchButton = source.OutputTouchButton = false;
        mouse.touchButtonUp(null, new TouchpadEventArgs(DateTime.UtcNow, false, first != null, first, second));
        AssertWireClick(Map(source), false);
        Mapping.Commit(Slot);
        Assert.AreEqual(0, handler.Keys.Count, "Releasing the touch must release the keyboard mapping.");
    }

    [TestMethod]
    public void ControllerMacroClickSurvivesMappingAndSerialization()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        FieldInfo countField = typeof(Mapping).GetField("macroCount", flags)!;
        FieldInfo controlsField = typeof(Mapping).GetField("macroControl", flags)!;
        object previousCount = countField.GetValue(null);
        object previousControls = controlsField.GetValue(null);
        try
        {
            var controls = new bool[26];
            controls[25] = true; // The existing controller macro touch-click output.
            controlsField.SetValue(null, controls);
            countField.SetValue(null, 1u);
            AssertWireClick(Map(new DS4State()), true);
            controls[25] = false;
            AssertWireClick(Map(new DS4State()), false);
        }
        finally
        {
            countField.SetValue(null, previousCount);
            controlsField.SetValue(null, previousControls);
        }
    }

    private DS4State Map(DS4State source)
    {
        source.elapsedTime = .004;
        var mapped = new DS4State();
        source.CopyExtrasTo(mapped);
        Mapping.MapCustom(Slot, source, mapped, new DS4StateExposed(source), mouse, service);
        return mapped;
    }

    private static void AssertWireClick(DS4State state, bool expected)
    {
        foreach (var type in new[] { ViiperVirtualDeviceType.DualShock4,
                     ViiperVirtualDeviceType.DualSense, ViiperVirtualDeviceType.DualSenseEdge })
        {
            byte[] packet = ViiperStatePacketBuilder.Build(type, state, -1);
            uint buttons = type == ViiperVirtualDeviceType.DualShock4
                ? BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4))
                : BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4));
            uint mask = type == ViiperVirtualDeviceType.DualShock4 ? 2u : 0x20000u;
            Assert.AreEqual(expected, (buttons & mask) != 0, type.ToString());
        }
    }

    private sealed class NoHidDevice : DS4Device
    {
        internal NoHidDevice() : base("Synthetic touchpad mapping", InputDeviceType.DualSense,
            ConnectionType.BT) { }
    }

    private sealed class RecordingHandler : VirtualKBMBase
    {
        internal readonly HashSet<uint> Keys = new();
        public override bool Connect() => throw new AssertFailedException("No system input allowed.");
        public override bool Disconnect() => throw new AssertFailedException("No system input allowed.");
        public override void MoveRelativeMouse(int x, int y) => Assert.Fail("Unexpected mouse movement.");
        public override void MoveAbsoluteMouse(double x, double y) => Assert.Fail("Unexpected absolute mouse.");
        public override void PerformMouseWheelEvent(int vertical, int horizontal) => Assert.Fail("Unexpected wheel.");
        public override void PerformMouseButtonEvent(uint button) { }
        public override void PerformMouseButtonPress(uint button) { }
        public override void PerformMouseButtonRelease(uint button) { }
        public override void PerformKeyPress(uint key) => Keys.Add(key);
        public override void PerformKeyPressAlt(uint key) => Keys.Add(key);
        public override void PerformKeyRelease(uint key) => Keys.Remove(key);
        public override void PerformKeyReleaseAlt(uint key) => Keys.Remove(key);
        public override string GetDisplayName() => "Synthetic touchpad mapping";
        public override string GetIdentifier() => GetDisplayName();
        public override string GetFullDisplayName() => GetDisplayName();
    }
}
