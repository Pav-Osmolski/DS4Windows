using System;
using System.Runtime.InteropServices;

namespace DS4Windows;

// Matches XINPUT_GAMEPAD / XINPUT_STATE from the Windows SDK, including padding.
[StructLayout(LayoutKind.Sequential)]
internal struct XInputDiagnosticGamepad
{
    internal ushort Buttons;
    internal byte LeftTrigger, RightTrigger;
    internal short LeftX, LeftY, RightX, RightY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputDiagnosticState
{
    internal uint PacketNumber;
    internal XInputDiagnosticGamepad Gamepad;
}

internal delegate uint ReadXInputDiagnosticState(uint index, out XInputDiagnosticState state);

internal readonly record struct XInputDiagnosticReading(uint ErrorCode, XInputDiagnosticState State);

/// <summary>Read-only Windows slot queries. No device creation, vibration, or slot reassignment.</summary>
internal sealed class XInputDiagnosticPoller
{
    internal const uint NotConnected = 1167;
    private readonly ReadXInputDiagnosticState read;
    private readonly XInputDiagnosticReading[] readings = new XInputDiagnosticReading[4];
    private readonly long[] nextProbe = new long[4];

    internal XInputDiagnosticPoller(ReadXInputDiagnosticState read = null)
    {
        this.read = read ?? XInputGetState;
    }

    // Called serially by the diagnostic window's worker, never by the input mapper.
    internal XInputDiagnosticReading[] Poll(long elapsedMilliseconds)
    {
        for (uint index = 0; index < 4; index++)
        {
            if (elapsedMilliseconds < nextProbe[index]) continue;
            uint error = read(index, out var state);
            // Failed calls need not initialise the native output. Never publish stale inputs.
            readings[index] = new XInputDiagnosticReading(error, error == 0 ? state : default);
            // Disconnected slots can be expensive to query. Recheck once per second.
            nextProbe[index] = error == 0 ? 0 : elapsedMilliseconds + 1000;
        }
        return (XInputDiagnosticReading[])readings.Clone();
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint XInputGetState(uint userIndex, out XInputDiagnosticState state);
}
