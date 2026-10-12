using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

/// <summary>
/// Bounded, offline audit of Sony feedback ownership. The USB writer is replaced
/// before any command is consumed; no device worker, HID handle, timer, or audio
/// endpoint is opened. These assertions are not radio/actuator latency evidence.
/// </summary>
[TestClass]
public sealed class SonyFeedbackBacklogAuditTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void UsbDualSenseAndEdgeRetainFullNativeBurstAndFinalStop(bool edge, bool rejectStopOnce)
    {
        DualSenseDevice device = CreateOfflineUsbDevice(edge);
        var expected = new List<byte[]>();
        var successful = new List<byte[]>();
        byte[] rejectedStop = null;
        device.PhysicalRawOutputWriteTestHook = report =>
        {
            if (rejectStopOnce && rejectedStop == null && IsStop(report))
            {
                rejectedStop = (byte[])report.Clone();
                return false;
            }
            successful.Add((byte[])report.Clone());
            return true;
        };

        long previousRevision = 0;
        for (int index = 0; index < 64; index++)
        {
            // Include A -> B -> A, then distinct complete two-trigger payloads.
            byte value = index < 3 ? (byte)(index == 1 ? 29 : 17) : (byte)(index + 1);
            byte[] command = NativeCommand(value);
            expected.Add((byte[])command.Clone());
            Assert.IsTrue(device.WriteRawOutputReportFromGame(command, 0, command.Length, out long revision));
            Assert.IsTrue(revision > previousRevision);
            previousRevision = revision;
            Array.Fill(command, (byte)0xEE); // The physical owner must own a copy.
        }

        byte[] stop = NativeCommand(0);
        Assert.IsFalse(device.WriteRawOutputReportFromGame(stop, 0, stop.Length, out long rejectedRevision));
        Assert.AreEqual(0L, rejectedRevision, "A full owner must reject before claiming the terminal command.");
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published,
            device.ProcessNextPhysicalOutputCommand());
        Assert.IsTrue(device.WriteRawOutputReportFromGame(stop, 0, stop.Length, out long stopRevision));
        Assert.IsTrue(stopRevision > previousRevision);
        expected.Add(stop);

        for (int index = 1; index < expected.Count; index++)
        {
            if (index == expected.Count - 1 && rejectStopOnce)
            {
                Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Retry,
                    device.ProcessNextPhysicalOutputCommand());
                Assert.AreEqual(expected.Count - 1, successful.Count,
                    "An unaccepted stop must remain the exact FIFO head.");
            }
            Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published,
                device.ProcessNextPhysicalOutputCommand());
        }
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.None,
            device.ProcessNextPhysicalOutputCommand());
        Assert.AreEqual(expected.Count, successful.Count);
        for (int index = 0; index < expected.Count; index++)
            AssertNativeActuators(expected[index], successful[index]);
        if (rejectStopOnce)
            AssertNativeActuators(rejectedStop, successful[^1]);
        Assert.IsTrue(IsStop(successful[^1]));
    }

    [TestMethod]
    public void Ds4LatestControlOverloadEndsAtNeutralWithoutHistoricalBacklog()
    {
        var buffer = new ViiperFeedbackDispatchBuffer(1, 16, 7);
        for (int index = 0; index < 4096; index++)
            Assert.IsTrue(buffer.QueueControl(new byte[] { 80, 160, 1, 2, 3, 0, 0 }, 7, 9, 2));
        byte[] stop = new byte[] { 0, 0, 1, 2, 3, 0, 0 };
        Assert.IsTrue(buffer.QueueControl(stop, stop.Length, 9, 2));
        byte[] output = new byte[7];
        Assert.IsTrue(buffer.TryTakeControl(output, out int length, out long generation, out int slot));
        Assert.AreEqual(7, length);
        Assert.AreEqual(9L, generation);
        Assert.AreEqual(2, slot);
        CollectionAssert.AreEqual(stop, output);
        Assert.IsFalse(buffer.TryTakeControl(output, out _, out _, out _));
    }

    [TestMethod]
    public void Ds4AudioMailboxOldCompletionCannotEraseNewerTerminalStop()
    {
        var mailbox = new DualShock4BluetoothEffectMailbox(78);
        byte[] active = Ds4AudioEffect(100);
        Assert.IsTrue(mailbox.TryPublish(active));
        byte[] claimed = new byte[78];
        Assert.IsTrue(mailbox.TryClaim(claimed, out _, out long oldVersion));
        for (int index = 0; index < 4096; index++)
            Assert.IsTrue(mailbox.TryPublish(active));
        byte[] stop = Ds4AudioEffect(0);
        Assert.IsTrue(mailbox.TryPublish(stop));
        mailbox.StopAccepting();
        Assert.IsFalse(mailbox.TryPublish(active));
        mailbox.Acknowledge(oldVersion);
        mailbox.Reject(oldVersion);
        Assert.IsTrue(mailbox.TryClaim(claimed, out int length, out long stopVersion));
        Assert.AreEqual(78, length);
        CollectionAssert.AreEqual(stop, claimed);
        mailbox.Reject(stopVersion);
        Assert.IsTrue(mailbox.TryClaim(claimed, out _, out long retryVersion));
        Assert.AreEqual(stopVersion, retryVersion);
        CollectionAssert.AreEqual(stop, claimed);
        mailbox.Acknowledge(retryVersion);
        Assert.IsFalse(mailbox.HasPending);
    }

    [TestMethod]
    public void CurrentDs4LatestValueSemanticsCanCoalesceAnUnconsumedShortPulse()
    {
        // Deliberately documents a remaining limitation, not a lossless pulse
        // guarantee: DS4 rumble is a latest-state lane, unlike native DS5 deltas.
        var mailbox = new DualShock4BluetoothEffectMailbox(78);
        Assert.IsTrue(mailbox.TryPublish(Ds4AudioEffect(120)));
        Assert.IsTrue(mailbox.TryPublish(Ds4AudioEffect(0)));
        byte[] output = new byte[78];
        Assert.IsTrue(mailbox.TryClaim(output, out _, out long version));
        Assert.AreEqual((byte)0, output[6]);
        Assert.AreEqual((byte)0, output[7]);
        mailbox.Acknowledge(version);
        Assert.IsFalse(mailbox.HasPending);
    }

    [TestMethod]
    public void EdgeAndDualSenseUseSameBoundedSpeakerDispatchPolicy()
    {
        Assert.AreEqual(ViiperOutDevice.GetFeedbackSpeakerQueueCapacity(ViiperVirtualDeviceType.DualSense),
            ViiperOutDevice.GetFeedbackSpeakerQueueCapacity(ViiperVirtualDeviceType.DualSenseEdge));
        Assert.AreEqual(ViiperOutDevice.GetFeedbackSpeakerMaximumAgeMilliseconds(ViiperVirtualDeviceType.DualSense),
            ViiperOutDevice.GetFeedbackSpeakerMaximumAgeMilliseconds(ViiperVirtualDeviceType.DualSenseEdge));
        Assert.AreEqual(ViiperOutDevice.GetVirtualSpeakerPcmSampleRate(ViiperVirtualDeviceType.DualSense),
            ViiperOutDevice.GetVirtualSpeakerPcmSampleRate(ViiperVirtualDeviceType.DualSenseEdge));
    }

    private static DualSenseDevice CreateOfflineUsbDevice(bool edge)
    {
        var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
        var device = new DualSenseDevice(hid, "Offline Sony feedback backlog audit");
        typeof(DS4Device).GetField("conType", Fields)!.SetValue(device, ConnectionType.USB);
        typeof(DS4Device).GetField("outputReport", Fields)!.SetValue(device, new byte[48]);
        typeof(DualSenseDevice).GetField("subType", Fields)!.SetValue(device,
            edge ? DualSenseDevice.DeviceSubType.DSEdge : DualSenseDevice.DeviceSubType.DualSense);
        Assert.AreEqual(edge ? DualSenseDevice.DeviceSubType.DSEdge : DualSenseDevice.DeviceSubType.DualSense,
            device.SubType);
        return device;
    }

    private static byte[] NativeCommand(byte value)
    {
        byte[] report = new byte[48];
        report[0] = 0x02;
        report[1] = 0x0F;
        report[3] = value;
        report[4] = value;
        for (int offset = 11; offset < 33; offset++)
            report[offset] = value;
        report[11] = report[22] = value == 0 ? (byte)0x05 : (byte)0x21;
        return report;
    }

    private static void AssertNativeActuators(byte[] expected, byte[] actual)
    {
        Assert.AreEqual(expected[1] & 0x0F, actual[1] & 0x0F);
        Assert.AreEqual(expected[3], actual[3]);
        Assert.AreEqual(expected[4], actual[4]);
        CollectionAssert.AreEqual(expected[11..33], actual[11..33],
            "Both complete trigger blocks, including stop, must remain ordered and byte-exact.");
    }

    private static bool IsStop(byte[] report) => report[3] == 0 && report[4] == 0 &&
        report[11] == 0x05 && report[22] == 0x05;

    private static byte[] Ds4AudioEffect(byte strength)
    {
        byte[] report = new byte[78];
        DualShock4BluetoothAudioProtocol.WriteAudioControlReport(report, true, false,
            60, 50, 40, strength, strength, 1, 2, 3, 0, 0);
        return report;
    }
}
