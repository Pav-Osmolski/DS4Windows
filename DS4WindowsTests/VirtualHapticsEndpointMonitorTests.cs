using DS4Windows;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace DS4WindowsTests;

[TestClass]
public class VirtualHapticsEndpointMonitorTests
{
    [TestMethod]
    public void StalePropertyStoresDoNotHideTheCurrentEndpointsDisabledStateOrRecovery()
    {
        DeviceState current = DeviceState.Disabled;
        Func<DeviceState?>[] readers = [
            () => throw new System.Runtime.InteropServices.COMException("Stale endpoint", unchecked((int)0xE000020B)),
            () => null, // An unrelated readable endpoint is excluded by the port match.
            () => current,
            () => throw new System.Runtime.InteropServices.COMException("Another stale endpoint")
        ];
        Assert.AreEqual(true, VirtualHapticsEndpointMonitor.EvaluateEndpointReaders(readers));
        current = DeviceState.Active;
        Assert.AreEqual(false, VirtualHapticsEndpointMonitor.EvaluateEndpointReaders(readers));
        Assert.IsNull(VirtualHapticsEndpointMonitor.EvaluateEndpointReaders(readers.Take(2)), "Unreadable entries alone are not evidence of a disabled controller.");
        Assert.IsNull(VirtualHapticsEndpointMonitor.EvaluateEndpointReaders(readers.Append(() => DeviceState.Disabled)), "Ambiguous exact-port matches must remain unknown.");
    }

    [TestMethod]
    public void OnlyOneConfirmedDisabledPlaybackEndpointProducesWarning()
    {
        Assert.AreEqual(true, VirtualHapticsEndpointMonitor.EvaluateStates([DeviceState.Disabled]));
        Assert.AreEqual(false, VirtualHapticsEndpointMonitor.EvaluateStates([DeviceState.Active]));
        Assert.IsNull(VirtualHapticsEndpointMonitor.EvaluateStates([]));
        Assert.IsNull(VirtualHapticsEndpointMonitor.EvaluateStates([DeviceState.NotPresent]));
        Assert.IsNull(VirtualHapticsEndpointMonitor.EvaluateStates([DeviceState.Disabled | DeviceState.NotPresent]));
        Assert.IsNull(VirtualHapticsEndpointMonitor.EvaluateStates([DeviceState.Disabled | DeviceState.Active]));
        Assert.IsNull(VirtualHapticsEndpointMonitor.EvaluateStates([DeviceState.Unplugged]));
        Assert.IsNull(VirtualHapticsEndpointMonitor.EvaluateStates([DeviceState.Disabled, DeviceState.Active]));
    }

    [TestMethod]
    public void SlowDiscoveryDoesNotBlockStatusAndRepeatedReadsDoNotQueueWork()
    {
        long now = 0;
        int calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var monitor = new VirtualHapticsEndpointMonitor(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return true;
        }, () => Interlocked.Read(ref now));
        Assert.IsFalse(monitor.IsDisabled(1, 10));
        Assert.AreEqual(0, calls, "Do not probe during startup grace.");
        Interlocked.Exchange(ref now, 2000);
        try
        {
            Assert.IsFalse(monitor.IsDisabled(1, 10));
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            var elapsed = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) Assert.IsFalse(monitor.IsDisabled(1, 10));
            Assert.IsLessThan(500L, elapsed.ElapsedMilliseconds);
            Assert.AreEqual(1, calls);
        }
        finally { release.Set(); }
        Assert.IsTrue(SpinWait.SpinUntil(() => monitor.IsDisabled(1, 10), 5000));
        Assert.AreEqual(1, calls, "Cached status must not cause a discovery on every UI poll.");
    }

    [TestMethod]
    public void ReenabledOrUnreadableEndpointClearsDisabledWarning()
    {
        long now = 0;
        bool throwProbe = false;
        bool disabled = true;
        var monitor = new VirtualHapticsEndpointMonitor(_ =>
            throwProbe ? throw new InvalidOperationException("Endpoint removed") : disabled,
            () => Interlocked.Read(ref now));
        monitor.IsDisabled(1, 1);
        Interlocked.Exchange(ref now, 2000);
        Assert.IsTrue(SpinWait.SpinUntil(() => monitor.IsDisabled(1, 1), 5000));
        disabled = false;
        Interlocked.Exchange(ref now, 4000);
        Assert.IsTrue(SpinWait.SpinUntil(() => !monitor.IsDisabled(1, 1), 5000));
        disabled = true;
        Interlocked.Exchange(ref now, 6000);
        Assert.IsTrue(SpinWait.SpinUntil(() => monitor.IsDisabled(1, 1), 5000));
        throwProbe = true;
        Interlocked.Exchange(ref now, 8000);
        Assert.IsTrue(SpinWait.SpinUntil(() => !monitor.IsDisabled(1, 1), 5000));
        Assert.IsFalse(monitor.IsDisabled(-1, 1));
    }

    [TestMethod]
    [DataRow(2, 10)]
    [DataRow(1, 11)]
    public void ReconnectOrDifferentPortRejectsLateObservation(int newPort, int newGeneration)
    {
        long now = 0;
        int calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var monitor = new VirtualHapticsEndpointMonitor(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return true;
            }
            return false;
        }, () => Interlocked.Read(ref now));
        monitor.IsDisabled(1, 10);
        Interlocked.Exchange(ref now, 2000);
        monitor.IsDisabled(1, 10);
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(monitor.IsDisabled(newPort, newGeneration));
        }
        finally { release.Set(); }
        Interlocked.Exchange(ref now, 4000);
        Assert.IsTrue(SpinWait.SpinUntil(() =>
        {
            Assert.IsFalse(monitor.IsDisabled(newPort, newGeneration), "Old output must not warn for the new one.");
            return Volatile.Read(ref calls) == 2;
        }, 5000));
    }
}
