using System;
using System.Collections.Generic;
using System.Linq;
using DS4Windows;

namespace DS4WinWPF.DS4Control;

/// <summary>
/// Per-controller button debouncing with setup-time typed field selection.
/// The report path reuses one scratch state and performs no reflection,
/// dictionary enumeration, boxing, or managed allocation.
/// </summary>
public sealed class Debouncer
{
    private readonly List<Entry> entries = new(24);
    private readonly DS4State scratchState = new();
    private TimeSpan duration;
    private IReadOnlyDictionary<DS4Controls, int> buttonOverrides;
    private bool enabled;

    public Debouncer(TimeSpan duration)
    {
        this.duration = duration;
        enabled = duration > TimeSpan.Zero;
    }

    public void AddDebouncer(string name)
    {
        Field field = ParseField(name);
        var effective = buttonOverrides != null && buttonOverrides.TryGetValue(PhysicalControl(field), out int value)
            ? TimeSpan.FromMilliseconds(value) : duration;
        enabled |= effective > TimeSpan.Zero;
        for (int index = 0; index < entries.Count; index++)
        {
            if (entries[index].Field == field)
            {
                entries[index] = new Entry(field,
                    new DebouncerInstance(effective));
                return;
            }
        }
        entries.Add(new Entry(field, new DebouncerInstance(effective)));
    }

    public DS4State ProcessInput(DS4State currentState)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        if (!enabled)
        {
            return currentState;
        }

