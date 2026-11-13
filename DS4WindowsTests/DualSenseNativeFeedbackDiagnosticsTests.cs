using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class DualSenseNativeFeedbackDiagnosticsTests
{
    [TestMethod]
    public void RetryKeepsOriginalAgeAndStopReceiptUntilSuccessfulUsbWrite()
    {
        var device = OfflineDevice();
        byte[] stop = Report(3, 0);
        bool accept = false;
        device.PhysicalRawOutputWriteTestHook = _ => accept;
        Assert.IsTrue(device.WriteRawOutputReportFromGame(stop, 0, 48, out long revision));
        var times = (long[])typeof(DualSenseDevice).GetField("physicalOutputCommandQueuedAt",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        long queuedAt = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        times[0] = queuedAt;
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Retry,
            device.ProcessNextPhysicalOutputCommand());
        string retry = device.FormatNativeFeedbackHealth();
        StringAssert.Contains(retry, "nativePhysicalPending=1 ");
        StringAssert.Contains(retry, "nativePhysicalRetries=1 ");
        StringAssert.Contains(retry, $"nativeLastStopRevision={revision} ");
        StringAssert.Contains(retry, "nativeLastStopCompletedRevision=0 ");
        Assert.AreEqual(queuedAt, times[0]);

        accept = true;
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published,
            device.ProcessNextPhysicalOutputCommand());
        string success = device.FormatNativeFeedbackHealth();
        StringAssert.Contains(success, "nativePhysicalPending=0 ");
        StringAssert.Contains(success, "nativePhysicalAttempts=2 ");
        StringAssert.Contains(success, "nativePhysicalCompleted=1 ");
        StringAssert.Contains(success, $"nativeLastStopCompletedRevision={revision} ");
        StringAssert.Contains(success, "nativePhysicalCompletionScope=USB-write ");
    }

    [TestMethod]
    public void FullQueueAdmissionRetryIsNotCountedAsDeliveredOrDroppedStop()
    {
        var device = OfflineDevice();
        byte[] rumble = Report(3, 120);
        for (int i = 0; i < 64; i++)
            Assert.IsTrue(device.WriteRawOutputReportFromGame(rumble, 0, 48));
        Assert.IsFalse(device.WriteRawOutputReportFromGame(Report(3, 0), 0, 48));
        string text = device.FormatNativeFeedbackHealth();
        StringAssert.Contains(text, "nativePhysicalAccepted=64 ");
        StringAssert.Contains(text, "nativePhysicalHighWater=64 ");
        StringAssert.Contains(text, "nativePhysicalAdmissionRetries=1 ");
        StringAssert.Contains(text, "nativePhysicalCompleted=0 ");
        StringAssert.Contains(text, "nativeMotorStops=0 ");
    }

    [TestMethod]
    public void DeterministicTimingsSeparateQueueWaitOwnerDurationAndCompletionAge()
    {
        var diagnostics = new DualSenseNativeFeedbackDiagnostics();
        long second = Stopwatch.Frequency;
        byte[] stop = Report(3, 0);
        diagnostics.Admit(stop, 19, 3);
        diagnostics.Attempt(stop, 19, second, second * 2, second * 3, false);
        diagnostics.Attempt(stop, 19, second, second * 4, second * 6, true);
        string text = diagnostics.Format(0, 0, true);
        StringAssert.Contains(text, "nativePhysicalQueueAgeMaxMs=3000.00 ");
        StringAssert.Contains(text, "nativePhysicalOwnerMaxMs=2000.00 ");
        StringAssert.Contains(text, "nativePhysicalCompletionAgeMaxMs=5000.00 ");
        StringAssert.Contains(text, "nativeLastStopCompletionAgeMs=5000.00 ");
        StringAssert.Contains(text, "nativeLastStopCompletedRevision=19 ");
        StringAssert.Contains(diagnostics.Format(0, 0, false),
            "nativePhysicalCompletionScope=BT-owner-handoff ");
    }

    [TestMethod]
    public void ZeroMotorBytesInTriggerOnlyOrAudioModeAreNotReportedAsExplicitMotorStops()
    {
        Assert.IsTrue(DualSenseNativeFeedbackDiagnostics.IsExplicitMotorStop(Report(3, 0)));
        byte[] improved = Report(2, 0);
        improved[39] = 4;
        Assert.IsTrue(DualSenseNativeFeedbackDiagnostics.IsExplicitMotorStop(improved));
        Assert.IsFalse(DualSenseNativeFeedbackDiagnostics.IsExplicitMotorStop(Report(0x0E, 0)));
        Assert.IsFalse(DualSenseNativeFeedbackDiagnostics.IsExplicitMotorStop(Report(0, 0)));
        Assert.IsFalse(DualSenseNativeFeedbackDiagnostics.IsExplicitMotorStop(Report(3, 1)));
        var diagnostics = new DualSenseNativeFeedbackDiagnostics();
        diagnostics.Admit(Report(0x0E, 0), 1, 1);
        string text = diagnostics.Format(1, Stopwatch.Frequency, false);
        StringAssert.Contains(text, "nativeTriggerReports=1 ");
        StringAssert.Contains(text, "nativeMotorStops=0 ");
        StringAssert.Contains(text, "nativeLastFlags=0x0E/0x40 ");
        StringAssert.Contains(text, "nativePhysicalHeadAgeMs=1000.00 ");
        diagnostics.Admit(improved, 2, 2);
        text = diagnostics.Format(2, 0, false);
        StringAssert.Contains(text, "nativeRumbleReports=1 ");
        StringAssert.Contains(text, "nativeMotorStops=1 ");
    }

    [TestMethod]
    public void RecordingAndUsbPolicyDoNotAllocatePerCommandAfterWarmup()
    {
        var diagnostics = new DualSenseNativeFeedbackDiagnostics();
        byte[] report = Report(3, 100);
        byte[] envelope = new byte[76];
        report.CopyTo(envelope, 28);
        for (int i = 0; i < 100; i++) Record();
        long allocated;
        // Use the suite's strict measurement scope: full-suite background GCs
        // can repair allocation contexts while the counter window is open.
        // Keep the zero-allocation requirement and framework assertions outside.
        using (StrictAllocationMeasurementScope.Begin())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) Record();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.AreEqual(0L, allocated);
        void Record()
        {
            diagnostics.Admit(report, 1, 1);
            diagnostics.Attempt(report, 1, 1, 2, 3, true);
            _ = DualSenseNativeUsbStatePolicy.ClassifyEnvelope(envelope);
        }
    }

    internal static byte[] Report(byte flags, byte strength)
    {
        var report = new byte[48];
        report[0] = 2;
        report[1] = flags;
        report[2] = 0x40;
        report[3] = report[4] = strength;
        return report;
    }

    internal static DualSenseDevice OfflineDevice(ConnectionType connection = ConnectionType.USB)
    {
        var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
        var device = new DualSenseDevice(hid, "Offline native feedback diagnostics");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(DS4Device).GetField("conType", flags)!.SetValue(device, connection);
        typeof(DS4Device).GetField("outputReport", flags)!.SetValue(device, new byte[48]);
        return device;
    }
}
