using System;

namespace DS4Windows;

// Only shares state already awaiting USB delivery. It never rewrites a command,
// examines a claimed head, or suppresses a later occurrence after delivery.
internal static class DualSensePendingTriggerState
{
    internal static bool IsEligible(ReadOnlySpan<byte> report)
    {
        if (report.Length != 48 || (report[1] & 0x0C) == 0) return false;
        Span<byte> canonical = stackalloc byte[48];
        report.CopyTo(canonical);
        for (int side = 0; side < 2; side++)
        {
            Span<byte> effect = canonical.Slice(11 + side * 11, 11);
            if ((report[1] & (4 << side)) == 0) effect.Clear();
            else if (effect[0] == 0x22)
            {
                // Static bow resistance: only its position/force parameters
                // qualify. Leave extensions and timed/unknown modes ordered.
                for (int i = 5; i < 11; i++) if (effect[i] != 0) return false;
                effect[0] = 0x21;
            }
        }
        return DualSenseNativeFeedbackRepeatPolicy.IsRepeatableReport(canonical, allowPowerState: true);
    }

    internal static bool MatchPrevious(ReadOnlySpan<byte> candidate,
        ReadOnlySpan<byte> previous, ref byte remaining)
    {
        if (!IsEligible(previous) ||
            (candidate[1] & ~0x0C) != (previous[1] & ~0x0C) ||
            !candidate.Slice(2, 9).SequenceEqual(previous.Slice(2, 9)) ||
            !candidate[33..].SequenceEqual(previous[33..])) return false;

        for (int side = 0; side < 2; side++)
        {
            byte flag = (byte)(4 << side);
            if ((remaining & previous[1] & flag) == 0) continue;
            int offset = 11 + side * 11;
            // Stop at the most recent command for that side. Looking beyond
            // a changed effect would erase an off/on or pause transition.
            if (!candidate.Slice(offset, 11).SequenceEqual(previous.Slice(offset, 11))) return false;
            remaining &= (byte)~flag;
        }
        return true;
    }
}
