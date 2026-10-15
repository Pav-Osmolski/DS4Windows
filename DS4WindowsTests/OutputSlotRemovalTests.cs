using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF.DS4Control;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class OutputSlotRemovalTests
{
    [TestMethod]
    [DataRow(false, -1)]
    [DataRow(true, -1)]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    public void FailedRemovalRetainsExactSlotAndBindingUntilRetrySucceeds(bool failFeedback, int input)
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[2];
        var output = new FakeOutput();
        var other = new FakeOutput();
        OutSlotDevice slot = manager.DeferredPlugin(output, input, "First", bound, OutContType.ViiperX360);
        int unassigned = 0;
        OutputSlotManager.SlotUnassignedDelegate onUnassigned = (_, index, detached) =>
        {
            Assert.IsNull(detached.OutputDevice);
            Assert.AreEqual(OutSlotDevice.AttachedStatus.UnAttached, detached.CurrentAttachedStatus);
            Assert.IsNull(manager.GetOutSlotDevice(index).OutputDevice);
            if (ReferenceEquals(detached, slot))
            {
                Assert.IsNull(manager.GetOutSlotDevice(output));
                if (input >= 0) Assert.IsNull(bound[input]);
            }
            // Signal success after assertions: subscriber exceptions are
            // intentionally isolated by the production notification path.
            unassigned++;
        };
        manager.SlotUnassigned += onUnassigned;
        if (failFeedback) output.FailFeedback = true;
        else output.FailDisconnect = true;
        try
        {
            Assert.ThrowsExactly<IOException>(() => manager.DeferredRemoval(output, input, bound));
            Assert.AreSame(slot, manager.GetOutSlotDevice(output));
            Assert.AreSame(output, slot.OutputDevice);
            Assert.AreEqual(OutSlotDevice.AttachedStatus.Attached, slot.CurrentAttachedStatus);
            Assert.AreEqual(1, manager.NumAttachedDevices);
            Assert.AreEqual(0, unassigned);
            Assert.AreEqual(failFeedback ? 0 : 1, output.DisconnectAttempts);
            if (input >= 0)
            {
                Assert.AreSame(output, bound[input]);
                Assert.IsTrue(manager.IsExactBoundOutput(output, input));
            }

            OutSlotDevice otherSlot = manager.DeferredPlugin(other, 1, "Second", bound, OutContType.ViiperX360);
            Assert.AreNotSame(slot, otherSlot, "Failed removal must not expose its slot for another output.");
            output.FailFeedback = output.FailDisconnect = false;
            manager.DeferredRemoval(output, input, bound);
            Assert.IsNull(manager.GetOutSlotDevice(output));
            Assert.AreEqual(1, manager.NumAttachedDevices);
            Assert.AreEqual(1, unassigned);
            Assert.IsTrue(manager.IsExactBoundOutput(other, 1));
            int attempts = output.DisconnectAttempts;
            manager.DeferredRemoval(output, input, bound);
            Assert.AreEqual(attempts, output.DisconnectAttempts);
            Assert.AreEqual(1, unassigned);
        }
        finally
        {
            output.FailFeedback = output.FailDisconnect = false;
            manager.SlotUnassigned -= onUnassigned;
            manager.Stop();
        }
    }

    [TestMethod]
    public void FailedStopKeepsFailedSlotRegisteredAndDoesNotRepeatSuccessfulDisconnections()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[2];
        var first = new FakeOutput();
        var second = new FakeOutput();
        manager.DeferredPlugin(first, 0, "First", bound, OutContType.ViiperX360);
        OutSlotDevice secondSlot = manager.DeferredPlugin(second, 1, "Second", bound, OutContType.ViiperX360);
        int unassigned = 0;
        manager.SlotUnassigned += (_, _, _) => unassigned++;
        second.FailDisconnect = true;
        try
        {
            Assert.ThrowsExactly<IOException>(() => manager.Stop());
            Assert.IsNull(manager.GetOutSlotDevice(first));
            Assert.AreSame(secondSlot, manager.GetOutSlotDevice(second));
            Assert.IsTrue(manager.IsExactBoundOutput(second, 1));
            Assert.AreEqual(1, manager.NumAttachedDevices);
            Assert.AreEqual(1, unassigned);
            second.FailDisconnect = false;
            manager.Stop();
            Assert.AreEqual(1, first.DisconnectAttempts);
            Assert.AreEqual(2, second.DisconnectAttempts);
            Assert.AreEqual(0, manager.NumAttachedDevices);
            Assert.IsNull(manager.GetOutSlotDevice(second));
            Assert.AreEqual(2, unassigned);
        }
        finally
        {
            second.FailDisconnect = false;
            manager.Stop();
        }
    }

    [TestMethod]
    public void SuccessfulRemovalKeepsPermanentSlotPreferenceAndAllowsFreshBinding()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[1];
        var output = new FakeOutput();
        OutSlotDevice slot = manager.DeferredPlugin(output, 0, "First", bound, OutContType.ViiperX360);
        slot.CurrentReserveStatus = OutSlotDevice.ReserveStatus.Permanent;
        try
        {
            manager.DeferredRemoval(output, 0, bound);
            Assert.AreEqual(OutSlotDevice.ReserveStatus.Permanent, slot.CurrentReserveStatus);
            Assert.AreEqual(OutContType.ViiperX360, slot.PermanentType);
            Assert.IsNull(bound[0]);
            var replacement = new FakeOutput();
            Assert.AreSame(slot, manager.DeferredPlugin(replacement, 0, "Replacement", bound,
                OutContType.ViiperX360, slot));
            Assert.IsTrue(manager.IsExactBoundOutput(replacement, 0));
        }
        finally { manager.Stop(); }
    }

    [TestMethod]
    public void ConcurrentDifferentControllerRemovalsRetireEachSlotOnce()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[2];
        var first = new FakeOutput();
        var second = new FakeOutput();
        manager.DeferredPlugin(first, 0, "First", bound, OutContType.ViiperX360);
        manager.DeferredPlugin(second, 1, "Second", bound, OutContType.ViiperX360);
        using var start = new Barrier(2);
        try
        {
            Task[] removals = new[] { (OutputDevice)first, second }.Select((output, index) => Task.Run(() =>
            {
                Assert.IsTrue(start.SignalAndWait(TimeSpan.FromSeconds(5)));
                manager.DeferredRemoval(output, index, bound);
            })).ToArray();

            Assert.IsTrue(Task.WaitAll(removals, TimeSpan.FromSeconds(10)));
            Assert.IsNull(manager.GetOutSlotDevice(first));
            Assert.IsNull(manager.GetOutSlotDevice(second));
            Assert.IsNull(bound[0]);
            Assert.IsNull(bound[1]);
            Assert.AreEqual(1, first.DisconnectAttempts);
            Assert.AreEqual(1, second.DisconnectAttempts);
        }
        finally { manager.Stop(); }
    }

    [TestMethod]
    public void ConcurrentDuplicateRemovalIsIdempotentAndObserversRunAfterUnlock()
    {
        var manager = new OutputSlotManager();
        var bound = new OutputDevice[1];
        var output = new FakeOutput();
        manager.DeferredPlugin(output, 0, "First", bound, OutContType.ViiperX360);
        OutSlotDevice assigned = manager.GetOutSlotDevice(output);
        bool detachedStateWasCommitted = false;
        assigned.CurrentInputBoundChanged += (sender, _) =>
        {
            var slot = (OutSlotDevice)sender;
            detachedStateWasCommitted = slot.OutputDevice == null &&
                slot.CurrentAttachedStatus == OutSlotDevice.AttachedStatus.UnAttached &&
                slot.InputIndex == OutSlotDevice.INPUT_INDEX_DEFAULT;
            throw new InvalidOperationException("Synthetic binding observer failure");
        };
        using var start = new Barrier(2);
        int notifications = 0;
        bool observerReadSucceeded = false;
        manager.SlotUnassigned += (sender, _, _) =>
        {
            Interlocked.Increment(ref notifications);
            try { observerReadSucceeded = sender.GetOutSlotDevice(output) == null; }
            catch (LockRecursionException) { observerReadSucceeded = false; }
        };
        try
        {
            Task[] removals = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            {
                Assert.IsTrue(start.SignalAndWait(TimeSpan.FromSeconds(5)));
                manager.DeferredRemoval(output, 0, bound);
            })).ToArray();
            Assert.IsTrue(Task.WaitAll(removals, TimeSpan.FromSeconds(10)));
            Assert.IsNull(manager.GetOutSlotDevice(output));
            Assert.IsNull(bound[0]);
            Assert.AreEqual(1, output.DisconnectAttempts);
            Assert.AreEqual(1, notifications);
            Assert.IsTrue(observerReadSucceeded);
            Assert.IsTrue(detachedStateWasCommitted);
        }
        finally { manager.Stop(); }
    }

    [TestMethod]
    public void SlotAssignmentObserverCanReadManagerStateWithoutReenteringWriterLock()
    {
        var manager = new OutputSlotManager();
        var output = new FakeOutput();
        bool observedCommittedSlot = false;
        manager.SlotAssigned += (sender, _, _) =>
        {
            try { observedCommittedSlot = ReferenceEquals(sender.GetOutSlotDevice(output)?.OutputDevice, output); }
            catch (LockRecursionException) { observedCommittedSlot = false; }
        };

        try
        {
            OutSlotDevice assigned = manager.DeferredPlugin(output, -1, "Unbound",
                new OutputDevice[1], OutContType.ViiperX360);
            Assert.IsNotNull(assigned);
            Assert.IsTrue(observedCommittedSlot);
        }
        finally { manager.Stop(); }
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PhysicalRetirementRetainsInputOwnershipWhenVirtualTeardownFails(bool failFeedback)
    {
        var service = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        var manager = new OutputSlotManager();
        service.outputDevices = new OutputDevice[ControlService.MAX_DS4_CONTROLLER_COUNT];
        typeof(ControlService).GetField("outputslotMan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, manager);
        var output = new FakeOutput { FailDisconnect = !failFeedback, FailFeedback = failFeedback };
        manager.DeferredPlugin(output, 0, "First", service.outputDevices, OutContType.ViiperX360);
        var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
        var physical = new DS4Device(hid, "Physical DS4 retirement regression");
        bool oldUseDInputOnly = Global.useDInputOnly[0];
        OutContType oldActiveType = Global.activeOutDevType[0];
        try
        {
            Global.useDInputOnly[0] = false;
            Global.activeOutDevType[0] = OutContType.ViiperX360;
            MethodInfo unplug = typeof(ControlService).GetMethod("UnplugOutDevCore",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var failure = Assert.ThrowsExactly<TargetInvocationException>(() =>
                unplug.Invoke(service, new object[] { 0, physical, false, false }));
            Assert.IsInstanceOfType<IOException>(failure.InnerException);
            Assert.AreSame(output, service.outputDevices[0],
                "The physical input must retain its output reference until teardown succeeds.");
            Assert.IsFalse(Global.useDInputOnly[0]);
            Assert.AreSame(output, manager.GetOutSlotDevice(output)?.OutputDevice);

            output.FailDisconnect = false;
            output.FailFeedback = false;
            unplug.Invoke(service, new object[] { 0, physical, false, false });
            Assert.IsNull(service.outputDevices[0]);
            Assert.IsTrue(Global.useDInputOnly[0]);
            Assert.IsNull(manager.GetOutSlotDevice(output));
        }
        finally
        {
            Global.useDInputOnly[0] = oldUseDInputOnly;
            Global.activeOutDevType[0] = oldActiveType;
            output.FailDisconnect = false;
            output.FailFeedback = false;
            manager.Stop();
            physical.ReadWaitEv.Dispose();
        }
    }

    // Synthetic methods never create a virtual device or touch a driver.
    private sealed class FakeOutput : OutputDevice
    {
        internal bool FailFeedback, FailDisconnect;
        internal int DisconnectAttempts;
        public override void Connect() => connected = true;
        public override void Disconnect()
        {
            DisconnectAttempts++;
            if (FailDisconnect) throw new IOException("Synthetic disconnect failure");
            connected = false;
        }
        public override void RemoveFeedbacks()
        {
            if (FailFeedback) throw new IOException("Synthetic feedback failure");
        }
        public override string GetDeviceType() => OutContType.ViiperX360.ToString();
        public override void ResetState(bool submit = true) { }
        public override void RemoveFeedback(int inIdx) { }
        public override void ConvertandSendReport(DS4State state, int device) { }
    }
}
