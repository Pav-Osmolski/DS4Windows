using System;
using System.Globalization;

namespace DS4Windows;

internal sealed class MultiActionConfiguration
{
    internal readonly string[] Macros;
    internal readonly int HoldMilliseconds;
    internal readonly int TapMilliseconds;
    internal bool HasTriple => Macros[3].Length != 0;
    internal MultiActionConfiguration(string details)
    {
        string[] parts = (details ?? "").Split(',');
        Macros = new string[4];
        for (int i = 0; i < 4; i++) Macros[i] = i < parts.Length ? parts[i] : "";
        HoldMilliseconds = Read(parts, 4, 500, 150, 60000);
        TapMilliseconds = Read(parts, 5, 150, 50, 2000);
    }
    private static int Read(string[] parts, int index, int fallback, int min, int max) =>
        index < parts.Length && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value >= min && value <= max ? value : fallback;
}

// One instance per action/controller, driven by monotonic time, not UI timers.
internal sealed class MultiActionGesture
{
    private bool down, consumed, initialized;
    private int taps;
    private long pressedAt, releasedAt;
    internal void Reset() { down = consumed = false; taps = 0; initialized = false; }
    internal int Update(bool pressed, long now, MultiActionConfiguration options, int debounce = 0)
    {
        // Do not turn a button already held when a profile/controller becomes
        // active into a new gesture. Wait for its release first.
        if (!initialized) { initialized = true; down = pressed; consumed = pressed; return -1; }
        int result = -1;
        if (!down && taps != 0 && now - releasedAt > options.TapMilliseconds + debounce)
        {
            result = taps == 1 ? 0 : 2;
            taps = 0;
        }
        if (pressed && !down)
        {
            down = true; consumed = false; pressedAt = now;
            if (taps == 1 && !options.HasTriple)
            {
                taps = 0; consumed = true; return 2;
            }
        }
        if (pressed && down && !consumed && now - pressedAt >= options.HoldMilliseconds + debounce)
        {
            taps = 0; consumed = true; return 1;
        }
        if (!pressed && down)
        {
            down = false;
            if (!consumed && now - pressedAt <= options.TapMilliseconds + debounce)
            {
                taps++; releasedAt = now;
                if (taps == 3) { taps = 0; return 3; }
            }
            else taps = 0;
            consumed = false;
        }
        return result;
    }
}
