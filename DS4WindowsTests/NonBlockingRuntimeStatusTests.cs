using DS4Windows;
using System.Reflection;

namespace DS4WindowsTests;

[TestClass]
public class NonBlockingRuntimeStatusTests
{
    [TestMethod]
    public void BusyRuntimeKeepsLastCoherentStatusAndRefreshesAfterRelease()
    {
        var gate = new object();
        var cache = new NonBlockingRuntimeStatus<ControllerRuntimeLaneState>(ControllerRuntimeLaneState.Starting);
        Assert.AreEqual(ControllerRuntimeLaneState.Ready, cache.Read(gate, () => ControllerRuntimeLaneState.Ready));
        WhileHeld(gate, () => cache.Read(gate, () => throw new AssertFailedException("Busy runtime must not be read.")),
            value => Assert.AreEqual(ControllerRuntimeLaneState.Ready, value));
        Assert.AreEqual(ControllerRuntimeLaneState.Unavailable, cache.Read(gate, () => ControllerRuntimeLaneState.Unavailable));
        Assert.IsFalse(System.Threading.Monitor.IsEntered(gate));
    }

    [TestMethod]
    public void ColdBusySlotReportsStartingAndOtherSlotsRemainIndependent()
    {
        var gates = new[] { new object(), new object() };
        var caches = NonBlockingRuntimeStatus<ControllerRuntimeLaneState>.CreateSlots(2, ControllerRuntimeLaneState.Starting);
        caches[1].Read(gates[1], () => ControllerRuntimeLaneState.Ready);
        WhileHeld(gates[0], () => caches[0].Read(gates[0], () => ControllerRuntimeLaneState.Unavailable),
            value => Assert.AreEqual(ControllerRuntimeLaneState.Starting, value));
        Assert.AreEqual(ControllerRuntimeLaneState.Ready, caches[1].Read(gates[1], () => ControllerRuntimeLaneState.Ready));
    }

    [TestMethod]
    public void FailedObservationReleasesGateAndPreservesPreviousSnapshot()
    {
        var gate = new object();
        var cache = new NonBlockingRuntimeStatus<AudioHapticsRuntimeStatus>(AudioHapticsRuntimeStatus.Starting);
        cache.Read(gate, () => AudioHapticsRuntimeStatus.Running);
        Assert.ThrowsExactly<InvalidOperationException>(() => cache.Read(gate, () => throw new InvalidOperationException()));
        Assert.IsFalse(System.Threading.Monitor.IsEntered(gate));
        WhileHeld(gate, () => cache.Read(gate, () => AudioHapticsRuntimeStatus.Inactive), value => Assert.IsTrue(value.Active));
        Assert.IsFalse(cache.Read(gate, () => AudioHapticsRuntimeStatus.Inactive).Active);
    }

    [TestMethod]
    [DataRow(typeof(DualSenseAudioPassthrough))]
    [DataRow(typeof(DualShock4AudioPassthrough))]
    [DataRow(typeof(AudioHapticsService))]
    public void ActualAudioStatusGetterDoesNotWaitForItsRuntimeGate(Type serviceType)
    {
        using var service = (IDisposable)Activator.CreateInstance(serviceType)!;
        var gates = serviceType.GetField(serviceType == typeof(AudioHapticsService) ? "slotLocks" : "syncRoot",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        object gate = gates is object[] slots ? slots[0] : gates;
        var read = serviceType.GetMethod("GetStatus")!;
        WhileHeld(gate, () => read.Invoke(service, new object[] { 0 }), value =>
        {
            if (value is ControllerRuntimeLaneState state) Assert.AreEqual(ControllerRuntimeLaneState.Starting, state);
            else Assert.AreEqual(AudioHapticsRuntimeStatus.Starting.Message, ((AudioHapticsRuntimeStatus)value!).Message);
        });
        object fresh = read.Invoke(service, new object[] { 0 })!;
        if (fresh is ControllerRuntimeLaneState state) Assert.AreEqual(ControllerRuntimeLaneState.Unavailable, state);
        else Assert.AreEqual(AudioHapticsRuntimeStatus.Inactive.Message, ((AudioHapticsRuntimeStatus)fresh).Message);
    }

    internal static void WhileHeld<T>(object gate, Func<T> read, Action<T> verify)
    {
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { lock (gate) { held.Set(); release.Wait(TimeSpan.FromSeconds(10)); } }) { IsBackground = true };
        holder.Start();
        Assert.IsTrue(held.Wait(TimeSpan.FromSeconds(3)), "Test runtime did not acquire its gate.");
        Task<T> observation = null;
        try
        {
            observation = Task.Run(read);
            Assert.IsTrue(observation.Wait(TimeSpan.FromSeconds(1)), "Status polling waited for runtime work; this would freeze the UI.");
            verify(observation.GetAwaiter().GetResult());
        }
        finally { release.Set(); holder.Join(TimeSpan.FromSeconds(3)); observation?.GetAwaiter().GetResult(); }
    }
}
