using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.Switch2;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize] // Reader fixtures replace the shared controller/profile lookup only.
public sealed class ViiperNintendoPendingControlRepeatTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    [DataRow(76)]
    [DataRow(217)]
    [DataRow(474)]
    public void TenThousandPendingRepeatsPreserveDistinctABAAndFinalZero(int length)
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] a = Rumble(length, 90), b = Rumble(length, 40), zero = Rumble(length, 0);
        Assert.IsTrue(Enqueue(buffer, a, context));
        long originalTimestamp = Timestamps(buffer)[0];
        for (int i = 1; i < 10_000; i++) Assert.IsTrue(Enqueue(buffer, a, context));
        Assert.AreEqual(1, buffer.PendingOrderedControlCount);
        Assert.AreEqual(9_999L, buffer.OrderedControlNintendoRepeated);
        Assert.AreEqual(0L, buffer.OrderedControlRepeated);
        Assert.AreEqual(originalTimestamp, Timestamps(buffer)[0], "Repeat folding must not rewrite the original queue-age timestamp.");
        Assert.IsTrue(Enqueue(buffer, b, context));
        Assert.IsTrue(Enqueue(buffer, a, context));
        Assert.IsTrue(Enqueue(buffer, zero, context));
        Assert.AreEqual(0L, buffer.OrderedControlDropped);
        Assert.AreEqual(4, buffer.PendingOrderedControlCount);
        foreach (byte[] expected in new[] { a, b, a, zero }) AssertOrdinaryCommand(buffer, expected);
        Assert.AreEqual(0, buffer.PendingOrderedControlCount);
    }

    [TestMethod]
    public void DequeuedCommandIsNotARepeatTargetForLaterKeepalive()
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] a = Rumble();
        for (int i = 0; i < 32; i++)
        {
            Assert.IsTrue(Enqueue(buffer, a, context));
            AssertOrdinaryCommand(buffer, a);
        }
        Assert.AreEqual(32L, buffer.OrderedControlEnqueued);
        Assert.AreEqual(0L, buffer.OrderedControlNintendoRepeated);
    }

    [TestMethod]
    [DataRow("target")]
    [DataRow("session")]
    [DataRow("device-generation")]
    [DataRow("transport-generation")]
    [DataRow("binding")]
    [DataRow("profile")]
    [DataRow("stream")]
    [DataRow("slot")]
    [DataRow("unbound")]
    public void DifferentLifetimeOrProfileCannotFoldPendingCommands(string change)
    {
        var buffer = Buffer();
        var first = Context(buffer);
        var next = change switch
        {
            "target" => first with { Target = new object() },
            "session" => first with { Session = new object() },
            "device-generation" => first with { DeviceGeneration = 71 },
            "transport-generation" => first with { TransportGeneration = 111 },
            "binding" => first with { BindingRevision = 12 },
            "profile" => first with { ProfileRevision = 14 },
            "unbound" => default,
            _ => first,
        };
        byte[] a = Rumble();
        Assert.IsTrue(Enqueue(buffer, a, first));
        Assert.IsTrue(Enqueue(buffer, a, next, change == "stream" ? 18 : 17, change == "slot" ? 3 : 2));
        Assert.AreEqual(2, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlNintendoRepeated);
    }

    [TestMethod]
    [DataRow("speaker")]
    [DataRow("ordinary-control")]
    [DataRow("explicit-media-boundary")]
    [DataRow("native-entry")]
    [DataRow("unscoped-entry")]
    public void AnotherLaneOrEntryKindBreaksTheRepeatRun(string boundary)
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] a = Rumble();
        Assert.IsTrue(Enqueue(buffer, a, context));
        int expected = 2;
        switch (boundary)
        {
            case "speaker": Assert.IsTrue(buffer.TryEnqueueSpeaker(new byte[] { 1, 2, 3, 4 }, 4, 17)); break;
            case "ordinary-control": Assert.IsTrue(buffer.QueueControl(new byte[] { 1, 2 }, 2, 17, 2)); break;
            case "explicit-media-boundary": buffer.InvalidateNativeRepeat(); break;
            case "native-entry":
                Assert.IsTrue(buffer.TryEnqueueOrderedControl(a, a.Length, 17, 2, nativeCommand: true,
                    nativeContext: new(new object(), 11, 13, buffer.PendingBoundaryRevision)));
                expected++;
                break;
            case "unscoped-entry": Assert.IsTrue(buffer.TryEnqueueOrderedControl(a, a.Length, 17, 2)); expected++; break;
        }
        Assert.IsTrue(Enqueue(buffer, a, context));
        Assert.AreEqual(expected, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlNintendoRepeated);
    }

    [TestMethod]
    [DataRow("pcm")]
    [DataRow("audio-routing")]
    [DataRow("unknown-trigger")]
    [DataRow("reserved-trigger")]
    [DataRow("unknown-length")]
    [DataRow("timed-xbox")]
    public void TimeBearingOrOpaquePacketsKeepTheirExistingDeliverySemantics(string kind)
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] payload = Rumble(474);
        switch (kind)
        {
            case "pcm": payload[76] = 0x36; payload[80] = 1; break;
            case "audio-routing": payload[30] = 0x01; break;
            case "unknown-trigger": payload[29] |= 0x04; payload[39] = 0x07; break;
            case "reserved-trigger": payload[29] |= 0x04; payload[39] = 0x21; payload[46] = 1; break;
            case "unknown-length": Array.Resize(ref payload, 100); break;
            case "timed-xbox": payload = new byte[ControllerFeedbackFrame.SerializedLength]; break;
        }
        Assert.IsTrue(Enqueue(buffer, payload, context));
        Assert.IsTrue(Enqueue(buffer, payload, context));
        Assert.AreEqual(2, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlNintendoRepeated);
        AssertOrdinaryCommand(buffer, payload);
        AssertOrdinaryCommand(buffer, payload);
    }

    [TestMethod]
    public void ExpiredPendingTailCannotAbsorbANewFreshRepeat()
    {
        var buffer = Buffer(maxAge: 20);
        var context = Context(buffer);
        byte[] a = Rumble();
        Assert.IsTrue(Enqueue(buffer, a, context));
        // Clock-independent test injection into the existing queue timestamp;
        // production still uses its unchanged QPC clock and20 ms age policy.
        long stale = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        Timestamps(buffer)[0] = stale;
        ReceiptTimestamps(buffer)[0] = stale;
        Assert.IsTrue(Enqueue(buffer, a, context));
        Assert.AreEqual(2, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlNintendoRepeated);
        Assert.AreEqual(stale, Timestamps(buffer)[0]);
        Assert.IsTrue(Timestamps(buffer)[1] > stale);
    }

    [TestMethod]
    public void FreshIdenticalReceiptSurvivesOriginalAdmissionExpiryWithoutHidingItsAge()
    {
        var buffer = Buffer(maxAge: 20);
        var context = Context(buffer);
        byte[] a = Rumble();
        Assert.IsTrue(Enqueue(buffer, a, context));
        Assert.IsTrue(Enqueue(buffer, a, context));
        Assert.AreEqual(1L, buffer.OrderedControlNintendoRepeated);
        long latestReceipt = ReceiptTimestamps(buffer)[0];
        long millisecond = Stopwatch.Frequency / 1_000;
        // Construct the near-expiry consumer instant without wall-clock sleeps:
        // the original admission is25 ms old, the latest identical receipt10 ms.
        Timestamps(buffer)[0] = latestReceipt - 15 * millisecond;
        long original = Timestamps(buffer)[0];
        typeof(ViiperFeedbackDispatchBuffer).GetMethod("DropExpiredOrderedControlFrames", PrivateInstance)!
            .Invoke(buffer, new object[] { latestReceipt + 10 * millisecond });
        Assert.AreEqual(1, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlExpired);
        Assert.AreEqual(original, Timestamps(buffer)[0]);
        // The folded entry still expires when the latest source receipt ages.
        typeof(ViiperFeedbackDispatchBuffer).GetMethod("DropExpiredOrderedControlFrames", PrivateInstance)!
            .Invoke(buffer, new object[] { latestReceipt + 21 * millisecond });
        Assert.AreEqual(0, buffer.PendingOrderedControlCount);
        Assert.AreEqual(1L, buffer.OrderedControlExpired);
        Assert.IsTrue(buffer.OrderedControlMaximumQueueAgeMilliseconds >= 35,
            "Queue-age telemetry retains the original admission, not the later folded receipt.");
    }

    [TestMethod]
    public void ExplicitClearAndUnboundOldContextCannotShareANewTail()
    {
        var buffer = Buffer();
        var old = Context(buffer);
        byte[] a = Rumble();
        Assert.IsTrue(Enqueue(buffer, a, old));
        buffer.ClearPending();
        var current = old with { PendingBoundaryRevision = buffer.PendingBoundaryRevision };
        Assert.IsTrue(Enqueue(buffer, a, old));
        Assert.IsTrue(Enqueue(buffer, a, current));
        Assert.AreEqual(2, buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, buffer.OrderedControlNintendoRepeated);
        buffer.Reset();
        Assert.AreEqual(0L, buffer.OrderedControlNintendoRepeated);
        Assert.AreEqual(0, buffer.PendingOrderedControlCount);
    }

    [TestMethod]
    public void DistinctOverflowStillKeepsOnlyTheExistingNewestWindow()
    {
        var buffer = Buffer();
        var context = Context(buffer);
        for (byte i = 1; i <= 8; i++) Assert.IsTrue(Enqueue(buffer, Rumble(76, i), context));
        Assert.AreEqual(4, buffer.PendingOrderedControlCount);
        Assert.AreEqual(4L, buffer.OrderedControlDropped);
        Assert.AreEqual(0L, buffer.OrderedControlNintendoRepeated);
        for (byte i = 5; i <= 8; i++) AssertOrdinaryCommand(buffer, Rumble(76, i));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PendingRepeatAllocationGateDetectsOnlyThePositiveControl(bool allocateClone)
    {
        var buffer = Buffer();
        var context = Context(buffer);
        byte[] a = Rumble(474);
        for (int i = 0; i < 512; i++) Assert.IsTrue(Enqueue(buffer, a, context));
        bool accepted = true;
        long allocated;
        using (StrictAllocationMeasurementScope.Begin())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++) accepted &= Enqueue(buffer, a, context);
            if (allocateClone) GC.KeepAlive(a.Clone());
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.IsTrue(accepted);
        if (allocateClone) Assert.IsTrue(allocated >= a.Length, "The exact zero gate must detect a real packet clone.");
        else Assert.AreEqual(0L, allocated);
        Assert.AreEqual(1, buffer.PendingOrderedControlCount);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ActualReaderAdmissionFoldsOnlyBoundSwitch2ControlAndKeepsFinalZero(int model)
    {
        using var fixture = new ReaderFixture(model);
        byte[] a = Rumble(474), zero = Rumble(474, 0);
        for (int i = 0; i < 10_000; i++) Assert.IsTrue(fixture.Output.QueueTranslatedDualSenseControl(a, a.Length, 17, 0));
        Assert.IsTrue(fixture.Output.QueueTranslatedDualSenseControl(zero, zero.Length, 17, 0));
        Assert.AreEqual(2, fixture.Buffer.PendingOrderedControlCount);
        Assert.AreEqual(9_999L, fixture.Buffer.OrderedControlNintendoRepeated);
        Assert.AreEqual(0L, fixture.Buffer.OrderedControlRepeated);
        Assert.AreEqual(0, fixture.NativeWrites, "This is reader admission only, never real hardware or a native output callback.");
        AssertOrdinaryCommand(fixture.Buffer, a);
        AssertOrdinaryCommand(fixture.Buffer, zero);
    }

    [TestMethod]
    [DataRow("delay")]
    [DataRow("retired-session")]
    [DataRow("missing-session")]
    [DataRow("disabled-output")]
    [DataRow("wrong-slot")]
    public void ActualReaderDoesNotFoldWhenNintendoAuthorityIsUnavailable(string reason)
    {
        using var fixture = new ReaderFixture(0);
        switch (reason)
        {
            case "delay": Global.Switch2RumbleDelayMilliseconds[0] = 5; break;
            case "retired-session": Assert.IsTrue(fixture.Session.TryRetire()); break;
            case "missing-session": Set(fixture.Output, "switch2FeedbackSession", null); break;
            case "disabled-output": Global.EnableOutputDataToDS4[0] = false; break;
            case "wrong-slot": Set(fixture.Output, "lastInputDeviceIndex", 1); break;
        }
        byte[] a = Rumble();
        Assert.IsTrue(fixture.Output.QueueTranslatedDualSenseControl(a, a.Length, 17, 0));
        Assert.IsTrue(fixture.Output.QueueTranslatedDualSenseControl(a, a.Length, 17, 0));
        Assert.AreEqual(2, fixture.Buffer.PendingOrderedControlCount);
        Assert.AreEqual(0L, fixture.Buffer.OrderedControlNintendoRepeated);
    }

    private static ViiperFeedbackDispatchBuffer Buffer(int maxAge = 0) =>
        new(2, 4096, 474, 4, orderedControlMaximumAgeMilliseconds: maxAge);
    private static ViiperPendingNintendoControlContext Context(ViiperFeedbackDispatchBuffer buffer) =>
        new(new object(), new object(), 7, 11, 11, 13, buffer.PendingBoundaryRevision);
    private static byte[] Rumble(int length = 76, byte strength = 90)
    {
        byte[] result = new byte[length];
        result[28] = 0x02; result[29] = 0x02;
        result[31] = result[32] = strength; result[67] = 0x04;
        return result;
    }
    private static bool Enqueue(ViiperFeedbackDispatchBuffer buffer, byte[] payload,
        ViiperPendingNintendoControlContext context, long generation = 17, int index = 2) =>
        buffer.TryEnqueueOrderedControl(payload, payload.Length, generation, index, nintendoContext: context);
    private static void AssertOrdinaryCommand(ViiperFeedbackDispatchBuffer buffer, byte[] expected)
    {
        byte[] actual = new byte[474];
        Assert.IsFalse(buffer.TryPeekNativeCommand(actual, out _, out _, out _, out _, out _),
            "Translated Nintendo feedback must not enter raw Sony delivery.");
        Assert.IsTrue(buffer.TryDequeueOrderedControl(actual, out int length, out _, out _));
        CollectionAssert.AreEqual(expected, actual[..length]);
    }
    private static long[] Timestamps(ViiperFeedbackDispatchBuffer buffer) =>
        (long[])Get(buffer, "orderedControlEnqueueTimestamps");
    private static long[] ReceiptTimestamps(ViiperFeedbackDispatchBuffer buffer) =>
        (long[])Get(buffer, "orderedControlNintendoLastReceiptTimestamps");
    private static object Get(object target, string name) => target.GetType().GetField(name, PrivateInstance)!.GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance)!.SetValue(target, value);

    private sealed class ReaderFixture : IDisposable
    {
        private readonly ControlService previousHub = DS4Windows.Program.rootHub;
        private readonly bool previousOutput = Global.EnableOutputDataToDS4[0];
        private readonly int previousDelay = Global.Switch2RumbleDelayMilliseconds[0];
        private readonly Switch2BluetoothFeedbackLifetime owner;
        private readonly AudioHapticsService audio = new();
        internal ViiperOutDevice Output { get; }
        internal ViiperFeedbackDispatchBuffer Buffer { get; } = ViiperNintendoPendingControlRepeatTests.Buffer();
        internal Switch2VirtualFeedbackSession Session { get; }
        internal int NativeWrites;

        internal ReaderFixture(int kind)
        {
            Switch2RuntimeInputDevice device;
            if (kind == 3)
            {
                Assert.IsTrue(Switch2RuntimeInputDevice.TryCreateJoinedJoyCon(30, 40, 7, 11, 8, 12, out device, out _));
                Assert.IsTrue(Switch2BluetoothFeedbackLifetime.TryCreateJoined(
                    new Lease(this, Switch2ControllerModel.JoyCon2Left, 7, 11),
                    new Lease(this, Switch2ControllerModel.JoyCon2Right, 8, 12),
                    30, 40, 7, 11, 8, 12, out owner));
                Assert.IsTrue(device.TryAttachJoinedBluetoothFeedbackLifetime(30, 40, owner));
            }
            else
            {
                var model = kind == 0 ? Switch2ControllerModel.ProController2 :
                    kind == 1 ? Switch2ControllerModel.JoyCon2Left : Switch2ControllerModel.JoyCon2Right;
                if (kind == 0) Assert.IsTrue(Switch2RuntimeInputDevice.TryCreatePro(7, 11, Switch2Transport.BluetoothLe, out device, out _));
                else Assert.IsTrue(Switch2RuntimeInputDevice.TryCreateStandaloneJoyCon(model, 7, 11, out device, out _));
                Assert.IsTrue(Switch2BluetoothFeedbackLifetime.TryCreate(new Lease(this, model, 7, 11), model, 7, 11, out owner));
                Assert.IsTrue(device.TryAttachBluetoothFeedbackLifetime(model, 7, 11, owner));
            }
            Assert.IsTrue(owner.TryActivate());
            Assert.IsTrue(owner.TryCreateVirtualFeedbackSession(ControllerFeedbackSource.DualSenseVirtualDevice, out var session));
            Session = session;
            var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
            hub.DS4Controllers = new DS4Device[4]; hub.DS4Controllers[0] = device;
            Set(hub, "audioHapticsService", audio);
            DS4Windows.Program.rootHub = hub;
            Global.EnableOutputDataToDS4[0] = true;
            Global.Switch2RumbleDelayMilliseconds[0] = 0;
            Output = new(OutContType.None, ViiperVirtualDeviceType.DualSense);
            // Disable age only for deterministic burst identity testing; the
            // production20 ms expiration is exercised separately above.
            Set(Output, "feedbackDispatchBuffer", Buffer);
            Set(Output, "switch2FeedbackSession", Session);
            Set(Output, "lastInputDeviceIndex", 0);
            Set(Output, "connected", true);
            Set(Output, "feedbackDispatchStopRequested", false);
        }

        public void Dispose()
        {
            try
            {
                Set(Output, "connected", false);
                Buffer.ClearPending();
                Assert.IsTrue(owner.TryStopAndRetireUntil(Environment.TickCount64 + 1_000, 3));
            }
            finally
            {
                DS4Windows.Program.rootHub = previousHub;
                Global.EnableOutputDataToDS4[0] = previousOutput;
                Global.Switch2RumbleDelayMilliseconds[0] = previousDelay;
                audio.Dispose();
            }
        }
    }

    private sealed class Lease(ReaderFixture fixture, Switch2ControllerModel expected, ulong generation,
        ulong transport) : ISwitch2BluetoothHdRumbleBindableTransportLease
    {
        public bool HasHdRumbleOutput => true;
        public bool Authenticates(Switch2ControllerModel model, ulong device, ulong connection) =>
            model == expected && device == generation && connection == transport;
        public bool TryBindHdRumbleLifetime(Switch2ControllerModel model, ulong device, ulong connection) =>
            Authenticates(model, device, connection);
        public Switch2BluetoothHdRumbleTransportWriteResult TryWritePayload(ReadOnlySpan<byte> payload,
            Switch2ControllerModel model, ulong device, ulong connection)
        {
            fixture.NativeWrites++;
            return Switch2BluetoothHdRumbleTransportWriteResult.Complete(model, device, connection, payload.Length);
        }
    }
}
