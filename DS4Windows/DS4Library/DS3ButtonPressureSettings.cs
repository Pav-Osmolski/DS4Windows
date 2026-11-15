using System;

namespace DS4Windows;

/// <summary>Profile range for DS3 button pressure mapped to analogue output axes.</summary>
public sealed class DS3ButtonPressureSettings
{
    public int StartPercent { get; private set; }
    public int FullPercent { get; private set; } = 100;

    public void SetRange(int startPercent, int fullPercent)
    {
        StartPercent = Math.Clamp(startPercent, 0, 99);
        FullPercent = Math.Clamp(fullPercent, StartPercent + 1, 100);
    }

    public void Reset() => SetRange(0, 100);

    public byte Map(byte pressure)
    {
        int aboveStart = pressure * 100 - StartPercent * 255;
        if (aboveStart <= 0) return 0;
        int range = FullPercent - StartPercent;
        if (aboveStart >= range * 255) return 255;
        return (byte)((aboveStart + range / 2) / range);
    }
}