        currentState.CopyTo(scratchState);
        DateTime timestamp = currentState.ReportTimeStamp;
        for (int index = 0; index < entries.Count; index++)
        {
            Entry entry = entries[index];
            bool reading = Read(scratchState, entry.Field);
            Write(scratchState, entry.Field,
                entry.Instance.ProcessInput(reading, timestamp));
        }
        return scratchState;
    }

    public void SetDuration(TimeSpan newDuration)
    {
        ApplyDurations(newDuration, null);
    }

    internal void ApplyProfile(int milliseconds, IReadOnlyDictionary<DS4Controls, int> overrides)
    {
        TimeSpan defaultDuration = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 0, 1000));
        if (defaultDuration != duration || !ReferenceEquals(buttonOverrides, overrides))
            ApplyDurations(defaultDuration, overrides);
    }

    private void ApplyDurations(TimeSpan newDuration, IReadOnlyDictionary<DS4Controls, int> overrides)
    {
        duration = newDuration;
        buttonOverrides = overrides;
        enabled = false;
        for (int index = 0; index < entries.Count; index++)
        {
            Entry entry = entries[index];
            var effective = overrides != null && overrides.TryGetValue(PhysicalControl(entry.Field), out int value)
                ? TimeSpan.FromMilliseconds(value) : newDuration;
            entry.Instance.SetDuration(effective);
            enabled |= effective > TimeSpan.Zero;
        }
    }

    internal static bool SupportsOverride(DS4Controls button) => button != DS4Controls.None &&
        Enum.GetValues<Field>().Any(field => PhysicalControl(field) == button);

    private static DS4Controls PhysicalControl(Field field) => field switch
    {
        Field.Cross => DS4Controls.Cross, Field.Triangle => DS4Controls.Triangle,
        Field.Circle => DS4Controls.Circle, Field.Square => DS4Controls.Square,
        Field.L3 => DS4Controls.L3, Field.R3 => DS4Controls.R3,
        Field.Share => DS4Controls.Share, Field.Options => DS4Controls.Options,
        Field.L1 => DS4Controls.L1, Field.R1 => DS4Controls.R1,
        Field.PS => DS4Controls.PS, Field.Capture => DS4Controls.Capture,
        Field.SideL => DS4Controls.SideL, Field.SideR => DS4Controls.SideR,
        Field.DpadUp => DS4Controls.DpadUp, Field.DpadDown => DS4Controls.DpadDown,
        Field.DpadLeft => DS4Controls.DpadLeft, Field.DpadRight => DS4Controls.DpadRight,
        _ => DS4Controls.None, // Trigger axes and touch regions are separate mappings.
    };

    private static Field ParseField(string name) => name switch
    {
        nameof(DS4State.Cross) => Field.Cross,
        nameof(DS4State.Triangle) => Field.Triangle,
        nameof(DS4State.Circle) => Field.Circle,
        nameof(DS4State.Square) => Field.Square,
        nameof(DS4State.R3) => Field.R3,
        nameof(DS4State.L3) => Field.L3,
        nameof(DS4State.Options) => Field.Options,
        nameof(DS4State.Share) => Field.Share,
        nameof(DS4State.R2Btn) => Field.R2Btn,
        nameof(DS4State.L2Btn) => Field.L2Btn,
        nameof(DS4State.R1) => Field.R1,
        nameof(DS4State.L1) => Field.L1,
        nameof(DS4State.PS) => Field.PS,
        nameof(DS4State.TouchButton) => Field.TouchButton,
        nameof(DS4State.Capture) => Field.Capture,
        nameof(DS4State.SideL) => Field.SideL,
        nameof(DS4State.SideR) => Field.SideR,
        nameof(DS4State.DpadUp) => Field.DpadUp,
        nameof(DS4State.DpadDown) => Field.DpadDown,
        nameof(DS4State.DpadLeft) => Field.DpadLeft,
        nameof(DS4State.DpadRight) => Field.DpadRight,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name,
            "Only typed DS4 button fields can be debounced."),
    };

    private static bool Read(DS4State state, Field field) => field switch
    {
        Field.Cross => state.Cross,
        Field.Triangle => state.Triangle,
        Field.Circle => state.Circle,
        Field.Square => state.Square,
        Field.R3 => state.R3,
        Field.L3 => state.L3,
        Field.Options => state.Options,
        Field.Share => state.Share,
        Field.R2Btn => state.R2Btn,
        Field.L2Btn => state.L2Btn,
        Field.R1 => state.R1,
        Field.L1 => state.L1,
        Field.PS => state.PS,
        Field.TouchButton => state.TouchButton,
        Field.Capture => state.Capture,
        Field.SideL => state.SideL,
        Field.SideR => state.SideR,
        Field.DpadUp => state.DpadUp,
        Field.DpadDown => state.DpadDown,
        Field.DpadLeft => state.DpadLeft,
        Field.DpadRight => state.DpadRight,
        _ => false,
    };

    private static void Write(DS4State state, Field field, bool value)
    {
        switch (field)
        {
            case Field.Cross: state.Cross = value; break;
            case Field.Triangle: state.Triangle = value; break;
            case Field.Circle: state.Circle = value; break;
            case Field.Square: state.Square = value; break;
            case Field.R3: state.R3 = value; break;
            case Field.L3: state.L3 = value; break;
            case Field.Options: state.Options = value; break;
            case Field.Share: state.Share = value; break;
            case Field.R2Btn: state.R2Btn = value; break;
            case Field.L2Btn: state.L2Btn = value; break;
            case Field.R1: state.R1 = value; break;
            case Field.L1: state.L1 = value; break;
            case Field.PS: state.PS = value; break;
            case Field.TouchButton: state.TouchButton = value; break;
            case Field.Capture: state.Capture = value; break;
            case Field.SideL: state.SideL = value; break;
            case Field.SideR: state.SideR = value; break;
            case Field.DpadUp: state.DpadUp = value; break;
            case Field.DpadDown: state.DpadDown = value; break;
            case Field.DpadLeft: state.DpadLeft = value; break;
            case Field.DpadRight: state.DpadRight = value; break;
        }
    }

    private enum Field : byte
    {
        Cross, Triangle, Circle, Square, R3, L3, Options, Share,
        R2Btn, L2Btn, R1, L1, PS, TouchButton, Capture, SideL, SideR,
        DpadUp, DpadDown, DpadLeft, DpadRight,
    }

    private readonly struct Entry
    {
        internal Entry(Field field, DebouncerInstance instance)
        {
            Field = field;
            Instance = instance;
        }

        internal Field Field { get; }
        internal DebouncerInstance Instance { get; }
    }

    private sealed class DebouncerInstance
    {
        private bool previousState;
        private bool currentlyDebouncing;
        private DateTime debounceStartTime;

        internal DebouncerInstance(TimeSpan duration)
        {
            Duration = duration;
        }

        private TimeSpan Duration { get; set; }

        internal void SetDuration(TimeSpan duration)
        {
            if (Duration == duration) return;
            Duration = duration;
            previousState = false;
            currentlyDebouncing = false;
        }

        internal bool ProcessInput(bool input, DateTime timestamp)
        {
            if (Duration <= TimeSpan.Zero) return input;
            if (currentlyDebouncing)
            {
                return Debounce(input, timestamp);
            }
            if (previousState != input)
            {
                currentlyDebouncing = true;
                debounceStartTime = timestamp;
                return Debounce(input, timestamp);
            }

            previousState = input;
            return input;
        }

        private bool Debounce(bool reading, DateTime timestamp)
        {
            if (timestamp - debounceStartTime < Duration)
            {
                return true;
            }
            currentlyDebouncing = false;
            return reading;
        }
    }
}
