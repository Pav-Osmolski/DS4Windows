using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DS4Windows;

internal sealed class MultiActionConfiguration
{
    internal readonly string[] Macros;
    internal readonly string[] Names = new string[4];
    internal readonly bool[] Enabled = new[] { true, true, true, true };
    internal readonly int HoldMilliseconds;
    internal readonly int TapMilliseconds;
    internal readonly string GroupKey;
    internal bool HasTriple => IsEnabled(3);
    internal bool HasEnabledMacro => IsEnabled(0) || IsEnabled(1) || IsEnabled(2) || IsEnabled(3);
    internal bool IsEnabled(int gesture) => Enabled[gesture] && Macros[gesture].Length != 0;
    internal MultiActionConfiguration(string details, string controls = "")
    {
        string[] parts = (details ?? "").Split(',');
        Macros = new string[4];
        for (int i = 0; i < 4; i++) Macros[i] = i < parts.Length ? parts[i] : "";
        HoldMilliseconds = Read(parts, 4, 500, 150, 60000);
        TapMilliseconds = Read(parts, 5, 150, 50, 2000);
        GroupKey = string.Join("/", (controls ?? "").Split('/').Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)) +
            ":" + HoldMilliseconds.ToString(CultureInfo.InvariantCulture) + ":" + TapMilliseconds.ToString(CultureInfo.InvariantCulture);
        // Keep the first six fields readable by older versions. Disabled macros
        // have empty legacy slots; their recordings and labels live in the extension.
        if (parts.Length == 15 && parts[6].StartsWith("v2:", StringComparison.Ordinal) &&
            int.TryParse(parts[6].AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int mask) && mask is >= 0 and <= 15)
            for (int i = 0; i < 4; i++)
            {
                Enabled[i] = (mask & (1 << i)) != 0;
                Macros[i] = parts[11 + i];
                try
                {
                    string name = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(parts[7 + i]));
                    Names[i] = name.Length <= 128 ? name : "";
                }
                catch (Exception e) when (e is FormatException or DecoderFallbackException) { Names[i] = ""; }
            }
    }
    internal static string Serialize(string[] macros, bool[] enabled, string[] names, int hold, int tap)
    {
        var parts = new string[15];
        int mask = 0;
        for (int i = 0; i < 4; i++)
        {
            parts[i] = enabled[i] ? macros[i] : "";
            if (enabled[i]) mask |= 1 << i;
            parts[7 + i] = Convert.ToBase64String(Encoding.UTF8.GetBytes(names[i] ?? ""));
            parts[11 + i] = macros[i];
        }
        parts[4] = hold.ToString(CultureInfo.InvariantCulture);
        parts[5] = tap.ToString(CultureInfo.InvariantCulture);
        parts[6] = "v2:" + mask.ToString(CultureInfo.InvariantCulture);
        return string.Join(",", parts);
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
    internal int Update(bool pressed, long now, MultiActionConfiguration options, int debounce = 0, bool reserveTriple = false)
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
            if (taps == 1 && !options.HasTriple && !reserveTriple)
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
