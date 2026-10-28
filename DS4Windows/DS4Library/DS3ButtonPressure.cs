using System;

namespace DS4Windows;

/// <summary>
/// Same-report DS3 button pressures, retained separately from digital button
/// semantics. Default means the source has no pressure channels; zero on an
/// available channel means a released/zero-pressure value, never full pressure.
/// </summary>
public readonly record struct DS3ButtonPressure(bool IsAvailable,
    byte DpadUp, byte DpadRight, byte DpadDown, byte DpadLeft,
    byte L1, byte R1, byte Triangle, byte Circle, byte Cross, byte Square)
{
    internal bool TryGetValue(DS4Controls control, out byte value)
    {
        value = 0;
        if (!IsAvailable) return false;
        switch (control)
        {
            case DS4Controls.DpadUp: value = DpadUp; break;
            case DS4Controls.DpadRight: value = DpadRight; break;
            case DS4Controls.DpadDown: value = DpadDown; break;
            case DS4Controls.DpadLeft: value = DpadLeft; break;
            case DS4Controls.L1: value = L1; break;
            case DS4Controls.R1: value = R1; break;
            case DS4Controls.Triangle: value = Triangle; break;
            case DS4Controls.Circle: value = Circle; break;
            case DS4Controls.Cross: value = Cross; break;
            case DS4Controls.Square: value = Square; break;
            default: return false;
        }
        return true;
    }
}

internal static class DS3InputReportParser
{
    // SXS feature report: one feature header byte before the 49-byte DS3
    // report. These offsets match the existing reader and DsHidMini's raw
    // Sixaxis-compatible report; they are not offsets in a DS4 report.
    internal static bool TryReadControls(ReadOnlySpan<byte> report, int offset, DS4State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.DS3Pressure = default;
        if (offset < 0 || offset > report.Length || report.Length - offset < 49) return false;
        byte first = report[2 + offset], second = report[3 + offset];
        state.Share = (first & 0x01) != 0;
        state.L3 = (first & 0x02) != 0;
        state.R3 = (first & 0x04) != 0;
        state.Options = (first & 0x08) != 0;
        state.DpadUp = (first & 0x10) != 0 && report[14 + offset] > 0;
        state.DpadRight = (first & 0x20) != 0 && report[15 + offset] > 0;
        state.DpadDown = (first & 0x40) != 0 && report[16 + offset] > 0;
        state.DpadLeft = (first & 0x80) != 0 && report[17 + offset] > 0;
        state.L2 = (second & 0x01) != 0 ? report[18 + offset] : (byte)0;
        state.R2 = (second & 0x02) != 0 ? report[19 + offset] : (byte)0;
        state.L1 = (second & 0x04) != 0 && report[20 + offset] > 0;
        state.R1 = (second & 0x08) != 0 && report[21 + offset] > 0;
        state.Triangle = (second & 0x10) != 0 && report[22 + offset] > 0;
        state.Circle = (second & 0x20) != 0 && report[23 + offset] > 0;
        state.Cross = (second & 0x40) != 0 && report[24 + offset] > 0;
        state.Square = (second & 0x80) != 0 && report[25 + offset] > 0;
        state.PS = (report[4 + offset] & 0x01) != 0;
        state.L2Btn = state.L2 > 0; state.R2Btn = state.R2 > 0;
        state.L2Raw = state.L2; state.R2Raw = state.R2;
        state.LX = report[6 + offset]; state.LY = report[7 + offset];
        state.RX = report[8 + offset]; state.RY = report[9 + offset];
        state.DS3Pressure = new DS3ButtonPressure(true,
            state.DpadUp ? report[14 + offset] : (byte)0,
            state.DpadRight ? report[15 + offset] : (byte)0,
            state.DpadDown ? report[16 + offset] : (byte)0,
            state.DpadLeft ? report[17 + offset] : (byte)0,
            state.L1 ? report[20 + offset] : (byte)0,
            state.R1 ? report[21 + offset] : (byte)0,
            state.Triangle ? report[22 + offset] : (byte)0,
            state.Circle ? report[23 + offset] : (byte)0,
            state.Cross ? report[24 + offset] : (byte)0,
            state.Square ? report[25 + offset] : (byte)0);
        return true;
    }
}
