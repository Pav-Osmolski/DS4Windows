using System.Buffers.Binary;
using DS4Windows;
using DS4Windows.Switch2;

namespace DS4WindowsTests;

[TestClass]
public class Switch2GameCubeTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void PhysicalReportPreservesAnalogTriggersAndIndependentClicks(bool usb)
    {
        Switch2InputProtocolIdentity identity;
        if (usb)
        {
            Assert.IsFalse(Switch2InputProtocolIdentity.TryCreateFullControllerUsb(
                0x057e, 0x2073, 0x0100, out _), "Reject the virtual revision as physical input.");
            Assert.IsTrue(Switch2InputProtocolIdentity.TryCreateFullControllerUsb(
                0x057e, 0x2073, 0x0101, out identity));
        }
        else
        {
            Assert.IsTrue(Switch2InputProtocolIdentity.TryCreateBluetoothLe(
                Switch2InputCodec.ServiceUuid, Switch2InputCodec.Common05CharacteristicUuid,
                Switch2GattProperty.Notify | Switch2GattProperty.Read,
                Switch2ControllerModel.GameCube, out identity));
        }
        Assert.IsTrue(Switch2InputSessionDescriptor.TryCreate(identity, 7, 8, 1_000_000, out var descriptor));
        Assert.IsTrue(Switch2InputCalibrationSnapshot.TryCreateFallback(
            Switch2ControllerModel.GameCube, 7, out var calibration));

        byte[] body = new byte[64];
        body[0] = 5;
        body[1] = 1;
        body[5] = 0xC4; // A, Z and the right click.
        body[6] = 0x72; // Start, Home, Capture, C.
        body[7] = 0x42; // Up and the left click.
        body[12] = body[15] = 0x08;
        body[13] = body[16] = 0x80;
        body[61] = 120;
        body[62] = 36;

        var session = new Switch2InputSession(descriptor, calibration.WithGameCubeTriggerZero(30, 36));
        Assert.IsTrue(session.TryProcess(descriptor, body.AsSpan(usb ? 0 : 1), 100,
            out var input, out var error), error.ToString());
        Assert.IsTrue(Switch2ProProfileInputMapper.TryMap(input, out var profile, out var mapping), mapping.ToString());
        var state = new DS4State();
        Assert.IsTrue(profile.TryWriteLegacyState(state));
        Assert.IsTrue(state.Cross && state.R1 && state.Options && state.PS && state.Capture && state.DpadUp);
        Assert.IsFalse(state.L1);
        Assert.IsTrue(state.Switch2RawInputStatus.IsGameCube && state.Switch2RawInputStatus.CButton);
        Assert.AreEqual((byte)113, state.L2);
        Assert.AreEqual((byte)0, state.R2);
        Assert.IsTrue(state.L2Btn && state.R2Btn);
        Assert.IsFalse(profile.HasCommonMotion);
        byte[] output = ViiperStatePacketBuilder.Build(ViiperVirtualDeviceType.GameCube, state, -1);
        Assert.AreEqual(0x00132872u, BinaryPrimitives.ReadUInt32LittleEndian(output));
        Assert.AreEqual((byte)103, output[24]);
        Assert.AreEqual((byte)0, output[25]);
    }

    [TestMethod]
    public void DualSenseInputUsesPositionalButtonsAndClicksOnlyAtFullTravel()
    {
        var source = new DS4State
        {
            Cross = true, Circle = true, Square = true, Triangle = true,
            LX = 128, LY = 0, RX = 128, RY = 255,
            L2 = 128, L2Btn = true, R2 = 255, R2Btn = true,
        };
        byte[] report = ViiperStatePacketBuilder.Build(ViiperVirtualDeviceType.GameCube, source, -1);
        Assert.AreEqual(0x2Fu, BinaryPrimitives.ReadUInt32LittleEndian(report));
        Assert.AreEqual((ushort)4095, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(6)));
        Assert.AreEqual((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(10)));
        Assert.AreEqual(26, report.Length);
        Assert.AreEqual((byte)116, report[24]);
        Assert.AreEqual((byte)232, report[25]);
        Assert.IsTrue(report.AsSpan(12, 12).IndexOfAnyExcept((byte)0) < 0);
    }

    [TestMethod]
    public void BluetoothMotionProjectsToSixAxisAndRoundTripsToGameCubeOutput()
    {
        Assert.IsTrue(Switch2InputProtocolIdentity.TryCreateBluetoothLe(
            Switch2InputCodec.ServiceUuid, Switch2InputCodec.Common05CharacteristicUuid,
            Switch2GattProperty.Notify | Switch2GattProperty.Read,
            Switch2ControllerModel.GameCube, out var identity));
        Assert.IsTrue(Switch2InputSessionDescriptor.TryCreate(identity, 7, 8, 1_000_000, out var descriptor));
        Assert.IsTrue(Switch2InputCalibrationSnapshot.TryCreateFallback(
            Switch2ControllerModel.GameCube, 7, out var calibration));
        var session = new Switch2InputSession(descriptor, calibration);
        byte[] report = new byte[64];
        report[0] = 5;
        report[1] = 1;
        report[43] = 24;
        short[] native = [256, -512, 4096, 1640, -820, 410];
        for (int i = 0; i < native.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(49 + 2 * i), native[i]);
        Assert.IsTrue(session.TryProcess(descriptor, report.AsSpan(1), 50_000, out var input,
            out var error), error.ToString());
        Assert.IsTrue(Switch2ProProfileInputMapper.TryMap(input, out var profile, out var mapping),
            mapping.ToString());
        Assert.IsTrue(profile.HasCommonMotion);
        var state = new DS4State();
        var projection = new Switch2ProMotionProjection();
        Assert.IsTrue(projection.TryApply(profile, state, magnetometerYawAssistEnabled: false,
            virtualGyroSoftDeadzone: 0, horizonStabilizationEnabled: false));
        Assert.AreEqual(100d, state.Motion.angVelPitch, 0.0001);
        Assert.AreEqual(-50d, state.Motion.angVelRoll, 0.0001);
        Assert.AreEqual(-25d, state.Motion.angVelYaw, 0.0001);
        Assert.AreEqual(-1d, state.Motion.accelYG, 0.0001);

        state.L2 = 255;
        byte[] wire = ViiperStatePacketBuilder.Build(ViiperVirtualDeviceType.GameCube, state, -1);
        for (int i = 0; i < native.Length; i++)
            Assert.AreEqual(native[i], BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(12 + i * 2)));
        Assert.AreEqual((byte)232, wire[24]);
        Assert.AreEqual((byte)0, wire[25]);

        state.GyroStickGateOpen = false;
        wire = ViiperStatePacketBuilder.Build(ViiperVirtualDeviceType.GameCube, state, -1);
        Assert.AreEqual((short)4096, BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(16)));
        Assert.IsTrue(wire.AsSpan(18, 6).IndexOfAnyExcept((byte)0) < 0);
        Assert.AreEqual((byte)232, wire[24]);
    }

    [TestMethod]
    public void RumblePacketsAreSpecificToTransportAndCanStop()
    {
        byte[] usb = new byte[64];
        Switch2GameCubeRumble.WriteUsb(7, false, usb);
        CollectionAssert.AreEqual(new byte[] { 3, 0x57, 1 }, usb[..3]);
        Switch2GameCubeRumble.WriteUsb(8, true, usb);
        CollectionAssert.AreEqual(new byte[] { 3, 0x58, 2 }, usb[..3]);
        Assert.IsTrue(Switch2GameCubeRumble.IsValidUsbReport(Switch2ControllerModel.GameCube, usb));
        Assert.IsFalse(Switch2GameCubeRumble.IsValidUsbReport(Switch2ControllerModel.ProController2, usb));

        byte[] bluetooth = new byte[Switch2GameCubeRumble.BluetoothCommandLength];
        Switch2GameCubeRumble.WriteBluetooth(9, false, bluetooth);
        CollectionAssert.AreEqual(new byte[] { 0, 0x59, 1 }, bluetooth[..3]);
        Assert.IsTrue(bluetooth.AsSpan(3).IndexOfAnyExcept((byte)0) < 0);
        Switch2GameCubeRumble.WriteBluetooth(10, true, bluetooth);
        CollectionAssert.AreEqual(new byte[] { 0, 0x5A, 0 }, bluetooth[..3]);
        Assert.AreEqual(21, Switch2BluetoothHdRumblePhysicalWriter.PayloadLengthFor(Switch2ControllerModel.GameCube));
    }
}
