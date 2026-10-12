using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public class DualSenseReusedSpeakerSessionTests
{
    [TestMethod]
    public void LatePreviousSourceAcknowledgementsCannotStrandPartialWarmup()
    {
        const long latePresented = 7489;
        int prime = DualSenseBluetoothAudioPacer.NativePrimeReportCount;
        for (int remaining = prime; remaining > 0; remaining--)
        {
            for (int pending = 0; pending < prime; pending++)
                Assert.IsFalse(DualSenseBluetoothSpeakerPassthrough.ShouldBackpressurePacerProducer(
                    true, pending, false, latePresented, remaining));
            Assert.IsTrue(DualSenseBluetoothSpeakerPassthrough.ShouldBackpressurePacerProducer(
                true, prime, false, latePresented, remaining), "Startup must remain bounded.");
        }
        Assert.IsTrue(DualSenseBluetoothSpeakerPassthrough.ShouldBackpressurePacerProducer(
            true, 1, false, latePresented, 0), "Completed legacy startup keeps its one-report limit.");
        Assert.IsFalse(DualSenseBluetoothSpeakerPassthrough.ShouldBackpressurePacerProducer(
            true, prime - 1, true, latePresented, 0), "V5's existing reservoir is unchanged.");
    }

    [TestMethod]
    [DataRow(7489L, false)]
    [DataRow(12521L, false)]
    [DataRow(7489L, true)]
    [DataRow(0L, false)]
    public void ActiveHelperLifecycleOpensGateWithCurrentSourcePresentationBoundary(
        long previousPresented, bool usesV5Source)
    {
        // No HID, child process, or pipe is opened. Exercise the real lifecycle
        // loop, not just the subtraction policy that previously missed this bug.
        var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
        var device = new DualSenseDevice(hid, "Reused speaker session regression");
        var pacer = (DualSenseBluetoothAudioPacer)RuntimeHelpers.GetUninitializedObject(
            typeof(DualSenseBluetoothAudioPacer));
        Set(pacer, "stateLock", new object());
        Set(pacer, "presentedReports", previousPresented);
        Set(pacer, "currentEpoch", 6);
        Set(pacer, "realtimeHapticsGeneration", 10);
        Set(device, "bluetoothAudioPacer", pacer);
        var source = new DualSenseBluetoothSpeakerPassthrough(device, 255,
            DualSenseSpeakerCompression.Off, 0, string.Empty,
            ControllerAudioEndpointKind.Any);
        Set(source, "directSpeakerUsesV5Source", usesV5Source);
        Set(source, "pacerPrewarmRequested", 1);
        Set(source, "pacerLifecycleGateSource", 1);
        var released = Get<AutoResetEvent>(source, "captureFramesAvailable");
        var wake = Get<AutoResetEvent>(source, "pacerLifecycleRequested");
        var loop = typeof(DualSenseBluetoothSpeakerPassthrough).GetMethod(
            "PacerLifecycleLoop", BindingFlags.Instance | BindingFlags.NonPublic);
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { loop.Invoke(source, null); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.Start();
        try
        {
            Assert.IsTrue(released.WaitOne(3000), "Active-helper source gate never opened.");
            Assert.AreEqual(0, Get<int>(source, "pacerLifecycleGateSource"));
            long baseline = Get<long>(source, "pacerPresentedReportsBaseline");
            Assert.AreEqual(previousPresented, baseline,
                "A reused helper's lifetime count was mistaken for the new source's prime.");
            long presented = DualSenseBluetoothSpeakerPassthrough.
                CalculatePacerPresentedReportsSinceBaseline(previousPresented, baseline);
            for (int pending = 1; pending < DualSenseBluetoothAudioPacer.NativePrimeReportCount; pending++)
                Assert.IsFalse(DualSenseBluetoothSpeakerPassthrough.ShouldBackpressurePacerProducer(
                    true, pending, usesV5Source, presented), "The helper still needs a complete prime.");
            Assert.IsTrue(DualSenseBluetoothSpeakerPassthrough.ShouldBackpressurePacerProducer(
                true, DualSenseBluetoothAudioPacer.NativePrimeReportCount, usesV5Source, presented));
            Assert.AreEqual(6, Get<int>(pacer, "currentEpoch"), "Source gate must not clear native output.");
            Assert.AreEqual(10, Get<int>(pacer, "realtimeHapticsGeneration"));
            Assert.AreEqual(0L, Get<long>(source, "pacerPrewarmAttempts"), "Reuse must not restart the helper.");
        }
        finally
        {
            Set(source, "pacerLifecycleStopping", 1);
            wake.Set();
            Assert.IsTrue(thread.Join(3000), "Lifecycle test worker did not exit.");
            Set(device, "bluetoothAudioPacer", null);
            source.Dispose();
        }
        Assert.IsNull(failure);
    }

    private static void Set(object target, string name, object value) =>
        Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException("Missing diagnostic field " + name);
}
