using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public class DualSenseDsxOverlayTests
{
    [TestMethod]
    public void NativeShadowPublicationWithDsxDisabledAllocatesZeroAfterWarmup()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        byte[] first = NativeReport(1, 0x0C, 0x15);
        WriteRawTrigger(first, 22, 0x21, 17);
        WriteRawTrigger(first, 11, 0x25, 29);
        SetRgb(first, 1, 17, 83, 201);
        byte[] second = (byte[])first.Clone();
        second[47] = 202;
        for (int index = 0; index < 1000; index++)
        {
            mailbox.ObserveDsxNativeState(first, 1);
            mailbox.ObserveDsxNativeState(second, 1);
            mailbox.ObserveDsxNativeState(second, 1);
        }
        long allocated;
        using (StrictAllocationMeasurementScope.Begin())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 2000; index++)
            {
                mailbox.ObserveDsxNativeState(first, 1);
                mailbox.ObserveDsxNativeState(second, 1);
                mailbox.ObserveDsxNativeState(second, 1);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.AreEqual(0L, allocated,
            $"Native underlay observation/snapshot equality allocated {allocated} bytes with DSX disabled.");
        Assert.IsNull(mailbox.ReadLatest().DsxOverlay.Owner);
        Assert.AreEqual((byte)202, mailbox.ReadLatest().DsxNativeState.Color.Value.blue);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    public void TriggerEffectEqualityIncludesEveryNativeByte(int changedByte)
    {
        byte[] raw = Enumerable.Range(1, 11).Select(value => (byte)value).ToArray();
        var original = DualSenseDsxOverlay.ReadTrigger(raw, 0);
        var equalCopy = DualSenseDsxOverlay.ReadTrigger(raw, 0);
        raw[changedByte]++;
        var changed = DualSenseDsxOverlay.ReadTrigger(raw, 0);

        Assert.IsTrue(original.Equals(equalCopy));
        Assert.AreEqual(original.GetHashCode(), equalCopy.GetHashCode());
        Assert.IsFalse(original.Equals(changed), $"Native byte {changedByte} was ignored by effect equality.");
        Assert.IsFalse(EqualityComparer<DualSenseDevice.TriggerEffectData>.Default.Equals(original, changed));
    }

    [TestMethod]
    public void PartialUpdatesPreserveOtherOverlayFieldsAndCurrentUnderlays()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        object owner = new();
        var profileLeft = Effect(0x21, 11);
        var modLeft = Effect(0x25, 22);
        mailbox.SetTrigger(TriggerId.LeftTrigger, profileLeft);
        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(owner,
            state => state with { Left = modLeft, Color = new DS4Color(17, 83, 201) }, out bool changed));
        Assert.IsTrue(changed);
        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(owner,
            state => state with { MicLed = 2, PlayerLeds = 0x15 }, out changed));
        Assert.IsTrue(changed);

        var latestProfileLeft = Effect(0x21, 33);
        mailbox.SetTrigger(TriggerId.LeftTrigger, latestProfileLeft);
        var snapshot = mailbox.ReadLatest();
        Assert.AreEqual(latestProfileLeft, snapshot.LeftTrigger);
        Assert.AreEqual(modLeft, snapshot.ForLocalTriggerReport().LeftTrigger);
        Assert.AreEqual(modLeft, snapshot.DsxOverlay.Left.Value);
        AssertColor(snapshot.DsxOverlay.Color.Value, 17, 83, 201);
        Assert.AreEqual((byte)2, snapshot.DsxOverlay.MicLed.Value);
        Assert.AreEqual((byte)0x15, snapshot.DsxOverlay.PlayerLeds.Value);
        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(owner, state => state, out changed));
        Assert.IsFalse(changed, "An identical command must not manufacture a state transition.");

        Assert.IsTrue(mailbox.ReleaseDsxOverlay(owner, out changed));
        Assert.IsTrue(changed);
        Assert.AreEqual(latestProfileLeft, mailbox.ReadLatest().ForLocalTriggerReport().LeftTrigger);
        Assert.AreEqual(0, mailbox.ReadLatest().DsxOverlay.Fields);
    }

    [TestMethod]
    public void SuccessorOwnerRejectsLateUpdatesAndReleaseFromPredecessor()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        var predecessor = new EqualOwner(1);
        var successor = new EqualOwner(1);
        Assert.AreEqual(predecessor, successor, "Reference identity must win over value equality.");
        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(predecessor,
            state => state with { MicLed = 1 }, out _));
        bool invoked = false;
        Assert.IsFalse(mailbox.TryUpdateDsxOverlay(successor, state =>
        {
            invoked = true;
            return state with { MicLed = 2 };
        }, out bool changed));
        Assert.IsFalse(invoked);
        Assert.IsFalse(changed);
        Assert.IsFalse(mailbox.ReleaseDsxOverlay(successor, out changed));
        Assert.IsFalse(changed);

        Assert.IsTrue(mailbox.ReleaseDsxOverlay(predecessor, out _));
        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(successor,
            state => state with { MicLed = 2, Owner = predecessor }, out _));
        var expected = mailbox.ReadLatest();
        Assert.AreSame(successor, expected.DsxOverlay.Owner,
            "An update cannot substitute a different owner token.");
        Assert.IsFalse(mailbox.ReleaseDsxOverlay(predecessor, out changed));
        Assert.IsFalse(changed);
        Assert.IsFalse(mailbox.TryUpdateDsxOverlay(predecessor,
            state => state with { MicLed = 0 }, out changed));
        Assert.IsFalse(changed);
        Assert.AreEqual(expected, mailbox.ReadLatest());
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void TriggerLabWinsOnlyOnItsActiveSideAndReleaseRevealsImpulse(bool leftLab, bool rightLab)
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        var profileLeft = Effect(0x21, 10);
        var profileRight = Effect(0x21, 20);
        var modLeft = Effect(0x25, 30);
        var modRight = Effect(0x25, 40);
        var labLeft = Effect(0x26, 50);
        var labRight = Effect(0x26, 60);
        object dsxOwner = new(), impulseOwner = new();
        mailbox.SetTrigger(TriggerId.LeftTrigger, profileLeft);
        mailbox.SetTrigger(TriggerId.RightTrigger, profileRight);
        Assert.IsTrue(mailbox.TrySetXboxImpulse(impulseOwner, 73, 191, out _));
        var impulse = mailbox.ReadLatest().ForLocalTriggerReport();
        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(dsxOwner,
            state => state with { Left = modLeft, Right = modRight }, out _));
        if (leftLab) mailbox.SetTriggerLabTrigger(TriggerId.LeftTrigger, labLeft, true);
        if (rightLab) mailbox.SetTriggerLabTrigger(TriggerId.RightTrigger, labRight, true);

        var actual = mailbox.ReadLatest().ForLocalTriggerReport();
        Assert.AreEqual(leftLab ? labLeft : modLeft, actual.LeftTrigger);
        Assert.AreEqual(rightLab ? labRight : modRight, actual.RightTrigger);
        Assert.IsTrue(mailbox.ReleaseDsxOverlay(dsxOwner, out _));
        actual = mailbox.ReadLatest().ForLocalTriggerReport();
        Assert.AreEqual(leftLab ? labLeft : impulse.LeftTrigger, actual.LeftTrigger);
        Assert.AreEqual(rightLab ? labRight : impulse.RightTrigger, actual.RightTrigger);
        Assert.AreEqual((byte)73, actual.LeftXboxImpulse);
        Assert.AreEqual((byte)191, actual.RightXboxImpulse);
    }

    [TestMethod]
    public void ExplicitZeroTriggerEffectOverridesInsteadOfReleasingTheField()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        object owner = new();
        mailbox.SetTrigger(TriggerId.LeftTrigger, Effect(0x21, 77));
        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(owner,
            state => state with { Left = default(DualSenseDevice.TriggerEffectData) }, out _));

        Assert.AreEqual(DualSenseDsxOverlay.LeftField, mailbox.ReadLatest().DsxOverlay.Fields);
        Assert.AreEqual(default(DualSenseDevice.TriggerEffectData),
            mailbox.ReadLatest().ForLocalTriggerReport().LeftTrigger);
    }

    [TestMethod]
    [DataRow(1)] // USB 0x02 report
    [DataRow(2)] // Bluetooth 0x31 report
    public void NativeShadowAccumulatesOnlyValidFieldsAndOwnsItsBytes(int offset)
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        byte[] initial = NativeReport(offset, 0x0C, 0x15);
        WriteRawTrigger(initial, offset + 21, 0x21, 17);
        WriteRawTrigger(initial, offset + 10, 0x25, 29);
        initial[offset + 8] = 2;
        initial[offset + 43] = 0x11;
        SetRgb(initial, offset, 10, 20, 30);
        mailbox.ObserveDsxNativeState(initial, offset);
        var first = mailbox.ReadLatest().DsxNativeState;
        Array.Fill(initial, (byte)0);

        byte[] ledOnly = NativeReport(offset, 0, 0x04);
        SetRgb(ledOnly, offset, 41, 72, 103);
        mailbox.ObserveDsxNativeState(ledOnly, offset);
        var updated = mailbox.ReadLatest().DsxNativeState;
        Assert.AreEqual(first.Left, updated.Left, "A packet without the trigger bit must not erase its held effect.");
        Assert.AreEqual(first.Right, updated.Right);
        Assert.AreEqual(first.MicLed, updated.MicLed);
        Assert.AreEqual(first.PlayerLeds, updated.PlayerLeds);
        AssertColor(updated.Color.Value, 41, 72, 103);

        byte[] rightOnly = NativeReport(offset, 0x04, 0);
        WriteRawTrigger(rightOnly, offset + 10, 0x26, 53);
        mailbox.ObserveDsxNativeState(rightOnly, offset);
        updated = mailbox.ReadLatest().DsxNativeState;
        Assert.AreEqual(first.Left, updated.Left);
        Assert.AreEqual(Effect(0x26, 53), updated.Right.Value);
        AssertColor(updated.Color.Value, 41, 72, 103);

        byte[] releaseLeds = NativeReport(offset, 0, 0x1C);
        mailbox.ObserveDsxNativeState(releaseLeds, offset);
        var released = mailbox.ReadLatest().DsxNativeState;
        Assert.IsNull(released.Color, "Explicit native LED release wins over simultaneous color bits.");
        Assert.IsNull(released.PlayerLeds);
        Assert.AreEqual(updated.Left, released.Left);
        Assert.AreEqual(updated.Right, released.Right);
        Assert.AreEqual(updated.MicLed, released.MicLed);
    }

    [TestMethod]
    public void NewDeviceMailboxDoesNotInheritThePreviousDevicesNativeOrModState()
    {
        var oldDevice = new DualSensePhysicalOutputStateMailbox();
        byte[] native = NativeReport(1, 0x08, 0);
        WriteRawTrigger(native, 22, 0x21, 17);
        oldDevice.ObserveDsxNativeState(native, 1);
        object oldOwner = new();
        oldDevice.TryUpdateDsxOverlay(oldOwner, state => state with { MicLed = 2 }, out _);
        var replacement = new DualSensePhysicalOutputStateMailbox();
        object newOwner = new();
        replacement.TryUpdateDsxOverlay(newOwner, state => state with { MicLed = 1 }, out _);
        var expected = replacement.ReadLatest();

        Assert.IsFalse(replacement.ReleaseDsxOverlay(oldOwner, out _));
        Assert.IsFalse(replacement.TryUpdateDsxOverlay(oldOwner,
            state => state with { Left = Effect(0x25, 33) }, out _));
        oldDevice.ReleaseDsxOverlay(oldOwner, out _);
        oldDevice.ClearDsxNativeState();
        Assert.AreEqual(expected, replacement.ReadLatest());
        Assert.AreEqual(0, expected.DsxNativeState.Fields);
    }

    [TestMethod]
    public void NativeSessionReleaseClearsUnderlayWithoutDiscardingTheOwnedOverlay()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        byte[] native = NativeReport(1, 0x0C, 0x15);
        mailbox.ObserveDsxNativeState(native, 1);
        object owner = new();
        mailbox.TryUpdateDsxOverlay(owner, state => state with { MicLed = 2 }, out _);
        var overlay = mailbox.ReadLatest().DsxOverlay;

        mailbox.ClearDsxNativeState();

        Assert.AreEqual(0, mailbox.ReadLatest().DsxNativeState.Fields);
        Assert.AreEqual(overlay, mailbox.ReadLatest().DsxOverlay);
    }

    [TestMethod]
    public void ReleaseMaskIncludesOnlyFieldsNoLongerOverridden()
    {
        var original = new DualSenseDsxOverlay(new object(), Effect(0x21, 11),
            Effect(0x25, 22), new DS4Color(1, 2, 3), 1, 0x15);
        var partial = original with { Left = null, Color = null, MicLed = 2 };

        Assert.AreEqual(DualSenseDsxOverlay.LeftField | DualSenseDsxOverlay.ColorField,
            DualSenseDsxOverlay.Released(original, partial));
        Assert.AreEqual(original.Fields, DualSenseDsxOverlay.Released(original, default));
        Assert.AreEqual(0, DualSenseDsxOverlay.Released(default, original));
    }

    [TestMethod]
    [DataRow(1)] // Canonical USB 0x02
    [DataRow(2)] // Canonical Bluetooth 0x31
    [DataRow(13)] // Production Bluetooth V5 0x36 combined carrier
    public void ExplicitLocalGenerationChangesOnlyItsSelectedCanonicalFields(int offset)
    {
        byte[] report = NativeReport(offset, 0xF3, 0xCB);
        for (int index = offset + 2; index < report.Length; index++) report[index] = (byte)(index + 73);
        byte[] expected = (byte[])report.Clone();
        var snapshot = DualSensePhysicalOutputSnapshot.Default with
        {
            DsxOverlay = new DualSenseDsxOverlay(new object(), Left: Effect(0x25, 41),
                Color: new DS4Color(17, 83, 201), MicLed: 2, PlayerLeds: 0x15),
        };
        expected[offset] |= 0x08;
        expected[offset + 1] = (byte)((expected[offset + 1] | 0x15) & ~0x08);
        Array.Clear(expected, offset + 21, 11);
        WriteRawTrigger(expected, offset + 21, 0x25, 41);
        expected[offset + 8] = 2;
        expected[offset + 43] = 0x15;
        SetRgb(expected, offset, 17, 83, 201);

        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, offset, snapshot,
            additionalTriggerValidity: 0x08);

        CollectionAssert.AreEqual(expected, report,
            "Overlay composition must leave the opposite trigger, rumble, audio, header, and carrier bytes intact.");
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(13, false)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    [DataRow(13, true)]
    public void NativeLedOnlyOrQuiescentMediaNeverRearmsTriggerCommands(int offset, bool pendingRelease)
    {
        var snapshot = DualSensePhysicalOutputSnapshot.Default with
        {
            DsxOverlay = pendingRelease ? default : new DualSenseDsxOverlay(new object(),
                Effect(0x25, 33), Effect(0x25, 44)),
            DsxNativeState = new DualSenseDsxOverlay(Left: Effect(0x21, 11), Right: Effect(0x21, 22)),
            DsxReleaseFields = pendingRelease ? DualSenseDsxOverlay.LeftField | DualSenseDsxOverlay.RightField : 0,
        };
        byte[] report = NativeReport(offset, 0xF3, offset == 13 ? (byte)0x83 : (byte)0x04);
        WriteRawTrigger(report, offset + 21, 0x26, 51);
        WriteRawTrigger(report, offset + 10, 0x26, 61);
        byte[] expected = (byte[])report.Clone();

        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, offset, snapshot);

        CollectionAssert.AreEqual(expected, report,
            "A held overlay or unacknowledged reset must not turn an unrelated native/media packet into a trigger command.");
    }

    [TestMethod]
    [DataRow(1, true)]
    [DataRow(1, false)]
    [DataRow(2, true)]
    [DataRow(2, false)]
    [DataRow(13, true)]
    [DataRow(13, false)]
    public void NativeTriggerCommandIsOverriddenOnItsValidSideOnly(int offset, bool left)
    {
        var snapshot = DualSensePhysicalOutputSnapshot.Default with
        {
            DsxOverlay = new DualSenseDsxOverlay(new object(), Effect(0x25, 33), Effect(0x25, 44)),
        };
        byte[] report = NativeReport(offset, left ? (byte)0x08 : (byte)0x04, 0);
        WriteRawTrigger(report, offset + 21, 0x21, 11);
        WriteRawTrigger(report, offset + 10, 0x21, 22);
        byte[] expected = (byte[])report.Clone();
        int triggerOffset = offset + (left ? 21 : 10);
        Array.Clear(expected, triggerOffset, 11);
        WriteRawTrigger(expected, triggerOffset, 0x25, left ? (byte)33 : (byte)44);

        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, offset, snapshot);

        CollectionAssert.AreEqual(expected, report,
            "Replacing a native effect must not restart the opposite trigger's held effect.");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(13)]
    public void ResetRestoresLatestExactNativeStateWithoutAnotherNativePacket(int offset)
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        object owner = new();
        byte[] original = NativeReport(offset, 0x0C, 0x15);
        WriteRawTrigger(original, offset + 21, 0x21, 11);
        WriteRawTrigger(original, offset + 10, 0x25, 22);
        // Opaque native bytes must survive; restoring is not re-encoding a profile effect.
        original[offset + 21 + 7] = 91;
        original[offset + 21 + 8] = 92;
        original[offset + 21 + 10] = 93;
        original[offset + 8] = 1;
        original[offset + 43] = 0x11;
        SetRgb(original, offset, 10, 20, 30);
        mailbox.ObserveDsxNativeState(original, offset);
        mailbox.SetNativeGameLightbarOwnershipReleased(false);
        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(owner, _ => new DualSenseDsxOverlay(
            Left: Effect(0x26, 33), Right: Effect(0x26, 44),
            Color: new DS4Color(200, 100, 50), MicLed: 0, PlayerLeds: 0x1F), out _));

        byte[] newer = NativeReport(offset, 0x04, 0x15);
        WriteRawTrigger(newer, offset + 10, 0x25, 55);
        newer[offset + 10 + 7] = 81;
        newer[offset + 10 + 8] = 82;
        newer[offset + 10 + 10] = 83;
        newer[offset + 8] = 2;
        newer[offset + 43] = 0x04;
        SetRgb(newer, offset, 41, 72, 103);
        mailbox.ObserveDsxNativeState(newer, offset);
        var previous = mailbox.ReadLatest().DsxOverlay;
        Assert.IsTrue(mailbox.ReleaseDsxOverlay(owner, out _));
        var snapshot = mailbox.ReadLatest();
        int released = DualSenseDsxOverlay.Released(previous, snapshot.DsxOverlay);
        byte[] quiescent = NativeReport(offset, 0xF3, 0x83);
        byte[] expected = (byte[])quiescent.Clone();
        expected[offset] |= 0x0C;
        expected[offset + 1] |= 0x15;
        Array.Copy(original, offset + 21, expected, offset + 21, 11);
        Array.Copy(newer, offset + 10, expected, offset + 10, 11);
        expected[offset + 8] = 2;
        expected[offset + 43] = 0x04;
        SetRgb(expected, offset, 41, 72, 103);

        DualSenseDevice.ApplyDsxOverlayToNativeReport(quiescent, offset, snapshot, released,
            DualSenseDevice.GetDsxTriggerValidity(released));

        CollectionAssert.AreEqual(expected, quiescent,
            "Reset itself must restore each latest native field, including a trigger omitted by newer native packets.");
        Assert.AreEqual(0, snapshot.DsxOverlay.Fields);
    }

    [TestMethod]
    [DataRow(1, true)]
    [DataRow(1, false)]
    [DataRow(2, true)]
    [DataRow(2, false)]
    [DataRow(13, true)]
    [DataRow(13, false)]
    public void CanonicalCompositionKeepsTriggerLabAboveDsxOnOnlyTheSelectedSide(int offset, bool leftLab)
    {
        var snapshot = DualSensePhysicalOutputSnapshot.Default with
        {
            LeftTrigger = Effect(0x21, 11),
            RightTrigger = Effect(0x21, 22),
            LeftTriggerLabActive = leftLab,
            RightTriggerLabActive = !leftLab,
            DsxOverlay = new DualSenseDsxOverlay(new object(), Effect(0x25, 33), Effect(0x25, 44)),
        };
        byte[] report = NativeReport(offset, 0, 0);

        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, offset, snapshot,
            additionalTriggerValidity: 0x0C);

        AssertRawTrigger(report, offset + 21, leftLab ? Effect(0x21, 11) : Effect(0x25, 33));
        AssertRawTrigger(report, offset + 10, leftLab ? Effect(0x25, 44) : Effect(0x21, 22));
        Assert.AreEqual((byte)0x0C, report[offset]);
        Assert.AreEqual((byte)0, report[offset + 1]);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(13)]
    public void ResetWithoutNativeOwnerRestoresCurrentProfileFields(int offset)
    {
        var snapshot = DualSensePhysicalOutputSnapshot.Default with
        {
            LeftTrigger = Effect(0x21, 11),
            RightTrigger = Effect(0x25, 22),
            ProfileLightbar = new DS4LightbarState { LightBarColor = new DS4Color(31, 63, 127) },
            MuteLedByte = 2,
            ActivePlayerLedMask = 0x04,
        };
        byte[] report = NativeReport(offset, 0, 0x08);

        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, offset, snapshot, 0x1F, 0x0C);

        AssertRawTrigger(report, offset + 21, snapshot.LeftTrigger);
        AssertRawTrigger(report, offset + 10, snapshot.RightTrigger);
        Assert.AreEqual((byte)0x0C, report[offset]);
        Assert.AreEqual((byte)0x15, report[offset + 1]);
        Assert.AreEqual((byte)2, report[offset + 8]);
        Assert.AreEqual((byte)0x04, report[offset + 43]);
        Assert.AreEqual((byte)31, report[offset + 44]);
        Assert.AreEqual((byte)63, report[offset + 45]);
        Assert.AreEqual((byte)127, report[offset + 46]);
    }

    [TestMethod]
    public void ResetBeforePhysicalClaimStillPublishesDurableRestorationFields()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        object owner = new();
        long claimed = 0;
        mailbox.TryClaim(ref claimed, out _);
        mailbox.TryUpdateDsxOverlay(owner,
            state => state with { Left = Effect(0x21, 11), MicLed = 2 }, out _);
        // A native callback may already have composed this state, without the
        // normal physical owner claiming the intermediate mailbox revision.
        mailbox.ReleaseDsxOverlay(owner, out _);

        Assert.IsTrue(mailbox.TryClaim(ref claimed, out var snapshot));
        Assert.AreEqual(0, snapshot.DsxOverlay.Fields);
        Assert.AreEqual(DualSenseDsxOverlay.LeftField | DualSenseDsxOverlay.MicField,
            snapshot.DsxReleaseFields);
        Assert.IsTrue(snapshot.DsxReleaseRevision > 0);
        mailbox.AcknowledgeDsxRelease(snapshot.DsxReleaseRevision, snapshot.DsxReleaseFields);
        Assert.AreEqual(0, mailbox.ReadLatest().DsxReleaseFields);
    }

    [TestMethod]
    public void PartialFieldReleaseBeforePhysicalClaimKeepsRemainingOverridesAndRestorationPending()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        object owner = new();
        var left = Effect(0x21, 11);
        mailbox.TryUpdateDsxOverlay(owner,
            state => state with { Left = left, MicLed = 2 }, out _);

        Assert.IsTrue(mailbox.TryUpdateDsxOverlay(owner,
            state => state with { MicLed = null }, out bool changed));

        Assert.IsTrue(changed);
        var snapshot = mailbox.ReadLatest();
        Assert.AreEqual(left, snapshot.DsxOverlay.Left.Value);
        Assert.IsNull(snapshot.DsxOverlay.MicLed);
        Assert.AreSame(owner, snapshot.DsxOverlay.Owner);
        Assert.AreEqual(DualSenseDsxOverlay.MicField, snapshot.DsxReleaseFields);
        Assert.IsTrue(snapshot.DsxReleaseRevision > 0);
        mailbox.AcknowledgeDsxRelease(snapshot.DsxReleaseRevision, snapshot.DsxReleaseFields);
        Assert.AreEqual(0, mailbox.ReadLatest().DsxReleaseFields);
        Assert.AreEqual(left, mailbox.ReadLatest().DsxOverlay.Left.Value);
    }

    [TestMethod]
    public void OlderPhysicalAcknowledgementCannotEraseANewerReset()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        object owner = new();
        mailbox.TryUpdateDsxOverlay(owner, state => state with { MicLed = 1 }, out _);
        mailbox.ReleaseDsxOverlay(owner, out _);
        var firstReset = mailbox.ReadLatest();
        mailbox.TryUpdateDsxOverlay(owner, state => state with { Color = new DS4Color(1, 2, 3) }, out _);
        mailbox.ReleaseDsxOverlay(owner, out _);
        var secondReset = mailbox.ReadLatest();

        mailbox.AcknowledgeDsxRelease(firstReset.DsxReleaseRevision, 0x1F);

        Assert.AreEqual(secondReset, mailbox.ReadLatest());
        Assert.AreEqual(DualSenseDsxOverlay.MicField | DualSenseDsxOverlay.ColorField,
            secondReset.DsxReleaseFields);
        mailbox.AcknowledgeDsxRelease(secondReset.DsxReleaseRevision, DualSenseDsxOverlay.MicField);
        Assert.AreEqual(DualSenseDsxOverlay.ColorField, mailbox.ReadLatest().DsxReleaseFields);
        mailbox.AcknowledgeDsxRelease(secondReset.DsxReleaseRevision, DualSenseDsxOverlay.ColorField);
        Assert.AreEqual(0, mailbox.ReadLatest().DsxReleaseFields);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(13)]
    public void PendingPredecessorRestorationCannotPaintOverSuccessorOverlay(int offset)
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        object predecessor = new(), successor = new();
        mailbox.TryUpdateDsxOverlay(predecessor,
            state => state with { Left = Effect(0x21, 11), Color = new DS4Color(1, 2, 3) }, out _);
        mailbox.ReleaseDsxOverlay(predecessor, out _);
        mailbox.TryUpdateDsxOverlay(successor,
            state => state with { Left = Effect(0x25, 22), Color = new DS4Color(31, 63, 127) }, out _);
        byte[] report = NativeReport(offset, 0, 0);

        var snapshot = mailbox.ReadLatest();
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, offset, snapshot,
            snapshot.DsxReleaseFields, additionalTriggerValidity: 0x08);

        AssertRawTrigger(report, offset + 21, Effect(0x25, 22));
        Assert.AreEqual((byte)31, report[offset + 44]);
        Assert.AreEqual((byte)63, report[offset + 45]);
        Assert.AreEqual((byte)127, report[offset + 46]);
        Assert.AreSame(successor, mailbox.ReadLatest().DsxOverlay.Owner);
    }

    [TestMethod]
    [DataRow(true, false, false, false, 0)]
    [DataRow(true, true, true, false, 1)]
    [DataRow(false, false, true, false, 0)]
    [DataRow(false, false, true, true, 1)]
    public void MicResetRespectsCurrentProfileMuteOverridesAheadOfNativeLed(
        bool ledOverride, bool ledOn, bool microphoneOverride, bool microphoneMuted, int expected)
    {
        var snapshot = DualSensePhysicalOutputSnapshot.Default with
        {
            MuteLedOverride = ledOverride,
            MuteLedOn = ledOn,
            MicrophoneMuteOverride = microphoneOverride,
            MicrophoneMuted = microphoneMuted,
            DsxNativeState = new DualSenseDsxOverlay(MicLed: 2),
            DsxReleaseFields = DualSenseDsxOverlay.MicField,
        };
        byte[] report = NativeReport(1, 0, 0);

        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, 1, snapshot, snapshot.DsxReleaseFields);

        Assert.AreEqual((byte)expected, report[9]);
        Assert.AreEqual((byte)0x01, report[2]);
        Assert.AreEqual((byte)0, report[10], "Restoring a mic LED must not toggle the microphone's mute state.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OrderedReleaseRestoresNativeBOnlyOnReleasedFieldsAndKeepsUnrelatedClaimedBytes(bool passthrough)
    {
        const int released = DualSenseDsxOverlay.LeftField | DualSenseDsxOverlay.ColorField;
        byte[] report = NativeReport(13, 0x04, 0x01);
        WriteRawTrigger(report, 23, 0x26, 99); // Unrelated claimed right-trigger generation.
        WriteRawTrigger(report, 34, 0x21, 11); // Old native A.
        report[21] = 77; // Unrelated microphone state is not part of this release.
        byte[] expected = (byte[])report.Clone();
        expected[13] |= 0x08;
        expected[14] |= 0x04;
        Array.Clear(expected, 34, 11);
        WriteRawTrigger(expected, 34, 0x25, 55);
        SetRgb(expected, 13, 41, 72, 103);
        var latest = DualSensePhysicalOutputSnapshot.Default with
        {
            // The successor owns only the other side, which must not block
            // restoration of this released side or alter its unrelated bytes.
            DsxOverlay = new DualSenseDsxOverlay(new object(), Right: Effect(0x25, 44)),
            DsxNativeState = new DualSenseDsxOverlay(Left: Effect(0x25, 55), Color: new DS4Color(41, 72, 103)),
            NativeGameLightbarOwnershipReleased = false,
            MuteLedOverride = true,
            MuteLedOn = false,
            IndicatorsFollowApplication = passthrough,
            ProfilePlayerLedMask = 0,
            ProfileMicLedByte = 2,
        };

        DualSenseDevice.ApplyDsxReleaseAtOrderedBoundary(report, 13, latest, released);

        CollectionAssert.AreEqual(expected, report,
            "After the native barrier, refresh only the released fields; never replay stale A or unrelated newer local fields.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OrderedReleaseCannotRestartOrReplaceANewerDsxEffect(bool successorSession)
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        object previousOwner = new();
        mailbox.TryUpdateDsxOverlay(previousOwner,
            state => state with { Left = Effect(0x21, 11) }, out _);
        mailbox.ReleaseDsxOverlay(previousOwner, out _);
        var claimedReset = mailbox.ReadLatest();
        object currentOwner = successorSession ? new object() : previousOwner;
        mailbox.TryUpdateDsxOverlay(currentOwner,
            state => state with { Left = Effect(0x25, 55) }, out _);
        byte[] staleReset = NativeReport(13, 0x0C, 0);
        WriteRawTrigger(staleReset, 34, 0x21, 22);
        WriteRawTrigger(staleReset, 23, 0x26, 99);
        byte[] before = (byte[])staleReset.Clone();

        // B may already have reached the controller through a native callback.
        DualSenseDevice.ApplyDsxReleaseAtOrderedBoundary(staleReset, 13,
            mailbox.ReadLatest(), claimedReset.DsxReleaseFields);

        Assert.AreEqual((byte)0x04, staleReset[13],
            "An older reset must not issue another trigger command after a newer mod owns that side.");
        CollectionAssert.AreEqual(before.Skip(23).Take(11).ToArray(),
            staleReset.Skip(23).Take(11).ToArray());
        Assert.AreSame(currentOwner, mailbox.ReadLatest().DsxOverlay.Owner);
        Assert.AreEqual(Effect(0x25, 55), mailbox.ReadLatest().DsxOverlay.Left.Value);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void OrderedReleaseCannotOverwriteALateTriggerLabOwner(bool left)
    {
        var latest = DualSensePhysicalOutputSnapshot.Default with
        {
            DsxNativeState = new DualSenseDsxOverlay(Left: Effect(0x25, 55), Color: new DS4Color(41, 72, 103)),
            LeftTriggerLabActive = left,
            RightTriggerLabActive = !left,
            LeftTrigger = Effect(0x26, 66),
            RightTrigger = Effect(0x26, 77),
        };
        byte[] staleReset = NativeReport(13, 0x0C, 0xC3);
        WriteRawTrigger(staleReset, 34, 0x21, 11);
        WriteRawTrigger(staleReset, 23, 0x21, 22);
        byte[] before = (byte[])staleReset.Clone();

        DualSenseDevice.ApplyDsxReleaseAtOrderedBoundary(staleReset, 13, latest,
            left ? DualSenseDsxOverlay.LeftField : DualSenseDsxOverlay.RightField);

        Assert.AreEqual(left ? (byte)0x04 : (byte)0x08, staleReset[13]);
        int otherSide = left ? 23 : 34;
        CollectionAssert.AreEqual(before.Skip(otherSide).Take(11).ToArray(),
            staleReset.Skip(otherSide).Take(11).ToArray());
        Assert.AreEqual(before[14], staleReset[14], "Trigger release cannot mutate unrelated visual/audio flags.");
    }

    [TestMethod]
    [DataRow(0)] // New DSX visual owner.
    [DataRow(1)] // Latest native visual owner.
    [DataRow(2)] // Latest profile visual/mic override.
    public void OrderedVisualReleaseUsesLatestFieldPriorityWithoutTouchingTriggers(int priority)
    {
        var nativeColor = new DS4Color(17, 83, 201);
        var profileColor = new DS4Color(31, 63, 127);
        var modColor = new DS4Color(41, 72, 103);
        var latest = DualSensePhysicalOutputSnapshot.Default with
        {
            DsxNativeState = new DualSenseDsxOverlay(Color: nativeColor, MicLed: 2, PlayerLeds: 0x11),
            NativeGameLightbarOwnershipReleased = priority == 2,
            ProfileLightbar = new DS4LightbarState { LightBarColor = profileColor },
            ActivePlayerLedMask = 0x04,
            MuteLedOverride = priority == 2,
            MuteLedOn = false,
            DsxOverlay = priority == 0 ? new DualSenseDsxOverlay(new object(),
                Color: modColor, MicLed: 1, PlayerLeds: 0x1F) : default,
        };
        byte[] report = NativeReport(13, 0xF3, 0xC8);
        WriteRawTrigger(report, 23, 0x26, 99);
        WriteRawTrigger(report, 34, 0x25, 55);
        byte[] expected = (byte[])report.Clone();
        expected[14] = 0xD5;
        expected[21] = priority == 0 ? (byte)1 : priority == 1 ? (byte)2 : (byte)0;
        expected[56] = priority == 0 ? (byte)0x1F : priority == 1 ? (byte)0x11 : (byte)0x04;
        var color = priority == 0 ? modColor : priority == 1 ? nativeColor : profileColor;
        SetRgb(expected, 13, color.red, color.green, color.blue);

        DualSenseDevice.ApplyDsxReleaseAtOrderedBoundary(report, 13, latest,
            DualSenseDsxOverlay.ColorField | DualSenseDsxOverlay.MicField | DualSenseDsxOverlay.PlayerField);

        CollectionAssert.AreEqual(expected, report);
    }

    [TestMethod]
    [DataRow(0x0C, 1, 0x04)]
    [DataRow(0x0C, 2, 0x08)]
    [DataRow(0x0C, 3, 0x00)]
    [DataRow(0x08, 2, 0x08)]
    [DataRow(0x04, 1, 0x04)]
    [DataRow(0x0C, 28, 0x0C)]
    public void FollowingLocalTriggerCommandExcludesOnlyTheAlreadyRestoredSides(
        int originallyDirty, int released, int expectedDirty)
    {
        var state = DualSensePhysicalOutputSnapshot.Default;
        Assert.AreEqual((byte)expectedDirty,
            DualSenseDevice.GetCurrentLocalTriggerValidity((byte)originallyDirty, released, state, state),
            "A completed native restoration must not be overwritten or repeated by the following local-trigger lane.");
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void ResetAndCurrentTriggerLabOrDsxGenerationInTheSameClaimMustStillPublish(bool triggerLab, bool left)
    {
        var effect = Effect(0x26, 55);
        var claimed = DualSensePhysicalOutputSnapshot.Default with
        {
            LeftTrigger = effect,
            RightTrigger = effect,
            LeftTriggerLabActive = triggerLab && left,
            RightTriggerLabActive = triggerLab && !left,
            DsxOverlay = triggerLab ? default : new DualSenseDsxOverlay(new object(),
                left ? effect : null, left ? null : effect),
        };

        byte validity = DualSenseDevice.GetCurrentLocalTriggerValidity(0x0C,
            left ? DualSenseDsxOverlay.LeftField : DualSenseDsxOverlay.RightField,
            claimed, claimed);

        Assert.AreEqual((byte)0x0C, validity,
            "The boundary skips a currently owned side; its still-current local generation must not be acknowledged without publication.");
    }

    [TestMethod]
    [DataRow("tl_effect", 0)]
    [DataRow("tl_effect", 1)]
    [DataRow("tl_presence", 0)]
    [DataRow("tl_presence", 1)]
    [DataRow("dsx_effect", 0)]
    [DataRow("dsx_effect", 1)]
    [DataRow("dsx_owner", 0)]
    [DataRow("dsx_owner", 1)]
    [DataRow("dsx_reserved", 0)]
    [DataRow("dsx_reserved", 1)]
    public void LocalAdmissionRejectsObsoleteOwnerOrEffectWithoutDroppingAnUnrelatedCurrentSide(string change, int released)
    {
        var effect = Effect(0x26, 55);
        var claimed = DualSensePhysicalOutputSnapshot.Default with
        {
            LeftTrigger = effect,
            RightTrigger = effect,
            LeftTriggerLabActive = change == "tl_effect",
            RightTriggerLabActive = true,
            DsxOverlay = new DualSenseDsxOverlay(new object(), Left: effect),
        };
        var latest = claimed;
        switch (change)
        {
            case "tl_effect": latest = latest with { LeftTrigger = Effect(0x26, 66) }; break;
            case "tl_presence": latest = latest with { LeftTriggerLabActive = true }; break;
            case "dsx_effect": latest = latest with { DsxOverlay = latest.DsxOverlay with { Left = Effect(0x25, 66) } }; break;
            case "dsx_owner": latest = latest with { DsxOverlay = latest.DsxOverlay with { Owner = new object() } }; break;
            case "dsx_reserved":
                effect.triggerReserved7 = 81;
                latest = latest with { DsxOverlay = latest.DsxOverlay with { Left = effect } };
                break;
            default: Assert.Fail("Unknown test change."); break;
        }

        byte validity = DualSenseDevice.GetCurrentLocalTriggerValidity(0x0C, released, claimed, latest);

        Assert.AreEqual((byte)0x04, validity,
            "A newer same-session effect, successor owner, or Trigger Lab activation wins over stale A; the current other side still publishes.");
    }

    private static DualSenseDevice.TriggerEffectData Effect(byte mode, byte seed)
    {
        var effect = new DualSenseDevice.TriggerEffectData();
        effect.ChangeRaw(mode, seed, (byte)(seed + 1), (byte)(seed + 2),
            (byte)(seed + 3), (byte)(seed + 4), (byte)(seed + 5), (byte)(seed + 6));
        return effect;
    }

    private static byte[] NativeReport(int offset, byte flag0, byte flag1)
    {
        byte[] report = new byte[offset == 1 ? 64 : offset == 2 ? 78 : 398];
        report[0] = offset == 1 ? (byte)0x02 : offset == 2 ? (byte)0x31 : (byte)0x36;
        if (offset == 2) report[1] = 0x02;
        report[offset] = flag0;
        report[offset + 1] = flag1;
        return report;
    }

    private static void WriteRawTrigger(byte[] report, int start, byte mode, byte seed)
    {
        report[start] = mode;
        for (int index = 1; index <= 6; index++) report[start + index] = (byte)(seed + index - 1);
        report[start + 9] = (byte)(seed + 6);
    }

    private static void SetRgb(byte[] report, int offset, byte red, byte green, byte blue)
    {
        report[offset + 44] = red;
        report[offset + 45] = green;
        report[offset + 46] = blue;
    }

    private static void AssertColor(DS4Color color, byte red, byte green, byte blue)
    {
        Assert.AreEqual(red, color.red);
        Assert.AreEqual(green, color.green);
        Assert.AreEqual(blue, color.blue);
    }

    private static void AssertRawTrigger(byte[] report, int offset, DualSenseDevice.TriggerEffectData expected)
    {
        Assert.AreEqual(expected.triggerMotorMode, report[offset]);
        Assert.AreEqual(expected.triggerStartResistance, report[offset + 1]);
        Assert.AreEqual(expected.triggerEffectForce, report[offset + 2]);
        Assert.AreEqual(expected.triggerRangeForce, report[offset + 3]);
        Assert.AreEqual(expected.triggerNearReleaseStrength, report[offset + 4]);
        Assert.AreEqual(expected.triggerNearMiddleStrength, report[offset + 5]);
        Assert.AreEqual(expected.triggerPressedStrength, report[offset + 6]);
        Assert.AreEqual(expected.triggerActuationFrequency, report[offset + 9]);
    }

    private sealed record EqualOwner(int Value);
}
