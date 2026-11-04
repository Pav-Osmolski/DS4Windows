using System;
using System.ComponentModel;
using DS4Windows;

namespace DS4WinWPF.DS4Forms.ViewModels;

public sealed class JoystickButtonAssignment : INotifyPropertyChanged
{
    private readonly int output;
    private readonly Func<GenericJoystickOptions> read;
    private readonly Action<GenericJoystickOptions> write;
    internal JoystickButtonAssignment(int output, Func<GenericJoystickOptions> read, Action<GenericJoystickOptions> write)
    { this.output = output; this.read = read; this.write = write; }
    public string Name => $"Button {output + 1}";
    public string[] Sources => sourceNames;
    private static readonly string[] sourceNames = {
        "Unassigned", "Cross / A", "Circle / B", "Square / X", "Triangle / Y",
        "L1 / LB", "R1 / RB", "L2 (Buttons trigger mode)", "R2 (Buttons trigger mode)",
        "Share / Back", "Options / Start", "L3", "R3", "PS / Guide", "Touchpad click"
    };
    public int SourceIndex
    {
        get => read().SourceFor(output);
        set { if (value >= 0 && value < Sources.Length && value != SourceIndex) write(read().WithButtonSource(output, value)); }
    }
    public event PropertyChangedEventHandler PropertyChanged;
    internal void Refresh() => PropertyChanged?.Invoke(this, new(nameof(SourceIndex)));
}
