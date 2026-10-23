using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DS4Windows;

namespace DS4WinWPF.DS4Forms.ViewModels;

internal abstract class XInputDiagnosticObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

internal sealed class XInputDiagnosticSlot : XInputDiagnosticObservable
{
    private static readonly (ushort Mask, string Label)[] buttons =
    {
        (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"),
        (0x0100, "LB"), (0x0200, "RB"), (0x0020, "Back"), (0x0010, "Start"),
        (0x0040, "L3"), (0x0080, "R3"), (0x0001, "D-pad Up"),
        (0x0002, "D-pad Down"), (0x0004, "D-pad Left"), (0x0008, "D-pad Right"),
    };
    private string status = "Checking...", pressedButtons = "No buttons pressed";
    private string leftStick = "X: 0   Y: 0", rightStick = "X: 0   Y: 0";
    private bool connected;
    private int leftTrigger, rightTrigger;

    internal XInputDiagnosticSlot(int index) => Title = global::DS4Windows.UiText.Format("Slot {0} (XInput index {1})", index + 1, index);
    public string Title { get; }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool Connected { get => connected; private set => Set(ref connected, value); }
    public string PressedButtons { get => pressedButtons; private set => Set(ref pressedButtons, value); }
    public string LeftStick { get => leftStick; private set => Set(ref leftStick, value); }
    public string RightStick { get => rightStick; private set => Set(ref rightStick, value); }
    public int LeftTrigger { get => leftTrigger; private set => Set(ref leftTrigger, value); }
    public int RightTrigger { get => rightTrigger; private set => Set(ref rightTrigger, value); }

    internal void Apply(XInputDiagnosticReading reading)
    {
        Connected = reading.ErrorCode == 0;
        Status = Connected ? global::DS4Windows.UiText.Get("Connected") : reading.ErrorCode == XInputDiagnosticPoller.NotConnected ?
            global::DS4Windows.UiText.Get("Not connected") : global::DS4Windows.UiText.Format("Read failed (Windows error {0})", reading.ErrorCode);
        ApplyInputs(Connected ? reading.State.Gamepad : default);
    }

    internal void Unavailable()
    {
        Connected = false;
        Status = global::DS4Windows.UiText.Get("Unavailable");
        ApplyInputs(default);
    }

    private void ApplyInputs(XInputDiagnosticGamepad state)
    {
        var pressed = new List<string>();
        foreach (var button in buttons)
            if ((state.Buttons & button.Mask) != 0) pressed.Add(button.Label);
        PressedButtons = pressed.Count == 0 ? "No buttons pressed" : string.Join("  /  ", pressed);
        LeftStick = string.Create(CultureInfo.InvariantCulture, $"X: {state.LeftX}   Y: {state.LeftY}");
        RightStick = string.Create(CultureInfo.InvariantCulture, $"X: {state.RightX}   Y: {state.RightY}");
        LeftTrigger = state.LeftTrigger;
        RightTrigger = state.RightTrigger;
    }
}

/// <summary>UI-owned lifetime; one off-thread poll at a time, no updates after closing.</summary>
internal sealed class XInputDiagnosticsViewModel : XInputDiagnosticObservable
{
    private readonly XInputDiagnosticPoller poller;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private bool busy, closed, failed;
    private string error;
    internal XInputDiagnosticsViewModel(XInputDiagnosticPoller poller = null)
    {
        this.poller = poller ?? new XInputDiagnosticPoller();
        Slots = new[] { new XInputDiagnosticSlot(0), new XInputDiagnosticSlot(1),
            new XInputDiagnosticSlot(2), new XInputDiagnosticSlot(3) };
    }
    public XInputDiagnosticSlot[] Slots { get; }
    public string Error { get => error; private set => Set(ref error, value); }

    internal async Task PollAsync()
    {
        if (closed || busy || failed) return;
        busy = true;
        try
        {
            var readings = await Task.Run(() => poller.Poll(clock.ElapsedMilliseconds));
            if (closed) return;
            for (int i = 0; i < Slots.Length; i++) Slots[i].Apply(readings[i]);
        }
        catch (Exception ex)
        {
            if (closed) return;
            failed = true;
            Error = ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException ?
                "Windows XInput is unavailable. Close this window and reopen it to retry." :
                "XInput could not be read. Close this window and reopen it to retry. " + ex.Message;
            foreach (var slot in Slots) slot.Unavailable();
        }
        finally { busy = false; }
    }

    internal void Close() { closed = true; clock.Stop(); }
}
