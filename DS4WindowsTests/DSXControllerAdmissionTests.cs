using DS4Windows.DS4Control;

namespace DS4WindowsTests;

[TestClass]
public class DSXControllerAdmissionTests
{
    [TestMethod]
    public void ReplacedOrMovedSlotCannotRetargetAnAdmittedPacket()
    {
        object owner = new();
        var original = new Device(1);
        var replacement = new Device(1);
        Device[] slots = { original, null };
        var admission = new DSXControllerAdmission<Device>(owner, slots.Length);
        Assert.IsTrue(admission.Begin(owner, slots, (_, _) => true));
        Assert.IsTrue(admission.TryGet(owner, 0, slots, out var captured));
        Assert.AreSame(original, captured);

        slots[0] = replacement;
        slots[1] = original;

        Assert.IsFalse(admission.TryGet(owner, 0, slots, out captured));
        Assert.IsNull(captured);
        Assert.IsFalse(admission.TryGet(owner, 1, slots, out _));
        Assert.IsTrue(admission.End(owner));
        Assert.IsTrue(admission.Begin(owner, slots, (_, _) => true));
        Assert.IsTrue(admission.TryGet(owner, 0, slots, out captured));
        Assert.AreSame(replacement, captured);
    }

    [TestMethod]
    public void RevokedOwnerCannotBeginOrContinuePacketsOrAffectItsSuccessor()
    {
        var predecessor = new Owner(1);
        var successor = new Owner(1);
        Device[] slots = { new(1) };
        var retired = new DSXControllerAdmission<Device>(predecessor, slots.Length);
        var current = new DSXControllerAdmission<Device>(successor, slots.Length);
        Assert.IsTrue(retired.Begin(predecessor, slots, (_, _) => true));
        Assert.IsTrue(retired.Revoke(predecessor));
        Assert.IsFalse(retired.TryGet(predecessor, 0, slots, out _));
        Assert.IsFalse(retired.Begin(predecessor, slots, (_, _) => true));
        Assert.IsTrue(current.Begin(successor, slots, (_, _) => true));

        Assert.IsFalse(current.Begin(predecessor, slots, (_, _) => true));
        Assert.IsFalse(current.End(predecessor));
        Assert.IsFalse(current.Revoke(predecessor));
        Assert.IsFalse(current.TryGet(predecessor, 0, slots, out _));
        Assert.IsTrue(current.TryGet(successor, 0, slots, out var device));
        Assert.AreSame(slots[0], device);
    }

    [TestMethod]
    public void IneligibleOrEmptySlotsAreNotAddedMidPacket()
    {
        object owner = new();
        Device[] slots = { new(1), new(2), null };
        var admission = new DSXControllerAdmission<Device>(owner, slots.Length);
        Assert.IsTrue(admission.Begin(owner, slots, (index, _) => index != 1));
        slots[2] = new Device(3);

        Assert.IsTrue(admission.TryGet(owner, 0, slots, out _));
        Assert.IsFalse(admission.TryGet(owner, 1, slots, out _));
        Assert.IsFalse(admission.TryGet(owner, 2, slots, out _));
        Assert.IsFalse(admission.TryGet(owner, -1, slots, out _));
        Assert.IsFalse(admission.TryGet(owner, 3, slots, out _));
        Assert.IsFalse(admission.TryGet(owner, 1, Array.Empty<Device>(), out _));
        Assert.IsTrue(admission.End(owner));
        Assert.IsFalse(admission.TryGet(owner, 0, slots, out _));
    }

    [TestMethod]
    public void FailedCaptureLeavesNoActivePartialPacket()
    {
        object owner = new();
        Device[] slots = { new(1), new(2) };
        var admission = new DSXControllerAdmission<Device>(owner, slots.Length);
        Assert.IsTrue(admission.Begin(owner, slots, (_, _) => true));

        Assert.ThrowsExactly<InvalidOperationException>(() => admission.Begin(owner, slots,
            (index, _) => index == 0 ? true : throw new InvalidOperationException("test capture failure")));

        Assert.IsFalse(admission.TryGet(owner, 0, slots, out _));
        Assert.IsFalse(admission.TryGet(owner, 1, slots, out _));
    }

    [TestMethod]
    public void RevocationDuringCaptureCannotBeUndoneByItsCompletion()
    {
        object owner = new();
        Device[] slots = { new(1) };
        var admission = new DSXControllerAdmission<Device>(owner, slots.Length);

        Assert.IsFalse(admission.Begin(owner, slots, (_, _) =>
        {
            Assert.IsTrue(admission.Revoke(owner));
            return true;
        }));

        Assert.IsFalse(admission.TryGet(owner, 0, slots, out _));
        Assert.IsFalse(admission.Begin(owner, slots, (_, _) => true));
    }

    private sealed record Device(int Id);
    private sealed record Owner(int Id);
}
