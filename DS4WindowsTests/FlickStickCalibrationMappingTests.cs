using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.DS4Control;
using DS4Windows.Switch2;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class FlickStickCalibrationMappingTests
{
    private const int Slot = 7;
    private static readonly FieldInfo StoreField = typeof(Global).GetField("m_Config",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private BackingStore previousStore = null!;
    private VirtualKBMBase previousHandler = null!;
    private VirtualKBMMapping previousKbmMapping = null!;
    private DS4StateFieldMapping previousFields = null!, previousOutputFields = null!;
    private Mapping.SyntheticState previousDeviceState = null!;
    private RecordingHandler handler = null!;
    private ControlService service = null!;
    private Switch2RuntimeInputDevice runtime = null!;
    private Mouse mouse = null!;

    [TestInitialize]
    public void Initialize()
    {
        previousStore = Global.store;
        previousHandler = Global.outputKBMHandler;
        previousKbmMapping = Global.outputKBMMapping;
        previousFields = Mapping.fieldMappings[Slot];
        previousOutputFields = Mapping.outputFieldMappings[Slot];
        previousDeviceState = Mapping.deviceState[Slot];
        StoreField.SetValue(null, new BackingStore());
        handler = new RecordingHandler();
        Global.outputKBMHandler = handler;
        var kbm = new SendInputMapping();
        kbm.PopulateConstants();
        kbm.PopulateMappings();
        Global.outputKBMMapping = kbm;
        Mapping.fieldMappings[Slot] = new();
        Mapping.outputFieldMappings[Slot] = new();
        Mapping.deviceState[Slot] = new();
        Assert.IsTrue(Switch2RuntimeInputDevice.TryCreatePro(1, 1,
            Switch2Transport.Usb, out runtime, out _));
        runtime.Synced = true;
        mouse = new Mouse(Slot, runtime);
        service = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        service.DS4Controllers = new DS4Device[Global.MAX_DS4_CONTROLLER_COUNT];
        service.DS4Controllers[Slot] = runtime;
        Mapping.ResetFlickStickCalibration(Slot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Mapping.ResetFlickStickCalibration(Slot);
        StoreField.SetValue(null, previousStore);
        Global.outputKBMHandler = previousHandler;
        Global.outputKBMMapping = previousKbmMapping;
        Mapping.fieldMappings[Slot] = previousFields;
        Mapping.outputFieldMappings[Slot] = previousOutputFields;
        Mapping.deviceState[Slot] = previousDeviceState;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CanonicalMapperProducesOneExactTurnWithoutVirtualButtonLeak(bool shifted)
    {
        Bind(shifted);
        Global.LSOutputSettings[Slot].outputSettings.flickSettings.realWorldCalibration = 2.0;
        Global.RSOutputSettings[Slot].outputSettings.flickSettings.realWorldCalibration = 5.3;
        Frame(0, false, shifted);
        var pressed = Frame(.004, true, shifted);
        Assert.IsFalse(pressed.Cross, "A calibration binding must consume its original game button.");
        for (int frame = 2; frame <= 400; frame++)
            Frame(frame * .004, true, shifted);
        Assert.AreEqual(shifted ? 720 : 1908, handler.Moves.Sum(move => move.X));
        Assert.IsTrue(handler.Moves.All(move => move.Y == 0));
        Assert.IsTrue(handler.Moves.All(move => move.X is >= 0 and <= 32767));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AxisConfiguredButtonUsesItsStickCalibrationAndConsumesOnlyItsNormalOutput(bool right)
    {
        ConfigureAxis(right, DS4Controls.Cross, right ? 5.3 : 2.0);
        Frame(0, false);
        Assert.IsFalse(Frame(.004, true, true).Cross);
        for (int frame = 2; frame <= 400; frame++)
        {
            var mapped = Frame(frame * .004, true, true);
            Assert.IsFalse(mapped.Cross);
            Assert.IsTrue(mapped.Triangle, "Other game buttons remain live during the turn.");
        }
        Assert.AreEqual(right ? 1908 : 720, handler.Moves.Sum(move => move.X));
        Assert.IsTrue(handler.Moves.All(move => move.Y == 0));
    }

    [TestMethod]
    public void AxisFlickModeAdmitsMapperWithoutAnyButtonRemapsOrSpecialActions()
    {
        Global.store.profileActions[Slot].Clear();
        Global.store.profileActionCount[Slot] = 0;
        ConfigureAxis(true, DS4Controls.Cross);
        Assert.IsFalse(Global.store.HasCustomActions(Slot));
        Global.store.CacheProfileCustomsFlags(Slot);
        Assert.IsTrue(Global.containsCustomAction(Slot));
    }

    [TestMethod]
    public void SharedAxisButtonRequestsOneRightStickTurn()
    {
        ConfigureAxis(false, DS4Controls.Cross, 2.0);
        ConfigureAxis(true, DS4Controls.Cross, 5.3);
        Frame(0, false);
        Frame(.004, true);
        for (int frame = 2; frame <= 260; frame++) Frame(frame * .004, false);
        Assert.AreEqual(1908, handler.Moves.Sum(move => move.X));
    }

    [TestMethod]
    [DataRow(StickMode.Controls)]
    [DataRow(StickMode.None)]
    public void InactiveAxisModeDoesNotConsumeOrTurn(StickMode mode)
    {
        ConfigureAxis(true, DS4Controls.Cross);
        Global.RSOutputSettings[Slot].mode = mode;
        Frame(0, false);
        for (int frame = 1; frame <= 260; frame++)
            Assert.IsTrue(Frame(frame * .004, true).Cross);
        Assert.AreEqual(0, handler.Moves.Count);
    }

    [TestMethod]
    [DataRow(DS4Controls.None)]
    [DataRow(DS4Controls.GyroXNeg)]
    [DataRow(DS4Controls.SwipeRight)]
    [DataRow(DS4Controls.LXNeg)]
    [DataRow((DS4Controls)255)]
    public void UnassignedOrUnsupportedAxisSourceIsSafelyIgnored(DS4Controls trigger)
    {
        ConfigureAxis(true, trigger);
        Frame(0, false);
        Assert.IsTrue(Frame(.004, true).Cross);
        for (int frame = 2; frame <= 260; frame++) Frame(frame * .004, true);
        Assert.AreEqual(0, handler.Moves.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AxisModeOrSourceChangeCancelsWithoutStartingOnAnAlreadyHeldButton(bool changeMode)
    {
        ConfigureAxis(true, DS4Controls.Cross);
        Frame(0, false);
        Frame(.004, true);
        Frame(.008, true);
        Assert.IsTrue(handler.Moves.Count > 0);
        if (changeMode) Global.RSOutputSettings[Slot].mode = StickMode.Controls;
        else Global.RSOutputSettings[Slot].outputSettings.flickSettings.calibrationTrigger = DS4Controls.Triangle;
        handler.Moves.Clear();
        for (int frame = 3; frame <= 260; frame++) Frame(frame * .004, true, true);
        Assert.AreEqual(0, handler.Moves.Count);
        ConfigureAxis(true, DS4Controls.Cross);
        for (int frame = 261; frame <= 280; frame++) Frame(frame * .004, true);
        Assert.AreEqual(0, handler.Moves.Count);
        Frame(1.124, false);
        Frame(1.128, true);
        for (int frame = 283; frame <= 540; frame++) Frame(frame * .004, false);
        Assert.AreEqual(1908, handler.Moves.Sum(move => move.X));
    }

    [TestMethod]
    public void ReservingAButtonDoesNotEraseAnotherMappingIntoItsDestination()
    {
        ConfigureAxis(true, DS4Controls.Cross);
        Global.store.GetDS4CSetting(Slot, DS4Controls.Triangle)
            .UpdateSettings(false, X360Controls.A, "", DS4KeyType.None);
        Frame(0, false);
        Assert.IsTrue(Frame(.004, true, true).Cross,
            "Triangle mapped to Cross must survive source-only reservation.");
    }

    [TestMethod]
    public void AxisButtonSuppressesAnExistingKeyBindingWithoutOverwritingIt()
    {
        ConfigureAxis(true, DS4Controls.Cross);
        var setting = Global.store.GetDS4CSetting(Slot, DS4Controls.Cross);
        setting.UpdateSettings(false, (ushort)65, "", DS4KeyType.None);
        Frame(0, false);
        for (int frame = 1; frame <= 260; frame++) Frame(frame * .004, true);
        Assert.IsTrue(Mapping.deviceState[Slot].keyPresses.Values.All(key =>
            key.current.vkCount == 0 && key.current.scanCodeCount == 0));
        Assert.AreEqual(DS4ControlSettings.ActionType.Key, setting.actionType);
        Assert.AreEqual(1908, handler.Moves.Sum(move => move.X));
    }

    [TestMethod]
    [DataRow(DS4Controls.L2, TwoStageTriggerMode.Disabled)]
    [DataRow(DS4Controls.L2FullPull, TwoStageTriggerMode.Disabled)]
    [DataRow(DS4Controls.R2, TwoStageTriggerMode.Disabled)]
    [DataRow(DS4Controls.R2FullPull, TwoStageTriggerMode.Disabled)]
    [DataRow(DS4Controls.L2, TwoStageTriggerMode.Normal)]
    [DataRow(DS4Controls.L2FullPull, TwoStageTriggerMode.Normal)]
    [DataRow(DS4Controls.R2, TwoStageTriggerMode.ExclusiveButtons)]
    [DataRow(DS4Controls.R2FullPull, TwoStageTriggerMode.ExclusiveButtons)]
    public void CalibratingWithATriggerReservesBothStagesWithoutChangingPhysicalInput(
        DS4Controls trigger, TwoStageTriggerMode mode)
    {
        ConfigureAxis(true, trigger);
        Global.L2OutputSettings[Slot].twoStageMode = mode;
        Global.R2OutputSettings[Slot].twoStageMode = mode;
        Global.store.GetDS4CSetting(Slot, DS4Controls.L2FullPull)
            .UpdateSettings(false, X360Controls.A, "", DS4KeyType.None);
        Global.store.GetDS4CSetting(Slot, DS4Controls.R2FullPull)
            .UpdateSettings(false, X360Controls.A, "", DS4KeyType.None);
        bool left = trigger is DS4Controls.L2 or DS4Controls.L2FullPull;
        Frame(0, new DS4State());
        for (int frame = 1; frame <= 260; frame++)
        {
            var input = new DS4State
            {
                L2 = left ? (byte)255 : (byte)0, L2Raw = left ? (byte)255 : (byte)0,
                R2 = left ? (byte)0 : (byte)255, R2Raw = left ? (byte)0 : (byte)255,
            };
            var mapped = Frame(frame * .004, input);
            Assert.AreEqual((byte)255, left ? input.L2 : input.R2);
            Assert.AreEqual((byte)0, mapped.L2);
            Assert.AreEqual((byte)0, mapped.R2);
            Assert.IsFalse(mapped.Cross);
        }
        Assert.AreEqual(1908, handler.Moves.Sum(move => move.X));
    }

    private static void ConfigureAxis(bool right, DS4Controls trigger, double calibration = 5.3)
    {
        var settings = right ? Global.RSOutputSettings[Slot] : Global.LSOutputSettings[Slot];
        settings.mode = StickMode.FlickStick;
        settings.outputSettings.flickSettings.calibrationTrigger = trigger;
        settings.outputSettings.flickSettings.realWorldCalibration = calibration;
    }

    [TestMethod]
    public void ReleaseCompletesTurnAndResetCancelsWithoutHeldRestart()
    {
        Bind();
        Frame(0, false);
        Frame(.004, true);
        for (int frame = 2; frame <= 60; frame++) Frame(frame * .004, false);
        Assert.IsTrue(handler.Moves.Sum(move => move.X) > 0);
        Mapping.ResetFlickStickCalibration(Slot);
        handler.Moves.Clear();
        for (int frame = 61; frame <= 400; frame++) Frame(frame * .004, true);
        Assert.AreEqual(0, handler.Moves.Count);
        Frame(1.604, false);
        Frame(1.608, true);
        for (int frame = 403; frame <= 660; frame++) Frame(frame * .004, false);
        Assert.AreEqual(1908, handler.Moves.Sum(move => move.X));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TerminalNeutralCancelsButRegularNoMotionReportDoesNot(bool terminal)
    {
        Bind();
        Frame(0, false);
        Frame(.004, true);
        Frame(.008, false);
        int before = handler.Moves.Sum(move => move.X);
        mouse.PrepareGyroNeutralReport(terminal);
        for (int frame = 3; frame <= 260; frame++) Frame(frame * .004, false);
        Assert.AreEqual(terminal ? before : 1908, handler.Moves.Sum(move => move.X));
    }

    [TestMethod]
    public void NewMouseLifetimeCancelsEvenWhenTemporaryProfileSkipsPreLoadReset()
    {
        Bind();
        Frame(0, false);
        Frame(.004, true);
        Frame(.008, true);
        mouse = new Mouse(Slot, runtime);
        handler.Moves.Clear();
        for (int frame = 3; frame <= 280; frame++) Frame(frame * .004, true);
        Assert.AreEqual(0, handler.Moves.Count);
    }

    [TestMethod]
    [DataRow("removed", false)]
    [DataRow("removing", false)]
    [DataRow("unsynced", false)]
    [DataRow("owner", false)]
    [DataRow("removed", true)]
    [DataRow("removing", true)]
    [DataRow("unsynced", true)]
    [DataRow("owner", true)]
    public void UnavailableControllerCannotContinueCalibration(string reason, bool axis)
    {
        if (axis) ConfigureAxis(true, DS4Controls.Cross);
        else Bind();
        Frame(0, false);
        Frame(.004, true);
        Frame(.008, true);
        switch (reason)
        {
            case "removed": runtime.IsRemoved = true; break;
            case "removing": runtime.IsRemoving = true; break;
            case "unsynced": runtime.Synced = false; break;
            case "owner": service.DS4Controllers[Slot] = null; break;
        }
        handler.Moves.Clear();
        for (int frame = 3; frame <= 280; frame++) Frame(frame * .004, true);
        Assert.AreEqual(0, handler.Moves.Count);
    }

    [TestMethod]
    public void BufferedBackendPreservesNormalAndCalibrationMovementWithoutOverwrite()
    {
        handler.Buffered = true;
        Mapping.SendMouseMovementWithCalibration(32760, 9, 20);
        handler.Sync();
        CollectionAssert.AreEqual(new[] { (32760, 9), (20, 0) }, handler.Moves);
        handler.Moves.Clear();
        Mapping.SendMouseMovementWithCalibration(100, -2, 20);
        handler.Sync();
        CollectionAssert.AreEqual(new[] { (120, -2) }, handler.Moves);
    }

    [TestMethod]
    public void CalibrationPreservesAlreadyBufferedGyroOrTouchMovement()
    {
        handler.Buffered = true;
        handler.MoveRelativeMouse(7, 9);
        Mapping.SendMouseMovementWithCalibration(3, -2, 20);
        handler.Sync();
        CollectionAssert.AreEqual(new[] { (7, 9), (23, -2) }, handler.Moves);
    }

    [TestMethod]
    public void PausedMappingAndProfileLifecycleRequestCancellation()
    {
        string sourcePath = Path.Combine(Path.GetDirectoryName(StoreSourcePath())!,
            "..", "DS4Windows", "DS4Control", "ControlService.cs");
        string source = File.ReadAllText(sourcePath).Replace("\r\n", "\n");
        StringAssert.Contains(source, "Mapping.DiscardPostMapStickData(ind);\n                    Mapping.ResetFlickStickCalibration(ind);",
            "Skipped mapping must cancel a short in-progress turn, not catch up on resume.");
        int begin = source.IndexOf("public void PreLoadReset(int ind)", StringComparison.Ordinal);
        int end = source.IndexOf("public void TouchPadOn", begin, StringComparison.Ordinal);
        StringAssert.Contains(source.Substring(begin, end - begin), "Mapping.ResetFlickStickCalibration(ind);");
    }

    private static string StoreSourcePath([CallerFilePath] string caller = "") => caller;

    [TestMethod]
    public void NoCalibrationLeavesOrdinaryMovementUnchanged()
    {
        Mapping.SendMouseMovementWithCalibration(0, 0, 0);
        Assert.AreEqual(0, handler.Moves.Count);
        Mapping.SendMouseMovementWithCalibration(-17, 23, 0);
        CollectionAssert.AreEqual(new[] { (-17, 23) }, handler.Moves);
    }

    private void Bind(bool shifted = false)
    {
        var settings = Global.store.GetDS4CSetting(Slot, DS4Controls.Cross);
        settings.UpdateSettings(false, X360Controls.FlickStickCalibrate360RS, "", DS4KeyType.Toggle);
        if (shifted)
        {
            settings.shiftTrigger = 4; // Triangle (the existing universal shift trigger).
            settings.UpdateSettings(true, X360Controls.FlickStickCalibrate360LS, "", DS4KeyType.Toggle, 4);
        }
    }

    private DS4State Frame(double seconds, bool cross, bool triangle = false)
    {
        var source = new DS4State { Cross = cross, Triangle = triangle, elapsedTime = .004 };
        var mapped = Frame(seconds, source);
        Assert.AreEqual(cross, source.Cross, "The physical snapshot must remain intact.");
        return mapped;
    }

    private DS4State Frame(double seconds, DS4State source)
    {
        var mapped = new DS4State();
        source.CopyExtrasTo(mapped);
        Mapping.MapCustom(Slot, source, mapped, new DS4StateExposed(source), mouse, service,
            10 * Stopwatch.Frequency + (long)Math.Round(seconds * Stopwatch.Frequency));
        return mapped;
    }

    private sealed class RecordingHandler : VirtualKBMBase
    {
        internal readonly List<(int X, int Y)> Moves = new();
        internal bool Buffered;
        private (int X, int Y)? pending;
        public override bool Connect() => throw new AssertFailedException("No device I/O in mapper tests.");
        public override bool Disconnect() => throw new AssertFailedException("No device I/O in mapper tests.");
        public override void MoveRelativeMouse(int x, int y)
        {
            if (Buffered) pending = (x, y); else Moves.Add((x, y));
        }
        public override void Sync()
        {
            if (pending is { } value) Moves.Add(value);
            pending = null;
        }
        public override void MoveAbsoluteMouse(double x, double y) => Assert.Fail("Unexpected absolute mouse output.");
        public override void PerformMouseWheelEvent(int v, int h) => Assert.Fail("Unexpected wheel output.");
        public override void PerformMouseButtonEvent(uint button) => Assert.Fail("Unexpected mouse button.");
        public override void PerformMouseButtonPress(uint button) => Assert.Fail("Unexpected mouse button.");
        public override void PerformMouseButtonRelease(uint button) => Assert.Fail("Unexpected mouse button.");
        public override void PerformKeyPress(uint key) => Assert.Fail("Unexpected keyboard output.");
        public override void PerformKeyPressAlt(uint key) => Assert.Fail("Unexpected keyboard output.");
        public override void PerformKeyRelease(uint key) => Assert.Fail("Unexpected keyboard output.");
        public override void PerformKeyReleaseAlt(uint key) => Assert.Fail("Unexpected keyboard output.");
        public override string GetDisplayName() => "calibration-recording";
        public override string GetIdentifier() => "calibration-recording";
        public override string GetFullDisplayName() => "calibration-recording";
    }
}
