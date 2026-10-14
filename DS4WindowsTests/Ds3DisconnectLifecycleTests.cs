using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DS4Windows.InputDevices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4Windows.Tests;

[TestClass]
public sealed class Ds3DisconnectLifecycleTests
{
    [TestMethod]
    public void LostTransportWithStoppedInputDoesNotRequirePhysicalNeutral()
    {
        using var fixture = new Fixture();
        fixture.LoseTransport();
        fixture.FinishInput();
        var result = fixture.Device.TryStopUpdateBoundedCore(1_000);
        Assert.IsTrue(result.Succeeded, result.FailureKind.ToString());
        Assert.AreEqual(0, fixture.OutputWrites);
    }

    [TestMethod]
    public void LostTransportCannotHideAStillRunningInputWorker()
    {
        using var fixture = new Fixture();
        fixture.LoseTransport();
        Assert.AreEqual(DS4DeviceWorkerLifecycleFailureKind.StopTimedOut,
            fixture.Device.TryStopOutputUpdateBounded(10).FailureKind,
            "Even a direct output finalizer call must prove the input worker exited.");
        var result = fixture.Device.TryStopUpdateBoundedCore(10);
        Assert.AreEqual(DS4DeviceWorkerLifecycleFailureKind.StopTimedOut, result.FailureKind);
        Assert.AreEqual(0, fixture.OutputWrites);
        fixture.FinishInput();
        Assert.IsTrue(fixture.Device.TryStopUpdateBoundedCore(1_000).Succeeded);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConnectedStopStillRequiresPhysicalNeutral(bool accepted)
    {
        using var fixture = new Fixture { AcceptOutput = accepted };
        fixture.FinishInput();
        var result = fixture.Device.TryStopUpdateBoundedCore(1_000);
        Assert.AreEqual(accepted, result.Succeeded);
        Assert.AreEqual(accepted ? DS4DeviceWorkerLifecycleFailureKind.None :
            DS4DeviceWorkerLifecycleFailureKind.OutputFinalizationUncertain, result.FailureKind);
        Assert.AreEqual(1, fixture.OutputWrites);
    }

    [TestMethod]
    public void DisconnectingFlagAloneDoesNotBypassPhysicalNeutral()
    {
        using var fixture = new Fixture();
        Field("isDisconnecting").SetValue(fixture.Device, true);
        fixture.FinishInput();
        var result = fixture.Device.TryStopUpdateBoundedCore(1_000);
        Assert.AreEqual(DS4DeviceWorkerLifecycleFailureKind.OutputFinalizationUncertain, result.FailureKind);
        Assert.AreEqual(1, fixture.OutputWrites);
    }

    [TestMethod]
    public void LostTransportRetiresExactSlotAndAllowsAReplacementConnection()
    {
        using var fixture = new Fixture();
        int removed = 0;
        var authority = new ControlServiceLegacyHidSlotAuthority(1, _ => removed++, fixture);
        Assert.IsTrue(authority.TryOpenNext(out _, out _, out _));
        Assert.IsTrue(authority.TryBindExactSlot(0, fixture.Device, false, out var binding, out _, out _));
        Assert.IsTrue(authority.TrySubscribeLegacyLifecycle(binding,
            (_, _) => { }, (_, _) => { }, (_, _) => { }, (_, _) => { }, (_, _) => { }, out _));
        Assert.IsTrue(authority.TrySubscribeReport(binding, (_, _) => { }, out _));
        Assert.IsTrue(authority.TryStartAndActivate(binding, out _, out _, out _));
        fixture.LoseTransport();
        fixture.FinishInput();
        Assert.IsTrue(authority.TryBeginRetirement(binding, out _, out _));
        Assert.IsTrue(authority.TryPublishTerminalNeutral(binding, () => { }, 1_000, out _, out _));
        Assert.IsTrue(authority.TryFinalizeRetirement(binding, 1_000, out var failure, out _), failure.ToString());
        Assert.AreEqual(1, removed);
        Assert.AreEqual(InputControllerSlotState.Removed, authority.Table.GetSnapshot()[0].State);
        Assert.AreEqual(DS4DeviceWorkerLifecycleState.Stopped, fixture.Device.WorkerLifecycleState);
        using var replacement = new Fixture();
        Assert.IsTrue(authority.TryBindExactSlot(0, replacement.Device, false, out var next, out _, out _));
        Assert.AreNotEqual(binding.ConnectionGeneration, next.ConnectionGeneration);
    }

    private static FieldInfo Field(string name) => typeof(DS4Device).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Exercise the actual DS3 output finalizer and base input join, replacing
    // only the physical HID read with a controlled input worker.
    private sealed class Fixture : IDisposable, IControlServiceLegacyHidWorkerLifecycle
    {
        private readonly ManualResetEventSlim release = new(false);
        private readonly ManualResetEventSlim loseTransport = new(false);
        private readonly ManualResetEventSlim removalRaised = new(false);
        private readonly Thread input;
        internal DS3Device Device { get; }
        internal int OutputWrites { get; private set; }
        internal bool AcceptOutput { get; set; }

        internal Fixture()
        {
            var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
            Device = new DS3Device(hid, "DS3 disconnect regression");
            Field("outputReport").SetValue(Device, new byte[64]);
            Device.PhysicalOutputWriteTestHook = _ => { OutputWrites++; return AcceptOutput; };
            Device.Removal += (_, _) => removalRaised.Set();
            input = new Thread(() =>
            {
                loseTransport.Wait();
                if (!release.IsSet)
                    typeof(DS3Device).GetMethod("HandleInputTransportLoss",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Device, null);
                release.Wait();
            }) { IsBackground = true };
            Field("ds4Input").SetValue(Device, input);
            input.Start();
            Field("inputWorkerStartCommitted").SetValue(Device, 1);
        }

        internal void LoseTransport()
        {
            loseTransport.Set();
            Assert.IsTrue(removalRaised.Wait(2_000));
        }

        internal void FinishInput()
        {
            release.Set();
            loseTransport.Set();
            Assert.IsTrue(input.Join(2_000));
        }

        public DS4DeviceWorkerLifecycleSupport Classify(DS4Device device) => device.WorkerLifecycleSupport;

        public bool TryStart(DS4Device device, out DS4DeviceWorkerLifecycleLease lease,
            out DS4DeviceWorkerLifecycleResult result)
        {
            var boundary = (DS4DeviceWorkerLifecycleBoundary)Field("workerLifecycle").GetValue(device)!;
            return boundary.TryStart(device, device.WorkerLifecycleSupport, () =>
                typeof(DS4Device).GetMethod("MarkInputWorkerStartCommitted",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(device, null), out lease, out result);
        }

        public bool TryStop(DS4Device device, in DS4DeviceWorkerLifecycleLease lease,
            int timeoutMilliseconds, out DS4DeviceWorkerLifecycleResult result) =>
            device.TryStopWorkerLifecycle(lease, timeoutMilliseconds, out result);

        public void Dispose()
        {
            FinishInput();
            Device.ReadWaitEv.Dispose();
            release.Dispose();
            loseTransport.Dispose();
            removalRaised.Dispose();
        }
    }
}
