using DS4Windows;
using DS4Windows.InputDevices;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DS4WindowsTests;

[TestClass]
public sealed class DualSenseMultiControllerSimulationTests
{
    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    public void StalledAudioStartAndStopInOneSlotDoNotBlockAnotherSlot(int blockedSlot, int otherSlot)
    {
        using var manager = new DualSenseAudioPassthrough();
        using var blocked = new ManualResetEvent(false);
        using var release = new ManualResetEvent(false);
        int thread = Environment.CurrentManagedThreadId;
        int otherAttempts = 0;
        Task stopping = null;
        Task otherOperation = null;
        ViiperOutDevice FirstSource()
        {
            if (Environment.CurrentManagedThreadId != thread)
            {
                blocked.Set();
                release.WaitOne();
            }
            return null;
        }
        ViiperOutDevice OtherSource() { Interlocked.Increment(ref otherAttempts); return null; }
        void Start(int slot, Func<ViiperOutDevice> resolver) => manager.Start(slot,
            Device(), 0, DualSenseSpeakerCompression.Off, 0, "", "",
            OutContType.ViiperDualSense, directSpeakerSourceResolver: resolver);
        try
        {
            Start(blockedSlot, FirstSource);
            Assert.IsTrue(blocked.WaitOne(2000));
            otherOperation = Task.Run(() => Start(otherSlot, OtherSource));
            Assert.IsTrue(otherOperation.Wait(2000), "Other slot admission waited behind the first controller.");
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref otherAttempts) >= 3, 2000),
                "One pending controller's source resolver blocked another slot's retries.");
            otherOperation = Task.Run(() => manager.Stop(otherSlot));
            Assert.IsTrue(otherOperation.Wait(2000), "Other slot retirement waited behind the first controller.");
            Assert.AreEqual(ControllerRuntimeLaneState.Starting, manager.GetStatus(blockedSlot));
            int beforeRestart = Volatile.Read(ref otherAttempts);
            otherOperation = Task.Run(() => Start(otherSlot, OtherSource));
            Assert.IsTrue(otherOperation.Wait(2000), "Other slot admission waited behind the first controller.");
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref otherAttempts) >= beforeRestart + 2, 2000));
            stopping = Task.Run(() => manager.Stop(blockedSlot));
            Assert.IsFalse(stopping.Wait(50));
            int beforeRetry = Volatile.Read(ref otherAttempts);
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref otherAttempts) > beforeRetry, 1500),
                "Joining the first slot's start blocked the other controller.");
        }
        finally
        {
            release.Set();
            if (otherOperation != null) Assert.IsTrue(otherOperation.Wait(3000));
            if (stopping != null) Assert.IsTrue(stopping.Wait(3000));
            manager.Stop(blockedSlot);
            manager.Stop(otherSlot);
        }
    }

    [TestMethod]
    public void DiagnosticDs4PersonaWithAudioDisabledRemovesSilentCarrier()
    {
        Assert.IsTrue(ControlService.RequiresDualSenseBluetoothMediaCarrier(
            ConnectionType.BT, false, false, OutContType.ViiperDualSense));
        Assert.IsFalse(ControlService.RequiresDualSenseBluetoothMediaCarrier(
            ConnectionType.BT, false, false, OutContType.ViiperDS4));
        Assert.IsTrue(ControlService.RequiresDualSenseBluetoothMediaCarrier(
            ConnectionType.BT, false, true, OutContType.ViiperDS4),
            "The comparison also needs Audio Haptics disabled.");
    }

    private static DualSenseDevice Device()
    {
        var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
        var device = new DualSenseDevice(hid, "Simulated second controller; no radio");
        typeof(DS4Device).GetField("conType", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(device, ConnectionType.BT);
        return device;
    }
}
