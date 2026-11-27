using System;

namespace DS4Windows.Switch2;

internal static class Switch2GameCubeRumble
{
    internal const int BluetoothCommandLength = 21;

    internal static void WriteBluetooth(byte counter, bool stop, Span<byte> destination)
    {
        // Native motor report on the GameCube rumble + command characteristic.
        // The separate basic command channel plays firmware vibration clips.
        destination.Clear();
        destination[1] = (byte)(0x50 | (counter & 0x0F));
        destination[2] = stop ? (byte)0 : (byte)1;
    }

    internal static void WriteUsb(byte counter, bool stop, Span<byte> destination)
    {
        destination.Clear();
        destination[0] = 0x03;
        destination[1] = (byte)(0x50 | (counter & 0x0F));
        destination[2] = stop ? (byte)2 : (byte)1;
    }

    internal static bool IsValidUsbReport(Switch2ControllerModel model, ReadOnlySpan<byte> report) =>
        model == Switch2ControllerModel.GameCube ?
            report.Length == Switch2UsbHdRumbleCodec.ReportLength &&
            report[0] == 0x03 && (report[1] & 0xF0) == 0x50 &&
            report[2] is 1 or 2 && report[3..].IndexOfAnyExcept((byte)0) < 0 :
            Switch2UsbHdRumbleCodec.TryDecodeProController(report, out _, out _, out _, out _);
}
