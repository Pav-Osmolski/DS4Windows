using System.IO;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public sealed class DualSenseGyroDeadbandTests
{
    [TestMethod]
    [DataRow("", 32)]
    [DataRow("<DualSenseGyroDeadband>0</DualSenseGyroDeadband>", 0)]
    [DataRow("<DualSenseGyroDeadband>64</DualSenseGyroDeadband>", 64)]
    [DataRow("<DualSenseGyroDeadband>-1</DualSenseGyroDeadband>", 0)]
    [DataRow("<DualSenseGyroDeadband>999</DualSenseGyroDeadband>", 256)]
    public void ProfileStoresDeadbandAndPreservesTheDefaultForOldFiles(string elements, int expected)
    {
        using var reader = new StringReader("<DS4Windows>" + elements + "</DS4Windows>");
        var loaded = (ProfileDTO)ProfileDTO.Serializer.Deserialize(reader)!;
        loaded.DeviceIndex = 0;
        var store = new BackingStore();
        loaded.MapTo(store);
        Assert.AreEqual(expected, store.dualSenseGyroDeadbands[0]);
        Assert.AreEqual(32, store.dualSenseGyroDeadbands[1]);

        var saved = new ProfileDTO { DeviceIndex = 0 };
        saved.MapFrom(store);
        using var writer = new StringWriter();
        ProfileDTO.Serializer.Serialize(writer, saved);
        using var savedXml = new StringReader(writer.ToString());
        var reloaded = (ProfileDTO)ProfileDTO.Serializer.Deserialize(savedXml)!;
        Assert.AreEqual(expected, reloaded.DualSenseGyroDeadband);
    }

    [TestMethod]
    [DataRow(32, 32, GyroOutMode.Passthru, ViiperVirtualDeviceType.DualSense, true, 0)]
    [DataRow(32, 33, GyroOutMode.Passthru, ViiperVirtualDeviceType.DualSense, true, 33)]
    [DataRow(0, 1, GyroOutMode.Passthru, ViiperVirtualDeviceType.DualSense, true, 1)]
    [DataRow(64, 40, GyroOutMode.Passthru, ViiperVirtualDeviceType.DualSense, true, 0)]
    [DataRow(0, 1, GyroOutMode.Passthru, ViiperVirtualDeviceType.DualSense, false, 0)]
    [DataRow(0, 1, GyroOutMode.Mouse, ViiperVirtualDeviceType.DualSense, true, 0)]
    [DataRow(0, 1, GyroOutMode.Passthru, ViiperVirtualDeviceType.DualSenseEdge, true, 0)]
    [DataRow(64, 1, GyroOutMode.Passthru, ViiperVirtualDeviceType.DualShock4, true, 1)]
    public void DeadbandAppliesOnlyToVirtualDualSensePassthru(int deadband, int reading, GyroOutMode mode,
        ViiperVirtualDeviceType output, bool gateOpen, int expected)
    {
        const int slot = 0;
        int previousDeadband = Global.DualSenseGyroDeadbands[slot];
        GyroOutMode previousMode = Global.GyroOutputMode[slot];
        try
        {
            Global.DualSenseGyroDeadbands[slot] = deadband;
            Global.GyroOutputMode[slot] = mode;
            var state = new DS4State { GyroStickGateOpen = gateOpen };
            state.Motion.gyroPitchFull = reading;
            state.Motion.gyroYawFull = reading;
            state.Motion.gyroRollFull = -reading;
            state.Motion.accelZFull = 777;

            byte[] packet = ViiperStatePacketBuilder.Build(output, state, slot);
            int offset = output == ViiperVirtualDeviceType.DualShock4 ? 19 : 21;
            Assert.AreEqual((short)expected, BitConverter.ToInt16(packet, offset));
            Assert.AreEqual((short)-expected, BitConverter.ToInt16(packet, offset + 2));
            Assert.AreEqual((short)expected, BitConverter.ToInt16(packet, offset + 4));
            Assert.AreEqual((short)777, BitConverter.ToInt16(packet, offset + 10));
        }
        finally
        {
            Global.DualSenseGyroDeadbands[slot] = previousDeadband;
            Global.GyroOutputMode[slot] = previousMode;
        }
    }
}
