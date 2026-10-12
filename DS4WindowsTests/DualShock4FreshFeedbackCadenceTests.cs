using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF.DS4Control;

namespace DS4WindowsTests;

[TestClass]
public sealed class DualShock4FreshFeedbackCadenceTests
{
    [ClassInitialize]
    public static void InitializeCrcTable(TestContext context) =>
        Crc32Algorithm.InitializeTable(DS4Device.DefaultPolynomial);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ChangedMotorsAndFinalZeroReachAudioMailboxInsideMaintenanceWindow(bool microphone)
    {
        var device = new AudioDevice(microphone);
        device.setRumble(30, 60);
        Assert.IsTrue(device.Pump());
        Assert.AreEqual(1, device.Admitted.Count);

        device.setRumble(31, 60);
        Assert.IsTrue(device.PumpInsideMaintenanceWindow());
        Assert.AreEqual(2, device.Admitted.Count, "A fresh light-motor change must not wait for the LED maintenance cadence.");
        device.setRumble(31, 61);
        Assert.IsTrue(device.PumpInsideMaintenanceWindow());
        Assert.AreEqual(3, device.Admitted.Count, "The heavy motor has the same immediate publication rule.");
        device.setRumble(0, 0);
        Assert.IsTrue(device.PumpInsideMaintenanceWindow());
        Assert.AreEqual(4, device.Admitted.Count, "A terminal game stop must not wait for maintenance.");
        AssertMotors(device.Admitted[^1], 0, 0);
        Assert.AreEqual(microphone ? (byte)0xA1 : (byte)0xA0, device.Admitted[^1][2]);
    }

