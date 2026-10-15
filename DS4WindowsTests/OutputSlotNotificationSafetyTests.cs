using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4WinWPF.DS4Control;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class OutputSlotNotificationSafetyTests
{
    [TestMethod]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void InvalidSlotIndicesAreRejectedBeforeReadingState(int index)
    {
        var manager = new OutputSlotManager();
        Assert.AreEqual("slotNum", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => manager.SlotAvailable(index)).ParamName);
        Assert.AreEqual("slotNum", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => manager.GetOutSlotDevice(index)).ParamName);
    }

    [TestMethod]
    public void DetachedBindingObserversReadCommittedStateAndOneFailureDoesNotSkipOthers()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[1];
        var output = new FakeOutput();
        var slot = manager.DeferredPlugin(output, 0, "First", bound, OutContType.ViiperX360);
        bool readSucceeded = false;
        int laterObserver = 0;
        slot.CurrentInputBoundChanged += (_, _) =>
        {
            readSucceeded = manager.GetOutSlotDevice(output) == null && manager.NumAttachedDevices == 0 &&
                slot.InputIndex == OutSlotDevice.INPUT_INDEX_DEFAULT && slot.InputDisplayString == "";
            throw new InvalidOperationException("Synthetic binding observer failure");
        };
        slot.CurrentInputBoundChanged += (_, _) => laterObserver++;
        try
        {
            manager.DeferredRemoval(output, 0, bound);
            Assert.IsTrue(readSucceeded, "Binding observers must run after the writer lock is released.");
            Assert.AreEqual(1, laterObserver);
        }
        finally { manager.Stop(); }
    }

    [TestMethod]
    public void ManagerObserverFailureDoesNotSkipRemainingSubscribers()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[1];
        var output = new FakeOutput();
        manager.DeferredPlugin(output, 0, "First", bound, OutContType.ViiperX360);
        int laterObserver = 0;
        manager.SlotUnassigned += (_, _, _) => throw new InvalidOperationException("Synthetic observer failure");
        manager.SlotUnassigned += (_, _, _) => laterObserver++;
        try
        {
            manager.DeferredRemoval(output, 0, bound);
            Assert.AreEqual(1, laterObserver);
        }
        finally { manager.Stop(); }
    }

    [TestMethod]
    public void FailedFirstStopStillRetiresEveryHealthyLaterSlotAndCanRetry()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[3];
        var outputs = new[] { new FakeOutput { FailDisconnect = true }, new FakeOutput(), new FakeOutput() };
        for (int i = 0; i < outputs.Length; i++) manager.DeferredPlugin(outputs[i], i, "Test", bound, OutContType.ViiperX360);
        try
        {
            Assert.ThrowsExactly<IOException>(() => manager.Stop());
            Assert.AreEqual(1, manager.NumAttachedDevices);
            Assert.AreSame(outputs[0], manager.GetOutSlotDevice(outputs[0]).OutputDevice);
            Assert.IsNull(manager.GetOutSlotDevice(outputs[1]));
            Assert.IsNull(manager.GetOutSlotDevice(outputs[2]));
            outputs[0].FailDisconnect = false;
            manager.Stop();
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, outputs.Select(o => o.DisconnectAttempts).ToArray());
        }
        finally { outputs[0].FailDisconnect = false; manager.Stop(); }
    }

    [TestMethod]
    public void ShutdownObserverCannotCreateAnUntrackedReplacementAndNextStartCanAttach()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[1];
        var output = new FakeOutput();
        var replacement = new FakeOutput();
        manager.DeferredPlugin(output, 0, "First", bound, OutContType.ViiperX360);
        OutSlotDevice reentrant = null;
        OutputSlotManager.SlotUnassignedDelegate handler = (_, _, _) =>
            reentrant = manager.DeferredPlugin(replacement, 0, "Replacement", bound, OutContType.ViiperX360);
        manager.SlotUnassigned += handler;
        try
        {
            manager.Stop();
            Assert.IsNull(reentrant);
            Assert.AreEqual(0, manager.NumAttachedDevices);
            Assert.AreEqual(0, replacement.ConnectAttempts);
            manager.SlotUnassigned -= handler;
            Assert.IsNotNull(manager.DeferredPlugin(replacement, 0, "Next start", bound, OutContType.ViiperX360));
        }
        finally { manager.SlotUnassigned -= handler; manager.Stop(); }
    }

    [TestMethod]
    public void MultipleFailedOutputsAreAllAttemptedAndRemainRetryable()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[3];
        var outputs = new[] { new FakeOutput { FailDisconnect = true }, new FakeOutput(), new FakeOutput { FailDisconnect = true } };
        for (int i = 0; i < outputs.Length; i++) manager.DeferredPlugin(outputs[i], i, "Test", bound, OutContType.ViiperX360);
        try
        {
            var error = Assert.ThrowsExactly<AggregateException>(() => manager.Stop());
            Assert.AreEqual(2, error.InnerExceptions.Count);
            Assert.AreEqual(2, manager.NumAttachedDevices);
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, outputs.Select(o => o.DisconnectAttempts).ToArray());
            foreach (var output in outputs) output.FailDisconnect = false;
            manager.Stop();
            Assert.AreEqual(0, manager.NumAttachedDevices);
        }
        finally { foreach (var output in outputs) output.FailDisconnect = false; manager.Stop(); }
    }

    [TestMethod]
    public void ReentrantReplacementSurvivesTheOldServicesFinalCleanup()
    {
        var service = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        var manager = new OutputSlotManager();
        service.outputDevices = new OutputDevice[ControlService.MAX_DS4_CONTROLLER_COUNT];
        typeof(ControlService).GetField("outputslotMan", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, manager);
        var output = new FakeOutput();
        var replacement = new FakeOutput();
        manager.DeferredPlugin(output, 0, "First", service.outputDevices, OutContType.ViiperX360);
        var physical = new DS4Device((HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice)), "Synthetic input");
        bool previousInputOnly = Global.useDInputOnly[0];
        OutContType previousType = Global.activeOutDevType[0];
        OutputSlotManager.SlotUnassignedDelegate handler = (_, _, _) =>
        {
            manager.DeferredPlugin(replacement, 0, "Replacement", service.outputDevices, OutContType.ViiperX360);
            Global.activeOutDevType[0] = OutContType.ViiperX360;
            Global.useDInputOnly[0] = false;
        };
        manager.SlotUnassigned += handler;
        try
        {
            Global.useDInputOnly[0] = false;
            typeof(ControlService).GetMethod("UnplugOutDevCore", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, new object[] { 0, physical, false, false });
            Assert.AreSame(replacement, service.outputDevices[0]);
            Assert.IsFalse(Global.useDInputOnly[0]);
            Assert.AreEqual(OutContType.ViiperX360, Global.activeOutDevType[0]);
        }
        finally
        {
            // Unsubscribe the rebind callback before final cleanup.
            manager.SlotUnassigned -= handler;
            manager.Stop();
            physical.ReadWaitEv.Dispose();
            Global.useDInputOnly[0] = previousInputOnly;
            Global.activeOutDevType[0] = previousType;
        }
    }

    [TestMethod]
    public void ConcurrentCommitsNotifyInOrderWithoutHoldingTheManagerLock()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[2];
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var observed = new ConcurrentQueue<int>();
        manager.SlotAssigned += (_, index, _) =>
        {
            if (index == 0) { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10))); }
            observed.Enqueue(index);
        };
        Task first = Task.Run(() => manager.DeferredPlugin(new FakeOutput(), 0, "First", bound, OutContType.ViiperX360));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            manager.DeferredPlugin(new FakeOutput(), 1, "Second", bound, OutContType.ViiperX360);
        }
        finally
        {
            release.Set();
            Assert.IsTrue(first.Wait(TimeSpan.FromSeconds(10)));
            manager.Stop();
        }
        CollectionAssert.AreEqual(new[] { 0, 1 }, observed.ToArray());
    }

    [TestMethod]
    public void BindingReleaseAndAdoptionObserversCanQueryTheManager()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[1];
        var output = new FakeOutput();
        var slot = manager.DeferredPlugin(output, 0, "First", bound, OutContType.ViiperX360);
        int observed = 0;
        slot.CurrentInputBoundChanged += (_, _) =>
        {
            Assert.AreSame(slot, manager.GetOutSlotDevice(output));
            Assert.AreEqual(bound[0] != null, manager.IsExactBoundOutput(output, 0));
            observed++;
        };
        try
        {
            Assert.IsTrue(manager.TryReleaseBoundOutput(output, bound, 0));
            Assert.IsTrue(manager.TryBindExistingUnboundOutput(slot, bound, 0, "Again", OutContType.ViiperX360, out _));
            Assert.AreEqual(2, observed);
        }
        finally { manager.Stop(); }
    }

    private sealed class FakeOutput : OutputDevice
    {
        internal bool FailDisconnect;
        internal int ConnectAttempts;
        internal int DisconnectAttempts;
        public override void Connect() { ConnectAttempts++; connected = true; }
        public override void Disconnect()
        {
            DisconnectAttempts++;
            if (FailDisconnect) throw new IOException("Synthetic disconnect failure");
            connected = false;
        }
        public override void RemoveFeedbacks() { }
        public override string GetDeviceType() => OutContType.ViiperX360.ToString();
        public override void ResetState(bool submit = true) { }
        public override void RemoveFeedback(int inIdx) { }
        public override void ConvertandSendReport(DS4State state, int device) { }
    }
}
