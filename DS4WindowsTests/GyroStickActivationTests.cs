using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4Windows.Switch2;

namespace DS4WindowsTests;

[TestClass]
public sealed class GyroStickActivationTests
{
    [TestMethod]
    [DoNotParallelize]
    public void RealMouseCombinesStickGateWithHoldAndReverseActivation()
    {
        const int slot = Global.TEST_PROFILE_INDEX;
        var oldMode = Global.GyroStickModes[slot];
        var oldStick = Global.GyroActivationSticks[slot];
        var oldThreshold = Global.GyroStickThresholds[slot];
        var oldOutput = Global.GyroOutputMode[slot];
        var controls = Global.GetGyroControlsInfo(slot);
        var oldTriggers = controls.triggers;
        var oldCondition = controls.triggerCond;
        var oldReverse = Global.GyroTriggerTurns[slot];
        try
        {
            Assert.IsTrue(Switch2RuntimeInputDevice.TryCreatePro(1, 1,
                Switch2Transport.Usb, out var device, out _));
            var mouse = new Mouse(slot, device) { ToggleGyroControls = false };
            Global.GyroStickModes[slot] = GyroStickActivationMode.WhileMoving;
            Global.GyroActivationSticks[slot] = GyroActivationStick.Left;
            Global.GyroStickThresholds[slot] = 15;
            Global.GyroOutputMode[slot] = GyroOutMode.Controls;
            controls.triggers = "0"; controls.triggerCond = false;
            Global.GyroTriggerTurns[slot] = true;
            var state = State();
            state.Motion = new SixAxis(100, 200, 300, 0, 0, 8192, 0.004);
            var args = new SixAxisEventArgs(DateTime.UtcNow, state.Motion);
            args.Reset(DateTime.UtcNow, state.Motion, state);
            state.Cross = true;
            mouse.sixaxisMoved(null, args);
            Assert.IsFalse(state.Motion.outputGyroControls);
            Assert.IsFalse(state.GyroStickGateOpen);
            state.LX = 200;
            mouse.sixaxisMoved(null, args);
            Assert.IsTrue(state.Motion.outputGyroControls);
            Assert.IsTrue(state.GyroStickGateOpen);
            state.Cross = false;
            mouse.sixaxisMoved(null, args);
            Assert.IsFalse(state.Motion.outputGyroControls, "Stick movement must not bypass the button condition.");
            Global.GyroTriggerTurns[slot] = false;
            mouse.sixaxisMoved(null, args);
            Assert.IsTrue(state.Motion.outputGyroControls);
            state.LX = 128;
            mouse.sixaxisMoved(null, args);
            Assert.IsFalse(state.Motion.outputGyroControls, "Reverse activation must not invert the independent stick gate.");
            Global.GyroStickModes[slot] = GyroStickActivationMode.WhileCentered;
            mouse.sixaxisMoved(null, args);
            Assert.IsTrue(state.Motion.outputGyroControls);
            mouse.ResetToggleGyroModes();
        }
        finally
        {
            Global.GyroStickModes[slot] = oldMode;
            Global.GyroActivationSticks[slot] = oldStick;
            Global.GyroStickThresholds[slot] = oldThreshold;
            Global.GyroOutputMode[slot] = oldOutput;
            controls.triggers = oldTriggers; controls.triggerCond = oldCondition;
            Global.GyroTriggerTurns[slot] = oldReverse;
        }
    }

    private static DS4State State(byte lx = 128, byte ly = 128, byte rx = 128, byte ry = 128) =>
        new() { LX = lx, LY = ly, RX = rx, RY = ry };

    [TestMethod]
    public void StickSelectionAndInverseBehaviourUseMovementInsteadOfClicks()
    {
        var gate = new GyroStickActivation();
        var state = State(rx: 200);
        Assert.IsFalse(gate.Evaluate(state, GyroStickActivationMode.WhileMoving, GyroActivationStick.Left, 15));
        Assert.IsTrue(gate.Evaluate(state, GyroStickActivationMode.WhileMoving, GyroActivationStick.Right, 15));
        Assert.IsTrue(gate.Evaluate(state, GyroStickActivationMode.WhileMoving, GyroActivationStick.Either, 15));
        Assert.IsFalse(gate.Evaluate(state, GyroStickActivationMode.WhileCentered, GyroActivationStick.Right, 15));
        state.RX = 128; state.L3 = state.R3 = true;
        Assert.IsFalse(gate.Evaluate(state, GyroStickActivationMode.WhileMoving, GyroActivationStick.Either, 15));
        Assert.IsTrue(gate.Evaluate(state, GyroStickActivationMode.WhileCentered, GyroActivationStick.Right, 15));
        Assert.IsTrue(gate.Evaluate(state, GyroStickActivationMode.Disabled, GyroActivationStick.Right, 15));
    }

