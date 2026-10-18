using System;
using System.Diagnostics;

namespace DS4Windows;

// Lifetime counters for one physical device. Recording never formats text or
// logs on the transport thread. Queue timestamps survive retries unchanged.
internal sealed class DualSenseNativeFeedbackDiagnostics
{
    private readonly object sync = new();
    private long accepted, attempts, retries, completed, rejected;
    private long highWater, queueMax, ownerMax, completionMax;
    private long rumbleReports, triggerReports, stopReports;
    private long lastStopRevision, lastStopCompletedRevision, lastStopCompletionAge;
    private byte flag0, flag1, smallMotor, largeMotor, power;

    internal void RejectAdmission()
    {
        lock (sync) rejected++;
    }

    internal void Admit(ReadOnlySpan<byte> report, long revision, int depth)
    {
        lock (sync)
        {
            accepted++;
            highWater = Math.Max(highWater, depth);
            flag0 = report[1];
            flag1 = report[2];
            smallMotor = report[3];
            largeMotor = report[4];
            power = report[37];
            if ((flag0 & 3) == 3) rumbleReports++;
            if ((flag0 & 0x0C) != 0) triggerReports++;
            if (IsExplicitMotorStop(report))
            {
                stopReports++;
                lastStopRevision = revision;
            }
        }
    }

    internal void Attempt(ReadOnlySpan<byte> report, long revision,
        long queuedAt, long startedAt, long finishedAt, bool succeeded)
    {
        lock (sync)
        {
            attempts++;
            queueMax = Math.Max(queueMax, Math.Max(0, startedAt - queuedAt));
            ownerMax = Math.Max(ownerMax, Math.Max(0, finishedAt - startedAt));
            if (!succeeded) { retries++; return; }
            completed++;
            long age = Math.Max(0, finishedAt - queuedAt);
            completionMax = Math.Max(completionMax, age);
            if (IsExplicitMotorStop(report))
            {
                lastStopCompletedRevision = revision;
                lastStopCompletionAge = age;
            }
        }
    }

    // Trigger-only updates can contain zero motor bytes without being a motor
    // stop. Only a report explicitly selecting compatible/improved rumble counts.
    internal static bool IsExplicitMotorStop(ReadOnlySpan<byte> report) =>
        report.Length == 48 && report[0] == 2 && report[3] == 0 && report[4] == 0 &&
        (report[1] & 2) != 0 && ((report[1] & 1) != 0 || (report[39] & 4) != 0);

    internal Snapshot Capture()
    {
        lock (sync) return new(accepted, attempts, retries, completed, rejected, highWater, queueMax, ownerMax, completionMax, rumbleReports, triggerReports, stopReports, lastStopRevision, lastStopCompletedRevision, lastStopCompletionAge, flag0, flag1, smallMotor, largeMotor, power);
    }

    internal string Format(int pending, long headAgeTicks, bool usb) =>
        Capture().Format(pending, headAgeTicks, usb);

    internal readonly record struct Snapshot(
        long accepted,
        long attempts,
        long retries,
        long completed,
        long rejected,
        long highWater,
        long queueMax,
        long ownerMax,
        long completionMax,
        long rumbleReports,
        long triggerReports,
        long stopReports,
        long lastStopRevision,
        long lastStopCompletedRevision,
        long lastStopCompletionAge,
        byte flag0,
        byte flag1,
        byte smallMotor,
        byte largeMotor,
        byte power)
    {
        internal string Format(int pending, long headAgeTicks, bool usb) =>
            FormattableString.Invariant(
                $"nativePhysicalAccepted={accepted} nativePhysicalPending={pending} nativePhysicalHighWater={highWater} nativePhysicalAdmissionRetries={rejected} nativePhysicalAttempts={attempts} nativePhysicalRetries={retries} nativePhysicalCompleted={completed} nativePhysicalHeadAgeMs={Ms(headAgeTicks):F2} nativePhysicalQueueAgeMaxMs={Ms(queueMax):F2} nativePhysicalOwnerMaxMs={Ms(ownerMax):F2} nativePhysicalCompletionAgeMaxMs={Ms(completionMax):F2} nativePhysicalCompletionScope={(usb ? "USB-write" : "BT-owner-handoff")} nativeRumbleReports={rumbleReports} nativeTriggerReports={triggerReports} nativeMotorStops={stopReports} nativeLastStopRevision={lastStopRevision} nativeLastStopCompletedRevision={lastStopCompletedRevision} nativeLastStopCompletionAgeMs={Ms(lastStopCompletionAge):F2} nativeLastFlags=0x{flag0:X2}/0x{flag1:X2} nativeLastMotors={smallMotor}/{largeMotor} nativeLastPower=0x{power:X2}");
    }

    private static double Ms(long ticks) => Math.Max(0, ticks) * 1000.0 / Stopwatch.Frequency;
}
