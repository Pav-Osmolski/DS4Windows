/*
DS4Windows
Copyright (C) 2026 hbashton

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
*/

using System;

namespace DS4Windows;

// A pending physical repeat must belong to the exact same producer and
// lifecycle. Default contexts (including direct/DSX callers) never opt in.
internal readonly record struct DualSenseNativeRepeatContext(
    object Producer, long StreamGeneration, ViiperNativeCommandContext Command);

/// <summary>
/// Recognizes state-setting reports for pending-tail repeat suppression, not
/// general HID command coalescing. Unknown commands retain exact FIFO delivery.
/// </summary>
internal static class DualSenseNativeFeedbackRepeatPolicy
{
    internal static bool IsRepeatable(ReadOnlySpan<byte> feedback, bool allowPowerState = false)
    {
        // VIIPER V3/V4/V5: 28 compatibility bytes, the native 48-byte report,
        // then an optional 141/398-byte Bluetooth media carrier. Never fold
        // media samples or unrecognized extensions into a native command.
        if (feedback.Length is not (76 or 217 or 474)) return false;
        if (!IsZero(feedback[76..])) return false;
        return IsRepeatableReport(feedback.Slice(28, 48), allowPowerState);
    }

    internal static bool IsRepeatableReport(ReadOnlySpan<byte> report, bool allowPowerState = false)
    {
        if (report.Length != 48 || report[0] != 0x02 || (report[1] & ~0x0F) != 0 ||
            (report[2] & ~(allowPowerState ? 0x54 : 0x14)) != 0 || (report[39] & ~0x04) != 0)
            return false;

        // Only ordinary rumble, documented trigger state, LED color/player
        // state and their explicit stops qualify. Audio routing, calibration,
        // LED reset/animation and reserved/vendor data are command barriers.
        // Haptic/trigger power reduction is an absolute state value too.
        // Accept only the documented 0..7 reduction in each nibble, with its
        // validity bit set. Never interpret unknown power/reserved data.
        if ((report[2] & 0x40) != 0
                ? (report[37] & 0x0F) > 7 || (report[37] >> 4) > 7
                : report[37] != 0) return false;
        if ((report[2] & 0x40) != 0 &&
            (((report[2] & 0x10) == 0 && report[44] != 0) ||
             ((report[2] & 0x04) == 0 && !IsZero(report[45..])))) return false;
        if (!IsZero(report.Slice(5, 6)) || !IsZero(report.Slice(33, 4)) || report[38] != 0 ||
            !IsZero(report.Slice(40, 4))) return false;
        if (!IsKnownTrigger(report.Slice(11, 11)) ||
            !IsKnownTrigger(report.Slice(22, 11))) return false;
        return true;
    }

    private static bool IsKnownTrigger(ReadOnlySpan<byte> effect) =>
        (effect[0] is 0x00 or 0x01 or 0x02 or 0x05 or 0x21 or 0x25 or 0x26) &&
        effect[7] == 0 && effect[8] == 0 && effect[10] == 0;

    private static bool IsZero(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
            if (value != 0) return false;
        return true;
    }
}