    [TestMethod]
    public void RadialThresholdAndHysteresisPreventBoundaryChatter()
    {
        var gate = new GyroStickActivation();
        bool Active(byte x, byte y = 128) => gate.Evaluate(State(lx: x, ly: y),
            GyroStickActivationMode.WhileMoving, GyroActivationStick.Left, 20);
        Assert.IsFalse(Active(152)); // 18.9%, not yet above 20%
        Assert.IsTrue(Active(154));
        Assert.IsTrue(Active(152)); // stay active above the 18% release threshold
        Assert.IsFalse(Active(150));
        Assert.IsTrue(Active(147, 147)); // radial diagonal, neither axis exceeds 20%
        gate.Reset();
        Assert.IsFalse(Active(152));
    }

    [TestMethod]
    public void InvalidSettingsFailOpenAndNewSettingsResetHysteresis()
    {
        var gate = new GyroStickActivation();
        Assert.IsTrue(gate.Evaluate(State(), (GyroStickActivationMode)99, GyroActivationStick.Left, 15));
        Assert.IsTrue(gate.Evaluate(State(), GyroStickActivationMode.WhileMoving, (GyroActivationStick)99, 15));
        Assert.IsTrue(gate.Evaluate(State(lx: 154), GyroStickActivationMode.WhileMoving, GyroActivationStick.Left, 20));
        Assert.IsFalse(gate.Evaluate(State(lx: 152), GyroStickActivationMode.WhileMoving, GyroActivationStick.Left, 21));
    }

    [TestMethod]
    [DataRow(ViiperVirtualDeviceType.DualShock4, 19)]
    [DataRow(ViiperVirtualDeviceType.DualSense, 21)]
    [DataRow(ViiperVirtualDeviceType.DualSenseEdge, 21)]
    public void VirtualSonyGyroPausesWithoutMutatingSensorsOrOtherControls(ViiperVirtualDeviceType type, int offset)
    {
        var state = State(lx: 200);
        state.Cross = true;
        state.Motion = new SixAxis(0, 0, 0, 0, 0, 8192, 0.001)
        {
            gyroPitchFull = 1600, gyroYawFull = 3200, gyroRollFull = -800,
            accelXFull = 100, accelYFull = 200, accelZFull = 8192,
        };
        byte[] active = ViiperStatePacketBuilder.Build(type, state, -1);
        state.GyroStickGateOpen = false;
        byte[] paused = ViiperStatePacketBuilder.Build(type, state, -1);
        CollectionAssert.AreEqual(new byte[6], paused.AsSpan(offset, 6).ToArray());
        Array.Clear(active, offset, 6);
        CollectionAssert.AreEqual(active, paused);
        Assert.AreEqual(1600, state.Motion.gyroPitchFull);
        Assert.AreEqual(8192, state.Motion.accelZFull);
        state.GyroStickGateOpen = true;
        Assert.AreNotEqual((byte)0, ViiperStatePacketBuilder.Build(type, state, -1)[offset + 1]);
    }

    [TestMethod]
    public void OutputGateSurvivesStateCopiesAndOwnedSnapshots()
    {
        var raw = State(); raw.GyroStickGateOpen = false;
        var copy = new DS4State(); raw.CopyTo(copy);
        Assert.IsFalse(copy.GyroStickGateOpen);
        copy = new DS4State(raw);
        Assert.IsFalse(copy.GyroStickGateOpen);
        copy = new DS4State(); raw.CopyExtrasTo(copy);
        Assert.IsFalse(copy.GyroStickGateOpen);
        var owned = new DS4StateOwnedSnapshot(); owned.Capture(raw);
        Assert.IsFalse(owned.State.GyroStickGateOpen);
    }

    [TestMethod]
    public void ProfileRoundTripKeepsGateAndOldProfilesDefaultOff()
    {
        using var reader = new StringReader("<DS4Windows />");
        var old = (ProfileDTO)ProfileDTO.Serializer.Deserialize(reader);
        Assert.AreEqual(GyroStickActivationMode.Disabled, old.GyroStickActivationMode);
        Assert.AreEqual(15, old.GyroStickThreshold);
        var source = new BackingStore();
        source.gyroStickModes[0] = GyroStickActivationMode.WhileCentered;
        source.gyroActivationSticks[0] = GyroActivationStick.Right;
        source.gyroStickThresholds[0] = 22;
        var dto = new ProfileDTO { DeviceIndex = 0 }; dto.MapFrom(source);
        using var writer = new StringWriter(); ProfileDTO.Serializer.Serialize(writer, dto);
        using var input = new StringReader(writer.ToString());
        var restored = (ProfileDTO)ProfileDTO.Serializer.Deserialize(input);
        restored.DeviceIndex = 0;
        var destination = new BackingStore(); restored.MapTo(destination);
        Assert.AreEqual(GyroStickActivationMode.WhileCentered, destination.gyroStickModes[0]);
        Assert.AreEqual(GyroActivationStick.Right, destination.gyroActivationSticks[0]);
        Assert.AreEqual(22, destination.gyroStickThresholds[0]);
        old.DeviceIndex = 0; old.MapTo(destination);
        Assert.AreEqual(GyroStickActivationMode.Disabled, destination.gyroStickModes[0]);
    }
}
