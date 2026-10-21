using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace DS4Windows;

public enum BatteryAlertKind { Low, Full, Check }
public enum BatteryAlertMode { Speech, Sound, Both, Off }

public abstract class BatteryAlertObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class BatteryAlertRule : BatteryAlertObservable
{
    private BatteryAlertMode mode;
    private string soundFile = "", soundName = "No sound selected";
    private bool vibrate;
    private int strength = 25, duration = 200, pulses = 2;
    [XmlIgnore]
    public BatteryAlertMode Mode { get => mode; set => Set(ref mode, Enum.IsDefined(value) ? value : BatteryAlertMode.Speech); }
    [XmlElement("Mode")]
    public string ModeString
    {
        get => Mode.ToString();
        set => Mode = Enum.TryParse<BatteryAlertMode>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : BatteryAlertMode.Speech;
    }
    public string SoundFile { get => soundFile; set => Set(ref soundFile, value ?? ""); }
    public string SoundName { get => soundName; set => Set(ref soundName, value ?? ""); }
    public bool Vibrate { get => vibrate; set => Set(ref vibrate, value); }
    public int Strength { get => strength; set => Set(ref strength, Math.Clamp(value, 1, 100)); }
    public int Duration { get => duration; set => Set(ref duration, Math.Clamp(value, 50, 1000)); }
    public int Pulses { get => pulses; set => Set(ref pulses, Math.Clamp(value, 1, 5)); }
    public BatteryAlertRule Clone() => new() { Mode = Mode, SoundFile = SoundFile, SoundName = SoundName,
        Vibrate = Vibrate, Strength = Strength, Duration = Duration, Pulses = Pulses };
}

public sealed class BatteryAlertSettings : BatteryAlertObservable
{
    private bool lowEnabled, fullEnabled;
    private int lowThreshold = 20, volume = 60;
    private double rate = 1;
    private string voice = "", endpoint = "";
    public bool LowEnabled { get => lowEnabled; set => Set(ref lowEnabled, value); }
    public bool FullEnabled { get => fullEnabled; set => Set(ref fullEnabled, value); }
    public int LowThreshold { get => lowThreshold; set => Set(ref lowThreshold, Math.Clamp(value, 5, 50)); }
    public int Volume { get => volume; set => Set(ref volume, Math.Clamp(value, 0, 100)); }
    public double Rate { get => rate; set => Set(ref rate, double.IsFinite(value) ? Math.Clamp(value, 0.5, 2) : 1); }
    public string Voice { get => voice; set => Set(ref voice, value ?? ""); }
    // Empty = Windows default; controller = this controller's enabled native speaker route.
    public string Endpoint { get => endpoint; set => Set(ref endpoint, value ?? ""); }
    public BatteryAlertRule Low { get; set; } = new();
    public BatteryAlertRule Full { get; set; } = new() { Pulses = 1, Duration = 400 };
    public BatteryAlertRule Check { get; set; } = new() { Pulses = 1 };
    public BatteryAlertRule Rule(BatteryAlertKind kind) => kind switch { BatteryAlertKind.Low => Low, BatteryAlertKind.Full => Full, _ => Check };
    public BatteryAlertSettings Clone() => new() { LowEnabled = LowEnabled, FullEnabled = FullEnabled,
        LowThreshold = LowThreshold, Volume = Volume, Rate = Rate, Voice = Voice, Endpoint = Endpoint,
        Low = Low?.Clone() ?? new(), Full = Full?.Clone() ?? new(), Check = Check?.Clone() ?? new() };
}

internal readonly record struct BatteryAlertReading(int Percentage, bool Charging, bool Full, bool Reliable, string Level = null);

internal static class BatteryAlertReports
{
    internal static bool DualShock4Complete(byte status) => (status & 0x10) != 0 && (status & 0x0F) == 11;
    internal static bool DualShock4Valid(byte status) => (status & 0x0F) <= ((status & 0x10) != 0 ? 11 : 10);
    internal static bool DualSenseComplete(byte status) => (status & 0xF0) == 0x20;
    internal static bool DualSenseValid(byte status) => DualSenseComplete(status) ||
        (status & 0xF0) <= 0x10 && (status & 0x0F) <= 10;
}

/// <summary>One latch per physical identity, retained across reconnections and profile changes.</summary>
internal sealed class BatteryAlertPolicy
{
    internal static bool IsLow(BatteryAlertReading reading, int threshold) =>
        !reading.Charging && (reading.Level == null ? reading.Percentage <= threshold : reading.Level == "low");
    private bool lowLatched, fullLatched;
    private int threshold = -1;
    private long lastLow = long.MinValue, lastFull = long.MinValue;
    private int stableLow, stableFull, recoveredLow, recoveredFull;
    internal void Pause() => stableLow = stableFull = recoveredLow = recoveredFull = 0;

    internal BatteryAlertKind? Observe(BatteryAlertReading reading, BatteryAlertSettings settings, long seconds)
    {
        if (!reading.Reliable) { Pause(); return null; }
        // Ignore transient reports. A changed threshold is a new deliberate configuration.
        if (threshold != settings.LowThreshold) { threshold = settings.LowThreshold; lowLatched = false; stableLow = 0; }
        bool low = IsLow(reading, threshold);
        stableLow = low ? Math.Min(3, stableLow + 1) : 0;
        stableFull = reading.Full ? Math.Min(3, stableFull + 1) : 0;
        bool recovered = reading.Charging || (reading.Level == null ? reading.Percentage >= threshold + 5 : reading.Level != "low");
        recoveredLow = recovered ? Math.Min(3, recoveredLow + 1) : 0;
        recoveredFull = !reading.Full && reading.Percentage <= 95 ? Math.Min(3, recoveredFull + 1) : 0;
        if (recoveredLow == 3) lowLatched = false;
        if (recoveredFull == 3) fullLatched = false;
        if (settings.LowEnabled && stableLow == 3 && !lowLatched)
        {
            lowLatched = true;
            if (lastLow != long.MinValue && seconds - lastLow < 300) return null;
            lastLow = seconds;
            return BatteryAlertKind.Low;
        }
        if (settings.FullEnabled && stableFull == 3 && !fullLatched)
        {
            fullLatched = true;
            if (lastFull != long.MinValue && seconds - lastFull < 300) return null;
            lastFull = seconds;
            return BatteryAlertKind.Full;
        }
        return null;
    }
}
