using System;

namespace DS4Windows;

// A narrow contract for native USB state reports, not an opaque HID merger.
// Only consecutive byte-identical pending motor/trigger reports qualify.
// Audio, LEDs, reset/calibration, media and vendor commands are barriers.
internal static class DualSenseNativeUsbStatePolicy
{
    internal enum Kind : byte { Unknown, Rumble, Triggers }

    internal static Kind ClassifyEnvelope(ReadOnlySpan<byte> feedback)
    {
        if (feedback.Length is not (76 or 217 or 474)) return Kind.Unknown;
        foreach (byte value in feedback[76..]) if (value != 0) return Kind.Unknown;
        return Classify(feedback.Slice(28, 48));
    }

    internal static Kind Classify(ReadOnlySpan<byte> report)
    {
        if (report.Length != 48 || report[0] != 2 || report[2] != 0x40 ||
            report[1] is not (0x03 or 0x0E) || report[39] != 0 ||
            (report[37] & 0x0F) > 7 || (report[37] >> 4) > 7 ||
            !Zero(report.Slice(5, 6)) || !Zero(report.Slice(33, 4)) ||
            report[38] != 0 || !Zero(report[40..])) return Kind.Unknown;
        if (report[1] == 3)
            return Zero(report.Slice(11, 22)) ? Kind.Rumble : Kind.Unknown;
        return report[3] == 0 && report[4] == 0 &&
            Trigger(report.Slice(11, 11)) && Trigger(report.Slice(22, 11)) ?
            Kind.Triggers : Kind.Unknown;
    }

    private static bool Trigger(ReadOnlySpan<byte> effect) =>
        (effect[0] is 0x00 or 0x05 or 0x21 or 0x25 or 0x26) &&
        effect[7] == 0 && effect[8] == 0 && effect[10] == 0;

    private static bool Zero(ReadOnlySpan<byte> values)
    {
        foreach (byte value in values) if (value != 0) return false;
        return true;
    }
}
