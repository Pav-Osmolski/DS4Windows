using System;
using System.Buffers.Binary;
namespace DS4Windows;

// Streaming remains authoritative. A timed-out Sony BT stream can still expose
// its current full HID report while a Windows gaming overlay is open. This is a current-state fallback, not an event-history replacement.
internal sealed class BluetoothInputReportFallback
{
    internal const uint StreamingWaitMilliseconds = 16;
    internal const uint QueryWaitMilliseconds = 32;
    private readonly Func<byte[], uint, HidDevice.ReadStatus> query;
    private readonly byte[] buffer;
    private readonly byte reportId;
    private bool recovering;
    private uint watermark;
    internal bool Enabled { get; private set; }
    internal long RecoveredReports { get; private set; }

    internal BluetoothInputReportFallback(HidDevice device, ConnectionType connection,
        int reportLength, byte reportId) : this(device.Attributes.VendorId,
            device.Attributes.ProductId, connection, reportLength, reportId,
            device.ReadCurrentInputReport) { }

    internal BluetoothInputReportFallback(int vendor, int product, ConnectionType connection,
        int reportLength, byte reportId, Func<byte[], uint, HidDevice.ReadStatus> query)
    {
        Enabled = connection == ConnectionType.BT && vendor == 0x054C &&
            ((reportId == 0x31 && (product == 0x0CE6 || product == 0x0DF2)) ||
             (reportId == 0x11 && (product == 0x05C4 || product == 0x09CC)));
        this.reportId = reportId;
        this.query = query;
        buffer = new byte[reportLength];
    }

    internal bool TryRecover(out byte[] report)
    {
        report = null;
        if (!Enabled) return false;
        Array.Clear(buffer);
        buffer[0] = reportId;
        if (query(buffer, QueryWaitMilliseconds) != HidDevice.ReadStatus.Success ||
            !Validate(buffer, reportId))
        {
            // Unsupported or unhealthy current-state requests are not retried
            // for the rest of this physical connection. Preserve normal reads.
            Enabled = false;
            return false;
        }
        uint timestamp = Timestamp(buffer);
        if (recovering && !IsNewer(timestamp, watermark)) return false;
        watermark = timestamp;
        recovering = true;
        RecoveredReports++;
        report = buffer;
        return true;
    }

    internal bool AcceptStreamingReport(byte[] report)
    {
        if (!recovering) return true;
        bool valid = reportId == 0x31 ? Validate(report, reportId) :
            report != null && DualShock4BluetoothAudioProtocol.HasHidState(report) &&
            DualShock4BluetoothAudioProtocol.ValidateInputReportCrc(report,
                DualShock4BluetoothAudioProtocol.GetInputReportLength(report[0]));
        if (!valid) return false;
        uint timestamp = Timestamp(report);
        // Drain queued frames older than the state already published by the
        // query path. Otherwise recovery can replay button presses/mouse deltas.
        if (!IsNewer(timestamp, watermark)) return false;
        recovering = false;
        return true;
    }

    private uint Timestamp(byte[] report) => reportId == 0x31 ?
        BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(29, 4)) :
        BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(12, 2));

    private bool IsNewer(uint value, uint previous) => reportId == 0x31 ?
        unchecked((int)(value - previous)) > 0 : unchecked((short)(value - previous)) > 0;

    internal static bool Validate(byte[] report, byte reportId)
    {
        if (report == null || report.Length < 78 || report[0] != reportId ||
            (reportId == 0x31 && ((report[1] & 0x03) != 0x01)) ||
            (reportId == 0x11 && !DualShock4BluetoothAudioProtocol.HasHidState(report))) return false;
        return DualShock4BluetoothAudioProtocol.ValidateInputReportCrc(report, 78);
    }
}
