using DS4Windows;
using DS4WinWPF.DS4Control;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class OutputSlotRemovalTests
{
    [DataTestMethod]
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
            unassigned++;
            Assert.IsNull(detached.OutputDevice);
            Assert.AreEqual(OutSlotDevice.AttachedStatus.UnAttached, detached.CurrentAttachedStatus);
            Assert.IsNull(manager.GetOutSlotDevice(index).OutputDevice);
            if (ReferenceEquals(detached, slot))
            {
                Assert.IsNull(manager.GetOutSlotDevice(output));
                if (input >= 0) Assert.IsNull(bound[input]);
            }
        };
        manager.SlotUnassigned += onUnassigned;
        if (failFeedback) output.FailFeedback = true;
        else output.FailDisconnect = true;
        try
        {
            Assert.ThrowsException<IOException>(() => manager.DeferredRemoval(output, input, bound));
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
            Assert.ThrowsException<IOException>(() => manager.Stop());
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
