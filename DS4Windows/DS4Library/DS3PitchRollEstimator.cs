using System;

namespace DS4Windows;

internal sealed class DS3PitchRollEstimator
{
    private const int WindowSize = 4;
    private const ulong MaxSampleGapUs = 50_000;
    private const double MinGravity = SixAxis.ACC_RES_PER_G * 0.8;
    private const double MaxGravity = SixAxis.ACC_RES_PER_G * 1.2;
    private const double MaxRotationDegreesPerSecond = 360;

    private readonly Sample[] samples = new Sample[WindowSize];
    private int next;
    private int count;
    private ulong lastTimestamp;
    private long profileRevision;

    internal void Reset()
    {
        next = 0;
        count = 0;
    }

    internal (int Pitch, int Roll) Update(int x, int y, int z, ulong timestamp, long revision)
    {
        double magnitudeSquared = (double)x * x + (double)y * y + (double)z * z;
        if (magnitudeSquared < MinGravity * MinGravity || magnitudeSquared > MaxGravity * MaxGravity)
        {
            Reset();
            return default;
        }

        if (count != 0 && (revision != profileRevision || timestamp <= lastTimestamp ||
            timestamp - lastTimestamp > MaxSampleGapUs))
        {
            Reset();
        }

        profileRevision = revision;
        lastTimestamp = timestamp;
        samples[next] = new Sample(x, y, z, timestamp);
        next = (next + 1) % WindowSize;
        count = Math.Min(count + 1, WindowSize);
        if (count < WindowSize)
            return default;

        Sample first = samples[next];
        Sample second = samples[(next + 1) % WindowSize];
        Sample third = samples[(next + 2) % WindowSize];
        Sample fourth = samples[(next + 3) % WindowSize];
        double elapsedSeconds =
            ((third.Time - first.Time) + (fourth.Time - second.Time)) / 2_000_000.0;

        // The rotation between the two averaged gravity vectors provides only the
        // components observable from gravity. Measured yaw is supplied separately.
        double oldX = first.X + second.X, oldY = first.Y + second.Y, oldZ = first.Z + second.Z;
        double newX = third.X + fourth.X, newY = third.Y + fourth.Y, newZ = third.Z + fourth.Z;
        double crossX = oldY * newZ - oldZ * newY;
        double crossY = oldZ * newX - oldX * newZ;
        double crossZ = oldX * newY - oldY * newX;
        double crossLength = Math.Sqrt(crossX * crossX + crossY * crossY + crossZ * crossZ);
        double dot = oldX * newX + oldY * newY + oldZ * newZ;
        double degreesPerSecond = Math.Atan2(crossLength, dot) * (180 / Math.PI) / elapsedSeconds;

        if (degreesPerSecond > MaxRotationDegreesPerSecond)
        {
            Reset();
            return default;
        }

        if (crossLength == 0)
            return default;

        double scale = -degreesPerSecond * SixAxis.GYRO_RES_IN_DEG_SEC / crossLength;
        return ((int)Math.Round(crossX * scale), (int)Math.Round(crossZ * scale));
    }

    private readonly record struct Sample(int X, int Y, int Z, ulong Time);
}
