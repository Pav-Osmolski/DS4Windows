using System.Buffers.Binary;
using System.Reflection;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public class Ds4CompatibleGyroCalibrationTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ReversedEndpointsPreserveEveryGyroDirection(bool usb)
    {
        for (int axis = 0; axis < 3; axis++)
        foreach (bool reversed in new[] { false, true })
        foreach (int raw in new[] { -1600, 1600 })
        {
            var motion = new DS4SixAxis();
            byte[] report = Calibration(usb, axis, reversed);
            Assert.IsTrue(motion.TrySetDs4CalibrationData(report, usb, out string diagnostic), diagnostic);
            int[] values = Apply(motion, raw, raw, raw);
            CollectionAssert.AreEqual(new[] { raw, raw, raw, 0, 0, 8192 }, values);
        }
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ValidBiasedCalibrationMatchesLegacyScaling(bool usb)
    {
        byte[] report = Calibration(usb, 0, false);
        Put(report, 1, 17); Put(report, 3, -23); Put(report, 5, 31);
        var legacy = new DS4SixAxis(); legacy.setCalibrationData(ref report, usb);
        var corrected = new DS4SixAxis();
        Assert.IsTrue(corrected.TrySetDs4CalibrationData(report, usb, out _));
        CollectionAssert.AreEqual(Apply(legacy, -1600, 1700, -1800), Apply(corrected, -1600, 1700, -1800));
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public void InvalidAxisDoesNotReplacePreviouslyAcceptedCalibration(int axis)
    {
        var motion = new DS4SixAxis();
        byte[] good = Calibration(true, 0, false); Put(good, 1, 17);
        Assert.IsTrue(motion.TrySetDs4CalibrationData(good, true, out _));
        int[] before = Apply(motion, 1600, 1600, 1600);
        byte[] invalid = Calibration(true, 0, false);
        int plus = axis < 3 ? 7 + axis * 4 : 23 + (axis - 3) * 4;
        Put(invalid, plus, 100); Put(invalid, plus + 2, 100);
        Assert.IsFalse(motion.TrySetDs4CalibrationData(invalid, true, out _));
        CollectionAssert.AreEqual(before, Apply(motion, 1600, 1600, 1600));
    }

    [TestMethod]
    [DataRow(1, 2000)] [DataRow(19, 0)] [DataRow(19, -1000)] [DataRow(7, 100)]
    public void ImplausibleCalibrationIsRejected(int offset, int value)
    {
        byte[] report = Calibration(true, 0, false);
        Put(report, offset, value);
        if (offset == 19) Put(report, 21, value);
        if (offset == 7) Put(report, 9, -100);
        Assert.IsFalse(new DS4SixAxis().TrySetDs4CalibrationData(report, true, out _));
    }

    [TestMethod]
    public void MissingAndShortReportsAreRejected()
    {
        var motion = new DS4SixAxis();
        Assert.IsFalse(motion.TrySetDs4CalibrationData(null, false, out _));
        Assert.IsFalse(motion.TrySetDs4CalibrationData(new byte[34], false, out _));
        Assert.IsFalse(motion.TrySetDs4CalibrationData(new byte[41], false, out _));
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ReadFailuresAndWrongReportIdsAreNotAccepted(bool bluetooth)
    {
        int count = 0;
        Assert.IsFalse(DS4Device.TryReadDs4Calibration(bluetooth, data => { count++; return false; }, out _, out _));
        Assert.AreEqual(bluetooth ? 5 : 1, count);
        Assert.IsFalse(DS4Device.TryReadDs4Calibration(bluetooth, data =>
        { Calibration(!bluetooth, 0, false).CopyTo(data, 0); data[0] = 0x20; return true; }, out _, out _));
    }

    [TestMethod]
    public void BluetoothCorruptionRetriesThenAcceptsOnlyValidReport()
    {
        int count = 0;
        Assert.IsTrue(DS4Device.TryReadDs4Calibration(true, data =>
        {
            Assert.AreEqual((byte)5, data[0]);
            Calibration(false, 1, true).CopyTo(data, 0);
            if (++count < 3) data[40] ^= 1;
            return true;
        }, out byte[] accepted, out _));
        Assert.AreEqual(3, count);
        Assert.IsTrue(new DS4SixAxis().TrySetDs4CalibrationData(accepted, false, out _));
        count = 0;
        Assert.IsFalse(DS4Device.TryReadDs4Calibration(true, data =>
        { count++; Calibration(false, 1, true).CopyTo(data, 0); data[40] ^= 1; return true; }, out _, out _));
        Assert.AreEqual(5, count);
    }

    private static byte[] Calibration(bool usb, int reversedAxis, bool reversed)
    {
        byte[] report = new byte[41]; report[0] = usb ? (byte)2 : (byte)5;
        for (int axis = 0; axis < 3; axis++)
        {
            int plus = usb ? 7 + axis * 4 : 7 + axis * 2;
            int minus = usb ? plus + 2 : plus + 6;
            bool swap = reversed && reversedAxis == axis;
            Put(report, plus, swap ? -16000 : 16000); Put(report, minus, swap ? 16000 : -16000);
            Put(report, 23 + axis * 4, 8192); Put(report, 25 + axis * 4, -8192);
        }
        Put(report, 19, 1000); Put(report, 21, 1000);
        byte[] prefix = new byte[38]; prefix[0] = 0xA3;
        report.AsSpan(0, 37).CopyTo(prefix.AsSpan(1));
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(37), Crc32Algorithm.Compute(prefix));
        return report;
    }

    private static void Put(byte[] report, int offset, int value)
        => BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(offset), (short)value);

    private static int[] Apply(DS4SixAxis motion, int yaw, int pitch, int roll)
    {
        object[] values = { yaw, pitch, roll, 0, 0, 8192 };
        typeof(DS4SixAxis).GetMethod("applyCalibs", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(motion, values);
        return values.Cast<int>().ToArray();
    }
}