    [TestMethod]
    public void UnchangedMotorRepeatsAndLedChangesStillUseMaintenanceCadence()
    {
        var device = new AudioDevice(false);
        device.setRumble(20, 40);
        Assert.IsTrue(device.Pump());
        for (int index = 0; index < 256; index++)
        {
            device.setRumble(20, 40); // Deliberately creates a new generation.
            Assert.IsTrue(device.PumpInsideMaintenanceWindow());
        }
        device.LightBarColor = new DS4Color(170, 90, 30);
        Assert.IsTrue(device.PumpInsideMaintenanceWindow());
        Assert.AreEqual(1, device.Admitted.Count,
            "Repeated publications and LED-only updates are not fresh motor changes.");
        Assert.IsTrue(device.PumpAfterMaintenanceWindow());
        Assert.AreEqual(2, device.Admitted.Count);
        AssertMotors(device.Admitted[^1], 20, 40);
        Assert.AreEqual((byte)170, device.Admitted[^1][8]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RejectedOrThrowingAdmissionRetainsFreshChangeForNextPass(bool throwOnRejection)
    {
        var device = new AudioDevice(false);
        device.setRumble(70, 140);
        Assert.IsTrue(device.Pump());
        device.Accept = false;
        device.ThrowOnRejection = throwOnRejection;
        device.setRumble(0, 0);
        Assert.IsFalse(device.PumpInsideMaintenanceWindow());
        Assert.AreEqual(1, device.Admitted.Count);
        Assert.AreEqual(2, device.Attempts.Count, "The fresh stop must be offered despite the maintenance deadline.");
        device.Accept = true;
        Assert.IsTrue(device.PumpInsideMaintenanceWindow());
        Assert.AreEqual(2, device.Admitted.Count);
        CollectionAssert.AreEqual(device.Attempts[^2], device.Admitted[^1],
            "A failed publication must not advance the admitted motor snapshot.");
        AssertMotors(device.Admitted[^1], 0, 0);
    }

    [TestMethod]
    public void RejectedChangedValueCanReturnToTheAlreadyAdmittedStateWithoutBurst()
    {
        var device = new AudioDevice(false);
        device.setRumble(40, 80);
        Assert.IsTrue(device.Pump());
        device.Accept = false;
        device.setRumble(50, 100);
        Assert.IsFalse(device.PumpInsideMaintenanceWindow());
        device.Accept = true;
        device.setRumble(40, 80);
        Assert.IsTrue(device.PumpInsideMaintenanceWindow());
        Assert.AreEqual(1, device.Admitted.Count,
            "Compare admitted motor bytes, not attempted bytes or command generations.");
        Assert.AreEqual(2, device.Attempts.Count);
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(6)]
    public void MotorComparisonIgnoresOtherBytesAndDetectsEachMotorAndStop(int motorOffset)
    {
        byte[] prepared = new byte[motorOffset + 2];
        byte[] admitted = new byte[prepared.Length];
        prepared[0] = 0x11;
        Assert.IsFalse(DS4Device.HasChangedDualShock4RumbleState(prepared, admitted, motorOffset));
        prepared[motorOffset] = 25;
        Assert.IsTrue(DS4Device.HasChangedDualShock4RumbleState(prepared, admitted, motorOffset));
        admitted[motorOffset] = 25;
        Assert.IsFalse(DS4Device.HasChangedDualShock4RumbleState(prepared, admitted, motorOffset));
        prepared[motorOffset + 1] = 50;
        Assert.IsTrue(DS4Device.HasChangedDualShock4RumbleState(prepared, admitted, motorOffset));
        admitted[motorOffset + 1] = 50;
        prepared[motorOffset] = prepared[motorOffset + 1] = 0;
        Assert.IsTrue(DS4Device.HasChangedDualShock4RumbleState(prepared, admitted, motorOffset));
    }

    [TestMethod]
    [DataRow(8, 8, -1)]
    [DataRow(8, 8, int.MaxValue)]
    [DataRow(0, 8, 0)]
    [DataRow(8, 0, 0)]
    [DataRow(7, 8, 6)]
    [DataRow(8, 7, 6)]
    public void ShortOrInvalidMotorViewsNeverBypassCadence(int preparedLength, int admittedLength, int offset)
    {
        Assert.IsFalse(DS4Device.HasChangedDualShock4RumbleState(
            new byte[preparedLength], new byte[admittedLength], offset));
    }

    private static void AssertMotors(byte[] report, byte light, byte heavy)
    {
        Assert.AreEqual(light, report[6]);
        Assert.AreEqual(heavy, report[7]);
        Assert.AreEqual(1, report[3] & 1);
        Assert.AreEqual(DualShock4BluetoothAudioProtocol.ComputeBluetoothCrc(0xA2, report, 74),
            BitConverter.ToUInt32(report, 74));
    }

    private sealed class AudioDevice : DS4Device
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo LastAdmissionTick = typeof(DS4Device).GetField(
            "lastBluetoothEffectReportDuringAudioTick", Fields)!;
        private readonly Func<bool, bool, bool, bool> send;
        internal readonly List<byte[]> Attempts = new();
        internal readonly List<byte[]> Admitted = new();
        internal bool Accept = true;
        internal bool ThrowOnRejection;

        internal AudioDevice(bool microphone)
            : base((HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice)),
                "Offline DS4 audio effect cadence test")
        {
            deviceType = InputDeviceType.DS4;
            conType = ConnectionType.BT;
            outReportBuffer = new byte[78];
            outputReport = new byte[78];
            typeof(DS4Device).GetField("btOutputPayloadLen", Fields)!.SetValue(this, 78);
            send = typeof(DS4Device).GetMethod("sendOutputReport", Fields)!
                .CreateDelegate<Func<bool, bool, bool, bool>>(this);
            var audio = (DualShock4BluetoothAudioState)typeof(DS4Device)
                .GetField("bluetoothAudioState", Fields)!.GetValue(this)!;
            Assert.IsTrue(audio.Update(true, microphone, 60, 50, 40, null));
            Assert.IsTrue(RegisterDualShock4BluetoothAudioControlLane(this, Record, Record));
        }

        internal bool Pump() => send(true, false, false);
        internal bool PumpInsideMaintenanceWindow()
        {
            // The production calculation clamps a future admission tick to
            // elapsed zero. Pin that branch without sleeping or trusting JIT
            // speed, while still exercising the actual non-forced compositor.
            LastAdmissionTick.SetValue(this, Environment.TickCount64 + 60_000L);
            return Pump();
        }
        internal bool PumpAfterMaintenanceWindow()
        {
            LastAdmissionTick.SetValue(this,
                Environment.TickCount64 - BLUETOOTH_EFFECT_INTERVAL_DURING_SPEAKER_MS - 1L);
            return Pump();
        }
        protected override bool writeOutput() => throw new AssertFailedException(
            "Audio effects must use the mailbox; this test never opens a HID writer.");
        private bool Record(byte[] report)
        {
            Attempts.Add((byte[])report.Clone());
            if (!Accept)
            {
                if (ThrowOnRejection) throw new IOException("Injected mailbox owner retirement.");
                return false;
            }
            Admitted.Add((byte[])report.Clone());
            return true;
        }
    }
}
