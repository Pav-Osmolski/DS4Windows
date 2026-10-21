using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

// Characterizes the actual legacy DS3 compositor, not its driver or actuator.
// No constructor/PostInit, worker, HID read/write, sleep, or controller is used.
[TestClass]
public sealed class Ds3FeedbackBacklogAuditTests
{
    [TestMethod]
    [DataRow(ConnectionType.USB)]
    [DataRow(ConnectionType.BT)]
    public void BatteryAlertPulseUsesRealDs3PacketsAndRestoresNewerGameFeedback(ConnectionType transport)
    {
        var device = Create(transport);
        using var lease = device.TryBeginBatteryAlertRumble(80);
        Assert.IsNotNull(lease);
        var active = Compose(device);
        Assert.AreEqual((byte)1, active[6]); Assert.AreEqual((byte)80, active[8]);
        device.setRumble(23, 191);
        lease.Dispose();
        var restored = Compose(device);
        Assert.AreEqual((byte)1, restored[6]); Assert.AreEqual((byte)191, restored[8]);
        Assert.IsNull(device.TryBeginBatteryAlertRumble(80), "A battery pulse cannot take over active game rumble.");
    }

    [TestMethod]
    [DataRow(ConnectionType.USB)]
    [DataRow(ConnectionType.BT)]
    public void FloodReachesTheNewestMotorStateOnTheNextComposition(ConnectionType transport)
    {
        DS3Device device = Create(transport);
        for (int index = 0; index < 10_000; index++)
            device.setRumble((byte)(index % 255 + 1), (byte)(index % 253 + 1));
        device.setRumble(23, 191);

        byte[] report = Compose(device);
        Assert.AreEqual((byte)1, report[6], "DS3's small motor is on/off, not an amplitude channel.");
        Assert.AreEqual((byte)191, report[8], "No old motor-state FIFO must drain first.");
    }

    [TestMethod]
    [DataRow(ConnectionType.USB)]
    [DataRow(ConnectionType.BT)]
    public void FinalZeroSupersedesAFloodWithoutReplayingOldStates(ConnectionType transport)
    {
        DS3Device device = Create(transport);
        device.setRumble(80, 170);
        Assert.AreEqual((byte)170, Compose(device)[8]);
        for (int index = 0; index < 10_000; index++)
            device.setRumble((byte)(index % 255 + 1), (byte)(index % 253 + 1));
        device.setRumble(0, 0);

        for (int pass = 0; pass < 32; pass++)
        {
            byte[] report = Compose(device);
            Assert.AreEqual((byte)0, report[6]);
            Assert.AreEqual((byte)0, report[8]);
        }
    }

    [TestMethod]
    [DataRow(ConnectionType.USB)]
    [DataRow(ConnectionType.BT)]
    public void UnconsumedShortPulseIsCoalescedToItsFinalStop(ConnectionType transport)
    {
        DS3Device device = Create(transport);
        device.setRumble(90, 180);
        device.setRumble(0, 0);

        byte[] report = Compose(device);
        Assert.AreEqual((byte)0, report[6]);
        Assert.AreEqual((byte)0, report[8]);
        // This is an observed state-mailbox tradeoff, not proof that every
        // rapid pulse was rendered. A future event-preserving design must
        // deliberately revisit this characterization rather than claim it
        // already preserves commands it never presented to the writer.
    }

    [TestMethod]
    [DataRow(ConnectionType.USB)]
    [DataRow(ConnectionType.BT)]
    public void FailedFinalNeutralIsRetriedWithoutANewRumblePublication(ConnectionType transport)
    {
        DS3Device device = Create(transport);
        var writes = new List<byte[]>();
        device.PhysicalOutputWriteTestHook = report =>
        {
            writes.Add((byte[])report.Clone());
            return writes.Count != 2;
        };
        device.setRumble(80, 170);
        Assert.IsTrue(device.ProcessPendingOutputReport());
        device.setRumble(0, 0);
        Assert.IsFalse(device.ProcessPendingOutputReport());
        Assert.IsTrue(device.ProcessPendingOutputReport());
        Assert.AreEqual(3, writes.Count, "A failed stop cannot be acknowledged as delivered.");
        CollectionAssert.AreEqual(writes[1], writes[2]);
        Assert.AreEqual((byte)0, writes[2][6]);
        Assert.AreEqual((byte)0, writes[2][8]);
        Assert.IsTrue(device.ProcessPendingOutputReport());
        Assert.AreEqual(3, writes.Count, "Once accepted, an unchanged stop must not be replayed.");
    }

    [TestMethod]
    [DataRow(ConnectionType.USB)]
    [DataRow(ConnectionType.BT)]
    public void NewestStopSupersedesAnUnacceptedActiveState(ConnectionType transport)
    {
        DS3Device device = Create(transport);
        var writes = new List<byte[]>();
        device.PhysicalOutputWriteTestHook = report =>
        {
            writes.Add((byte[])report.Clone());
            return writes.Count != 1;
        };
        device.setRumble(80, 170);
        Assert.IsFalse(device.ProcessPendingOutputReport());
        device.setRumble(0, 0);
        Assert.IsTrue(device.ProcessPendingOutputReport());
        Assert.AreEqual(2, writes.Count);
        Assert.AreEqual((byte)0, writes[1][6]);
        Assert.AreEqual((byte)0, writes[1][8]);
        Assert.IsTrue(device.ProcessPendingOutputReport());
        Assert.AreEqual(2, writes.Count);
    }

    private static DS3Device Create(ConnectionType transport)
    {
        DS3Device device = (DS3Device)RuntimeHelpers.GetUninitializedObject(typeof(DS3Device));
        Field("rumbleStateLock").SetValue(device, new object());
        Field("standbySw").SetValue(device, new Stopwatch());
        Field("outputReport").SetValue(device, new byte[64]);
        Field("deviceType").SetValue(device, InputDeviceType.DS3);
        Field("conType").SetValue(device, transport);
        return device;
    }

    private static byte[] Compose(DS3Device device)
    {
        typeof(DS3Device).GetMethod("PrepareOutReport", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(device, null);
        return (byte[])Field("outputReport").GetValue(device)!;
    }

    private static FieldInfo Field(string name) =>
        typeof(DS4Device).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
}
