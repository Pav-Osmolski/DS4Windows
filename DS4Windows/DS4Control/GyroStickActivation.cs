using System;

namespace DS4Windows;

public enum GyroStickActivationMode { Disabled, WhileMoving, WhileCentered }
public enum GyroActivationStick { Left, Right, Either }

/// <summary>Per-input-source gate, evaluated before output remapping. Sensor data stays intact.</summary>
internal sealed class GyroStickActivation
{
    private bool leftMoving, rightMoving;
    private GyroStickActivationMode previousMode;
    private GyroActivationStick previousStick;
    private int previousThreshold;

    internal void Reset() => leftMoving = rightMoving = false;

    internal bool Evaluate(DS4State state, GyroStickActivationMode mode,
        GyroActivationStick stick, int thresholdPercent)
    {
        int threshold = Math.Clamp(thresholdPercent, 1, 95);
        if (mode != previousMode || stick != previousStick || threshold != previousThreshold)
            Reset();
        previousMode = mode;
        previousStick = stick;
        previousThreshold = threshold;
        if (mode == GyroStickActivationMode.Disabled || !Enum.IsDefined(mode) ||
            !Enum.IsDefined(stick))
            return true;

        leftMoving = IsMoving(state.LXAxis.ProfileCoordinate, state.LYAxis.ProfileCoordinate,
            threshold, leftMoving);
        rightMoving = IsMoving(state.RXAxis.ProfileCoordinate, state.RYAxis.ProfileCoordinate,
            threshold, rightMoving);
        bool moving = stick == GyroActivationStick.Left ? leftMoving :
            stick == GyroActivationStick.Right ? rightMoving : leftMoving || rightMoving;
        return mode == GyroStickActivationMode.WhileMoving ? moving : !moving;
    }

    private static bool IsMoving(double x, double y, int threshold, bool wasMoving)
    {
        // The asymmetric byte range has 128 negative and 127 positive steps.
        x = (x - 128) / (x < 128 ? 128.0 : 127.0);
        y = (y - 128) / (y < 128 ? 128.0 : 127.0);
        double boundary = (wasMoving ? Math.Max(0, threshold - 2) : threshold) / 100.0;
        return x * x + y * y > boundary * boundary;
    }
}
