using System;
using System.Buffers.Binary;
using DS4Windows;
namespace DS4WindowsTests;
[TestClass]
public class BluetoothInputReportFallbackTests
{
    [TestMethod]
    [DataRow(0x05C4, 0x11)]
    [DataRow(0x09CC, 0x11)]
    [DataRow(0x0CE6, 0x31)]
    [DataRow(0x0DF2, 0x31)]
    public void RecoversCurrentStateThenRejectsQueuedOlderReports(int product, int id)
    {
        var current = Report((byte)id, 500);
        var fallback = Create(product, (byte)id, current);
        Assert.IsTrue(fallback.TryRecover(out var recovered));
        Assert.AreEqual(current[1], recovered[1]);
        Assert.IsFalse(fallback.AcceptStreamingReport(Report((byte)id, 490)));
        Assert.IsFalse(fallback.AcceptStreamingReport(Report((byte)id, 500)));
        Assert.IsFalse(fallback.TryRecover(out _), "Cached state must not be replayed.");
        Assert.IsTrue(fallback.AcceptStreamingReport(Report((byte)id, 501)));
        Assert.IsTrue(fallback.AcceptStreamingReport(Report((byte)id, 502)));
        Assert.AreEqual(1L, fallback.RecoveredReports);
    }

    [TestMethod]
    [DataRow(0x05C4, 0x11)]
    [DataRow(0x0CE6, 0x31)]
    public void RecoveryHandlesControllerClockWrap(int product, int id)
    {
        uint end = id == 0x31 ? uint.MaxValue - 2 : ushort.MaxValue - 2u;
        var current = Report((byte)id, end);
        var fallback = Create(product, (byte)id, current);
        Assert.IsTrue(fallback.TryRecover(out _));
        Assert.IsTrue(fallback.AcceptStreamingReport(Report((byte)id, 3)));
    }

    [TestMethod]
    [DataRow(ConnectionType.USB, 0x054C, 0x0CE6)]
    [DataRow(ConnectionType.SONYWA, 0x054C, 0x0CE6)]
    [DataRow(ConnectionType.BT, 0x1234, 0x0CE6)]
    [DataRow(ConnectionType.BT, 0x054C, 0x1234)]
    public void DoesNotQueryUsbOrUnsupportedHardware(ConnectionType connection, int vendor, int product)
    {
        var fallback = new BluetoothInputReportFallback(vendor, product, connection, 78, 0x31,
            (_, _) => throw new AssertFailedException("Unexpected current-state query."));
        Assert.IsFalse(fallback.Enabled);
        Assert.IsFalse(fallback.TryRecover(out _));
    }

    [TestMethod]
    [DataRow(HidDevice.ReadStatus.ReadError)]
    [DataRow(HidDevice.ReadStatus.WaitTimedOut)]
    public void FailedQueryDisablesFurtherQueriesWithoutChangingNormalStream(HidDevice.ReadStatus result)
    {
        int calls = 0;
        var fallback = new BluetoothInputReportFallback(0x054C, 0x0CE6, ConnectionType.BT, 78, 0x31,
            (_, timeout) => { Assert.AreEqual(32u, timeout); calls++; return result; });
        Assert.IsFalse(fallback.TryRecover(out _));
        Assert.IsFalse(fallback.Enabled);
        Assert.IsFalse(fallback.TryRecover(out _));
        Assert.AreEqual(1, calls);
        Assert.IsTrue(fallback.AcceptStreamingReport(Report(0x31, 1)));
    }

    [TestMethod]
    [DataRow(0x11)]
    [DataRow(0x31)]
    public void CorruptCurrentReportCannotReachMapping(int id)
    {
        var report = Report((byte)id, 100);
        report[20] ^= 1;
        var fallback = Create(id == 0x11 ? 0x05C4 : 0x0CE6, (byte)id, report);
        Assert.IsFalse(fallback.TryRecover(out _));
        Assert.AreEqual(0L, fallback.RecoveredReports);
    }

    [TestMethod]
    public void MediaOnlyDualSenseReportCannotReplaceControllerInput()
    {
        var report = Report(0x31, 10);
        report[1] = 2;
        Seal(report, 78);
        Assert.IsFalse(Create(0x0CE6, 0x31, report).TryRecover(out _));
    }

    [TestMethod]
    public void Ds4AudioHidStreamCanResumeAfterBasicReportFallback()
    {
        var fallback = Create(0x09CC, 0x11, Report(0x11, 100));
        Assert.IsTrue(fallback.TryRecover(out _));
        var audioReport = new byte[547];
        audioReport[0] = 0x12;
        audioReport[1] = 0xC0;
        BinaryPrimitives.WriteUInt16LittleEndian(audioReport.AsSpan(12), 101);
        Seal(audioReport, DualShock4BluetoothAudioProtocol.GetInputReportLength(0x12));
        Assert.IsTrue(fallback.AcceptStreamingReport(audioReport));
    }

    private static BluetoothInputReportFallback Create(int product, byte id, byte[] report) =>
        new(0x054C, product, ConnectionType.BT, id == 0x31 ? 78 : 547, id,
            (buffer, timeout) => {
                Assert.AreEqual(32u, timeout);
                Assert.AreEqual(id, buffer[0]);
                report.CopyTo(buffer, 0);
                return HidDevice.ReadStatus.Success;
            });
    private static byte[] Report(byte id, uint timestamp)
    {
        var report = new byte[id == 0x31 ? 78 : 547];
        report[0] = id;
        report[1] = id == 0x31 ? (byte)1 : (byte)0xC0;
        if (id == 0x31) BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(29), timestamp);
        else BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(12), (ushort)timestamp);
        Seal(report, 78);
        return report;
    }
    private static void Seal(byte[] report, int length) =>
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(length - 4),
            DualShock4BluetoothAudioProtocol.ComputeBluetoothCrc(0xA1, report, length - 4));
}
