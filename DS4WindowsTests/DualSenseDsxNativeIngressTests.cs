using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public class DualSenseDsxNativeIngressTests
{
    [TestMethod]
    [DataRow(ConnectionType.USB, true, false, 0x30)]
    [DataRow(ConnectionType.USB, true, true, 0x00)]
    [DataRow(ConnectionType.USB, false, false, 0x00)]
    [DataRow(ConnectionType.BT, true, false, 0x09)]
    [DataRow(ConnectionType.BT, true, true, 0x00)]
    public void PhysicalAudioReportsUseTheCorrectTransportSpeakerPath(ConnectionType connection, bool enabled, bool headset, int expected)
    {
        var device = CreateDevice(connection);
        try
        {
            device.SetProfileAudioAndMuteButtonState(enabled, 128, 128, headset,
                false, false, false, false, false, false);
            Invoke(device, "PrepareOutReport");
            var report = Get<byte[]>(device, typeof(DS4Device), "outputReport");
            int offset = connection == ConnectionType.USB ? 8 : 9;
            Assert.AreEqual((byte)expected, report[offset], "USB PCM speaker routing and the Bluetooth media snapshot use different audio-control contracts.");
            if (connection == ConnectionType.USB)
            {
                byte[] written = null;
                device.PhysicalRawOutputWriteTestHook = bytes => { written = (byte[])bytes.Clone(); return true; };
                var native = RawNative(0x21, 17);
                native[1] |= 0x80; native[8] = 0x09;
                Assert.IsTrue(device.WriteRawOutputReportFromGame(native, 0, native.Length));
                Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
                Assert.AreEqual((byte)expected, written[8], "Native game reports must retain the profile's USB speaker path.");
                Invoke(device, "PrepareOutReport");
                Assert.AreEqual((byte)expected, report[8], "The cached native underlay must retain the USB speaker path too.");
            }
        }
        finally { device.ReadWaitEv.Dispose(); }
    }

    [TestMethod]
    public void UsbPassthruRestoresLatestIndicatorsAfterOffWithoutAnotherApplicationPacket()
    {
        var device = CreateDevice(ConnectionType.USB);
        device.SetProfileLightbarOwnership(false);
        device.ApplyProfileIndicators(ProfilePlayerIndicators.DeviceSettings,
            ProfileMicrophoneIndicator.DeviceSettings, passthrough: true);
        byte[] written = null;
        device.PhysicalRawOutputWriteTestHook = report => { written = (byte[])report.Clone(); return true; };
        byte[] native = RawNative(0x21, 17);
        native[2] = 0x11; native[44] = 0x1F; native[9] = 2;
        Assert.IsTrue(device.WriteRawOutputReportFromGame(native, 0, native.Length));
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
        Assert.AreEqual((byte)2, written[9], "A fresh browser Blink must survive USB audio composition.");
        Assert.AreEqual((byte)0x1F, written[44]);
        // A later mic-only delta must update Blink to On and retain the player mask.
        native[2] = 1; native[9] = 1; native[44] = 0;
        Assert.IsTrue(device.WriteRawOutputReportFromGame(native, 0, native.Length));
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
        Assert.AreEqual((byte)1, written[9]);
        Assert.AreEqual((byte)0x1F, written[44]);
        var originalUnderlay = Mailbox(device).ReadLatest().DsxNativeState;
        device.ApplyProfileIndicators(ProfilePlayerIndicators.Off, ProfileMicrophoneIndicator.Off, true, true);
        Invoke(device, "PrepareOutReport");
        var output = Get<byte[]>(device, typeof(DS4Device), "outputReport");
        Assert.AreEqual((byte)0, output[44]); Assert.AreEqual((byte)0, output[9]);
        device.ApplyProfileIndicators(ProfilePlayerIndicators.DeviceSettings,
            ProfileMicrophoneIndicator.DeviceSettings, true, true);
        Invoke(device, "PrepareOutReport");
        Assert.AreEqual((byte)0x1F, output[44]); Assert.AreEqual((byte)1, output[9]);
        Assert.AreEqual(0x11, output[2] & 0x11);
        Assert.AreEqual(originalUnderlay, Mailbox(device).ReadLatest().DsxNativeState);
        device.SetProfileLightbarOwnership(true);
        Invoke(device, "PrepareOutReport");
        Assert.AreEqual((byte)0, output[9], "Normal mode keeps the existing microphone device policy.");
        device.SetProfileLightbarOwnership(false);
        Invoke(device, "PrepareOutReport");
        Assert.AreEqual((byte)1, output[9], "Live Passthru changes must restore native state too.");
        native[2] = 0x11; native[9] = 0; native[44] = 0;
        Assert.IsTrue(device.WriteRawOutputReportFromGame(native, 0, native.Length));
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
        Assert.AreEqual((byte)0, written[44]); Assert.AreEqual((byte)0, written[9]);
        Invoke(device, "PrepareOutReport");
        Assert.AreEqual((byte)0, output[44]); Assert.AreEqual((byte)0, output[9]);
        device.ReadWaitEv.Dispose();
    }

    [TestMethod]
    public void BluetoothPassthruRestoresBothIndicatorsThroughTheRealCombinedCompositor()
    {
        var device = CreateDevice(ConnectionType.BT);
        Set(device, typeof(DualSenseDevice), "bluetoothOutputTransportStopping", 1);
        device.SetProfileLightbarOwnership(false);
        device.ApplyProfileIndicators(ProfilePlayerIndicators.Off, ProfileMicrophoneIndicator.Off, true, true);
        byte[] raw = RawNative(0x21, 17); raw[2] = 0x11; raw[44] = 0x1F; raw[9] = 2;
        Assert.IsTrue(device.WriteRawOutputReportFromGame(raw, 0, raw.Length));
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published, device.ProcessNextPhysicalOutputCommand());
        var before = Mailbox(device).ReadLatest().DsxNativeState;
        device.ApplyProfileIndicators(ProfilePlayerIndicators.DeviceSettings,
            ProfileMicrophoneIndicator.DeviceSettings, true, true);
        byte[] combined = new byte[398];
        // Exercise the same final overlay used by idle writes and speaker templates.
        var compositor = typeof(DualSenseDevice).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == "ApplyBluetoothMicrophoneStreamingRequest" && m.GetParameters().Length == 5);
        compositor.Invoke(device, new object[] { combined, Mailbox(device).ReadLatest(), (byte)0, false, -1 });
        Assert.AreEqual((byte)0x1F, combined[13 + 43]); Assert.AreEqual((byte)2, combined[13 + 8]);
        Assert.AreEqual(0x11, combined[14] & 0x11);
        Assert.AreEqual(before, Mailbox(device).ReadLatest().DsxNativeState);
        device.ReadWaitEv.Dispose();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PhysicalUsbQueueAppliesProfileLightbarPolicyWithoutChangingNativeUnderlay(bool normalMode)
    {
        var device = CreateDevice(ConnectionType.USB);
        var profileColor = new DS4Color(7, 8, 239);
        Mailbox(device).SetProfileLightbar(new DS4LightbarState { LightBarColor = profileColor });
        device.SetProfileLightbarOwnership(normalMode);
        byte[] raw = RawNative(0x21, 17);
        byte[] expectedRaw = (byte[])raw.Clone();
        byte[] written = null;
        device.PhysicalRawOutputWriteTestHook = report => { written = (byte[])report.Clone(); return true; };
        Assert.IsTrue(device.WriteRawOutputReportFromGame(raw, 0, raw.Length));
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published,
            device.ProcessNextPhysicalOutputCommand());
        Assert.IsNotNull(written);
        Assert.AreEqual(normalMode ? profileColor.red : raw[45], written[45]);
        Assert.AreEqual(normalMode ? profileColor.green : raw[46], written[46]);
        Assert.AreEqual(normalMode ? profileColor.blue : raw[47], written[47]);
        CollectionAssert.AreEqual(expectedRaw, raw);
        CollectionAssert.AreEqual(raw.Skip(22).Take(11).ToArray(), written.Skip(22).Take(11).ToArray());
        Assert.AreEqual(default(DualSenseDsxOverlay).ObserveNative(raw, 1), Mailbox(device).ReadLatest().DsxNativeState);
    }

    [TestMethod]
    public void LiveProfileLightbarRoutinePublishesNormalAndPassThroughOwnership()
    {
        int slot = DS4LightBar.forcelight.Length - 1;
        var lighting = Global.getLightbarSettingsInfo(slot);
        var previousMode = lighting.mode;
        bool previousForce = DS4LightBar.forcelight[slot];
        bool previousDefault = DS4LightBar.defaultLight;
        var device = CreateDevice(ConnectionType.USB);
        try
        {
            DS4LightBar.forcelight[slot] = false;
            DS4LightBar.defaultLight = false;
            lighting.mode = LightbarMode.DS4Win;
            DS4LightBar.updateLightBar(device, slot);
            Assert.IsTrue(Mailbox(device).ReadLatest().ProfileOwnsLightbar);
            lighting.mode = LightbarMode.Passthru;
            DS4LightBar.updateLightBar(device, slot);
            Assert.IsFalse(Mailbox(device).ReadLatest().ProfileOwnsLightbar);
        }
        finally
        {
            lighting.mode = previousMode;
            DS4LightBar.forcelight[slot] = previousForce;
            DS4LightBar.defaultLight = previousDefault;
            device.ReadWaitEv.Dispose();
        }
    }

    [TestMethod]
    public void UsbUnderlayCommitsOriginalBytesOnlyAfterSuccessfulPhysicalWrite()
    {
        var device = CreateDevice(ConnectionType.USB);
        var mailbox = Mailbox(device);
        byte[] raw = RawNative(0x21, 17);
        byte[] expectedRaw = (byte[])raw.Clone();
        byte[] prepared = PreparedNative(raw);
        object owner = new();
        device.TryUpdateDsxOverlay(owner, state => state with
        {
            Left = Effect(0x26, 63), Color = new DS4Color(99, 88, 77), MicLed = 0, PlayerLeds = 0x1F,
        });
        bool succeed = false;
        byte[] written = null;
        device.PhysicalRawOutputWriteTestHook = report =>
        {
            written = (byte[])report.Clone();
            return succeed;
        };

        Assert.IsTrue(device.WriteRawOutputReportFromGame(prepared, 0, prepared.Length,
            out long revision, raw, 0));
        Assert.IsTrue(revision > 0);
        Array.Fill(raw, (byte)0xEE);
        Array.Fill(prepared, (byte)0xDD);
        Assert.AreEqual(0, mailbox.ReadLatest().DsxNativeState.Fields,
            "Enqueuing a packet does not prove that its native state took effect.");
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Retry,
            device.ProcessNextPhysicalOutputCommand());
        Assert.AreEqual(0, mailbox.ReadLatest().DsxNativeState.Fields,
            "A failed write cannot replace the native restoration underlay.");

        succeed = true;
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published,
            device.ProcessNextPhysicalOutputCommand());
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.None,
            device.ProcessNextPhysicalOutputCommand());
        Assert.AreEqual((byte)0x26, written[22], "The physical write still carries the active DSX override.");
        Assert.AreEqual((byte)0, written[9]);
        CollectionAssert.AreEqual(new byte[] { 0x1F, 99, 88, 77 }, written.Skip(44).Take(4).ToArray());
        var expected = default(DualSenseDsxOverlay).ObserveNative(expectedRaw, 1);
        Assert.AreEqual(expected, mailbox.ReadLatest().DsxNativeState,
            "The restoration shadow must retain the original native delta, not Trigger Lab or DSX composition.");
        Assert.IsNull(mailbox.ReadLatest().DsxNativeState.Right,
            "An injected right-trigger validity bit must not invent a native right-trigger effect.");

        device.ReleaseDsxOverlay(owner);
        Invoke(device, "PrepareOutReport");
        byte[] restored = Get<byte[]>(device, typeof(DS4Device), "outputReport");
        CollectionAssert.AreEqual(expectedRaw.Skip(22).Take(11).ToArray(),
            restored.Skip(22).Take(11).ToArray(),
            "The real USB local-output preparation must restore the native effect without another game packet.");
        Assert.AreEqual((byte)0x08, (byte)(restored[1] & 0x08));
        Assert.AreEqual(expectedRaw[9], restored[9]);
        CollectionAssert.AreEqual(expectedRaw.Skip(44).Take(4).ToArray(),
            restored.Skip(44).Take(4).ToArray(),
            "RGB, mic LED, and player LED reset must restore native state in the same local generation.");
    }

    [TestMethod]
    public void FullPhysicalQueueCannotCommitRejectedNativeUnderlay()
    {
        var device = CreateDevice(ConnectionType.USB);
        byte[] accepted = RawNative(0x21, 17);
        byte[] rejected = RawNative(0x25, 51);
        for (int index = 0; index < 64; index++)
            Assert.IsTrue(device.WriteRawOutputReportFromGame(accepted, 0, accepted.Length,
                out _, accepted, 0));

        Assert.IsFalse(device.WriteRawOutputReportFromGame(rejected, 0, rejected.Length,
            out long rejectedRevision, rejected, 0));

        Assert.AreEqual(0L, rejectedRevision);
        Assert.AreEqual(0, Mailbox(device).ReadLatest().DsxNativeState.Fields);
    }

    [TestMethod]
    public void BluetoothRawFifoCommitsOriginalDeltaWithItsDurablePendingTransaction()
    {
        var device = CreateDevice(ConnectionType.BT);
        // Suppress recovery/helper startup; exercise the real retained native transaction.
        Set(device, typeof(DualSenseDevice), "bluetoothOutputTransportStopping", 1);
        byte[] raw = RawNative(0x21, 17);
        byte[] expectedRaw = (byte[])raw.Clone();
        byte[] prepared = PreparedNative(raw);
        Assert.IsTrue(device.WriteRawOutputReportFromGame(prepared, 0, prepared.Length,
            out long revision, raw, 0));
        Array.Fill(raw, (byte)0xEE);
        Assert.AreEqual(0, Mailbox(device).ReadLatest().DsxNativeState.Fields);

        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published,
            device.ProcessNextPhysicalOutputCommand());

        Assert.AreEqual(revision, Get<long>(device, typeof(DualSenseDevice),
            "pendingBluetoothNativeGameRevision"));
        Assert.AreEqual(default(DualSenseDsxOverlay).ObserveNative(expectedRaw, 1),
            Mailbox(device).ReadLatest().DsxNativeState);
        Assert.IsNull(Get<object>(device, typeof(DualSenseDevice), "bluetoothAudioPacer"));
        Assert.AreEqual(0, Get<int>(device, typeof(DualSenseDevice), "physicalOutputCommandCount"));
    }

    [TestMethod]
    public void BluetoothCombinedRejectionCannotReplaceThePreviousNativeUnderlay()
    {
        var device = CreateDevice(ConnectionType.BT);
        Set(device, typeof(DualSenseDevice), "bluetoothOutputTransportStopping", 1);
        byte[] raw = RawNative(0x21, 17);
        byte[] prepared = Combined(PreparedNative(raw));
        Assert.IsFalse(device.WriteBluetoothCombinedHapticsAudioOutputReport(
            prepared, 0, prepared.Length, true, out long firstRevision, raw, 0),
            "The synthetic transport has no pacer and must retain the exact transaction.");
        Assert.IsTrue(firstRevision > 0);
        Assert.AreEqual(firstRevision, Get<long>(device, typeof(DualSenseDevice),
            "pendingBluetoothNativeGameRevision"));
        var firstUnderlay = Mailbox(device).ReadLatest().DsxNativeState;
        Assert.AreEqual(default(DualSenseDsxOverlay).ObserveNative(raw, 1), firstUnderlay);
        byte[] laterRaw = RawNative(0x25, 51);
        byte[] laterPrepared = Combined(PreparedNative(laterRaw));

        Assert.IsFalse(device.WriteBluetoothCombinedHapticsAudioOutputReport(
            laterPrepared, 0, laterPrepared.Length, true, out long laterRevision, laterRaw, 0));

        Assert.AreEqual(0L, laterRevision, "Pending A must reject B before B acquires a native revision.");
        Assert.AreEqual(firstUnderlay, Mailbox(device).ReadLatest().DsxNativeState);
        Assert.AreEqual(firstRevision, Get<long>(device, typeof(DualSenseDevice),
            "pendingBluetoothNativeGameRevision"));
        Assert.IsNull(Get<object>(device, typeof(DualSenseDevice), "bluetoothAudioPacer"));
    }

    [TestMethod]
    public void ZeroGameRumbleRetainsTriggerLabOwnershipUntilThatMappingIsReleased()
    {
        var device = CreateDevice(ConnectionType.USB);
        object owner = new();
        var mod = Effect(0x26, 63);
        device.TryUpdateDsxOverlay(owner, state => state with { Left = mod });

        TriggerLabEffectEncoder.ApplyGameRumbleToDevice(device, TriggerId.LeftTrigger,
            new TriggerLabEffectSettings(), persistentEffectActive: false, magnitude: 0);

        var snapshot = Mailbox(device).ReadLatest();
        Assert.IsTrue(snapshot.LeftTriggerLabActive);
        Assert.AreEqual((byte)0x05, snapshot.ForLocalTriggerReport().LeftTrigger.triggerMotorMode);
        Assert.AreEqual(mod, snapshot.DsxOverlay.Left.Value);
        TriggerLabEffectEncoder.ApplyToDevice(device, TriggerId.LeftTrigger,
            new TriggerLabEffectSettings(), active: false);
        snapshot = Mailbox(device).ReadLatest();
        Assert.IsFalse(snapshot.LeftTriggerLabActive);
        Assert.AreEqual(mod, snapshot.ForLocalTriggerReport().LeftTrigger);
    }

    [TestMethod]
    public void UsbExplicitOffPublishesEvenWhenEqualToProfileAndDoesNotRearmOnALaterLedEdit()
    {
        var device = CreateDevice(ConnectionType.USB);
        var off = new DualSenseDevice.TriggerEffectData { triggerMotorMode = 0x05 };
        Mailbox(device).SetTrigger(TriggerId.LeftTrigger, off);
        byte[] native = RawNative(0x21, 17);
        device.PhysicalRawOutputWriteTestHook = _ => true;
        Assert.IsTrue(device.WriteRawOutputReportFromGame(native, 0, native.Length));
        Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published,
            device.ProcessNextPhysicalOutputCommand());
        Invoke(device, "PrepareOutReport");
        long beforeOverlay = Get<long>(device, typeof(DualSenseDevice), "preparedLocalLeftTriggerGeneration");
        ModelSuccessfulLocalTriggerAcknowledgement(device);
        object owner = new();

        device.TryUpdateDsxOverlay(owner, state => state with { Left = off });
        Invoke(device, "PrepareOutReport");

        Assert.IsTrue(Get<long>(device, typeof(DualSenseDevice), "preparedLocalLeftTriggerGeneration") > beforeOverlay,
            "Taking ownership must publish an explicit Off even if its bytes equal the local profile value.");
        byte[] report = Get<byte[]>(device, typeof(DS4Device), "outputReport");
        Assert.AreEqual((byte)0x08, (byte)(report[1] & 0x08));
        Assert.AreEqual((byte)0x05, report[22]);

        // Model the completed-write acknowledgement without touching HID. The
        // real preparation path must not manufacture a new trigger generation.
        ModelSuccessfulLocalTriggerAcknowledgement(device);
        device.TryUpdateDsxOverlay(owner, state => state with { Color = new DS4Color(11, 22, 33) });
        Invoke(device, "PrepareOutReport");

        Assert.AreEqual((byte)0, (byte)(report[1] & 0x0C),
            "An unrelated LED edit must not repeat a held trigger effect after its generation was acknowledged.");
    }

    [TestMethod]
    [DoNotParallelize]
    public void RejectedNativeGameRumbleBDoesNotPublishItsLabStateOrRewriteQueuedA()
    {
        int slot = Global.TEST_PROFILE_ITEM_COUNT - 1;
        var previousSettings = Global.store.triggerLabSettings[slot];
        try
        {
            var lab = new TriggerLabProfileSettings
            {
                Enabled = true, LeftActive = false, LeftGameRumbleVibration = true,
                Left = new TriggerLabEffectSettings { Mode = TriggerLabMode.Vibration, StartPercent = 10, WallPercent = 50 },
            };
            Global.store.triggerLabSettings[slot] = lab;
            var device = CreateDevice(ConnectionType.USB);
            TriggerLabEffectEncoder.ApplyProfileToDevice(device, TriggerId.LeftTrigger,
                lab.Left, persistentEffectActive: false, gameRumbleEnabled: true);
            device.TryUpdateDsxOverlay(new object(), state => state with { Left = Effect(0x25, 99) });
            byte[] firstFeedback = NativeEnvelope(RawNative(0x21, 17), heavy: 63);
            byte[] preparedA = new byte[48];
            byte capturedLabMask = ViiperOutDevice.PrepareNativeDualSenseOutputReportForProfileInto(
                firstFeedback, slot, preparedA);
            Assert.AreEqual((byte)0x08, capturedLabMask);
            for (int index = 0; index < 64; index++)
                Assert.IsTrue(device.WriteRawOutputReportFromGame(preparedA, 0, preparedA.Length,
                    out _, firstFeedback, 28, capturedLabMask));
            var beforeB = Mailbox(device).ReadLatest();
            var output = new ViiperOutDevice(OutContType.None, ViiperVirtualDeviceType.DualSense);
            Set(output, typeof(ViiperOutDevice), "physicalDualSenseIdentityPath", string.Empty);
            Set(output, typeof(ViiperOutDevice), "physicalDualSenseIdentityVerified", true);
            byte[] rejectedB = NativeEnvelope(RawNative(0x25, 51), heavy: 231);

            bool accepted = (bool)typeof(ViiperOutDevice).GetMethod("TryApplyNativeDualSenseOutputReport",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(output,
                new object[] { device, slot, rejectedB, rejectedB.Length, new byte[48], 1L });

            Assert.IsFalse(accepted, "B must face the real full physical FIFO.");
            Assert.AreEqual(beforeB, Mailbox(device).ReadLatest(),
                "Preparation/rejection must not publish B's game-rumble effect to the live Trigger Lab mailbox.");
            byte[] written = null;
            device.PhysicalRawOutputWriteTestHook = report => { written = (byte[])report.Clone(); return true; };
            Assert.AreEqual(DualSenseDevice.PhysicalOutputCommandProcessResult.Published,
                device.ProcessNextPhysicalOutputCommand());
            CollectionAssert.AreEqual(preparedA.Skip(22).Take(11).ToArray(), written.Skip(22).Take(11).ToArray(),
                "A's captured canonical Trigger Lab bytes must survive live mailbox and DSX composition.");
            Assert.AreEqual((byte)0x26, written[22]);
        }
        finally { Global.store.triggerLabSettings[slot] = previousSettings; }
    }

    [TestMethod]
    public void BluetoothNativeTransactionKeepsCapturedLabAInsteadOfLiveLabB()
    {
        var device = CreateDevice(ConnectionType.BT);
        Set(device, typeof(DualSenseDevice), "bluetoothOutputTransportStopping", 1);
        Mailbox(device).SetTriggerLabTrigger(TriggerId.LeftTrigger, Effect(0x26, 77), true);
        device.TryUpdateDsxOverlay(new object(), state => state with { Left = Effect(0x26, 99) });
        byte[] raw = RawNative(0x21, 17);
        byte[] preparedA = PreparedNative(raw);
        byte[] combinedA = Combined(preparedA);

        Assert.IsFalse(device.WriteBluetoothCombinedHapticsAudioOutputReport(
            combinedA, 0, combinedA.Length, true, out long revision, raw, 0,
            preparedTriggerLabValidity: 0x08));

        Assert.IsTrue(revision > 0);
        byte[] retained = Get<byte[]>(device, typeof(DualSenseDevice), "pendingBluetoothNativeGameExactState");
        CollectionAssert.AreEqual(preparedA.Skip(22).Take(11).ToArray(), retained.Skip(34).Take(11).ToArray(),
            "The durable Bluetooth transaction must contain admitted A, not later live Trigger Lab B or DSX.");
        Assert.AreEqual(default(DualSenseDsxOverlay).ObserveNative(raw, 1), Mailbox(device).ReadLatest().DsxNativeState);
    }

    [TestMethod]
    public void GameRumbleOnlyProfileOwnsItsTriggerBeforeTheFirstNativeFeedbackPacket()
    {
        var device = CreateDevice(ConnectionType.USB);
        var mod = Effect(0x26, 99);
        device.TryUpdateDsxOverlay(new object(), state => state with { Left = mod, Right = mod });

        TriggerLabEffectEncoder.ApplyProfileToDevice(device, TriggerId.LeftTrigger,
            new TriggerLabEffectSettings(), persistentEffectActive: false, gameRumbleEnabled: true);

        var snapshot = Mailbox(device).ReadLatest();
        Assert.IsTrue(snapshot.LeftTriggerLabActive);
        Assert.IsFalse(snapshot.RightTriggerLabActive);
        Assert.AreEqual((byte)0x05, snapshot.ForLocalTriggerReport().LeftTrigger.triggerMotorMode);
        Assert.AreEqual(mod, snapshot.ForLocalTriggerReport().RightTrigger);
        Assert.AreEqual(mod, snapshot.DsxOverlay.Left.Value, "Profile ownership must not erase the mod's held effect.");
    }

    [TestMethod]
    public void ProfileRestorationRetainsGameRumbleOwnershipAndTheOtherLegacySideThenReleasesOnPause()
    {
        var left = new TriggerOutputSettings
        {
            triggerEffect = TriggerEffects.Resistance,
            effectSettings = new TriggerEffectSettings { startValue = 2, maxValue = 80 },
        };
        var right = new TriggerOutputSettings
        {
            triggerEffect = TriggerEffects.FullClick,
            effectSettings = new TriggerEffectSettings { startValue = 4, maxValue = 180 },
        };
        var expectedDevice = CreateDevice(ConnectionType.USB);
        TriggerLabProfileEffectRestoration.ApplyToDevice(expectedDevice,
            new TriggerLabProfileSettings(), left, right);
        var legacy = Mailbox(expectedDevice).ReadLatest();
        var device = CreateDevice(ConnectionType.USB);
        var mod = Effect(0x26, 99);
        device.TryUpdateDsxOverlay(new object(), state => state with { Left = mod, Right = mod });
        var settings = new TriggerLabProfileSettings
        {
            Enabled = true, LeftGameRumbleVibration = true,
            LeftActive = false, RightActive = false,
        };

        TriggerLabProfileEffectRestoration.ApplyToDevice(device, settings, left, right);

        var active = Mailbox(device).ReadLatest();
        Assert.IsTrue(active.LeftTriggerLabActive,
            "Applying/restoring a game-rumble-only profile must claim its selected side before feedback arrives.");
        Assert.IsFalse(active.RightTriggerLabActive);
        Assert.AreEqual(legacy.RightTrigger, active.RightTrigger,
            "Adding ownership on the left must not replace the ordinary right profile effect with Off.");
        Assert.AreEqual(mod, active.ForLocalTriggerReport().RightTrigger);
        settings.Enabled = false;

        TriggerLabProfileEffectRestoration.ApplyToDevice(device, settings, left, right);

        var paused = Mailbox(device).ReadLatest();
        Assert.IsFalse(paused.LeftTriggerLabActive);
        Assert.IsFalse(paused.RightTriggerLabActive);
        Assert.AreEqual(legacy.LeftTrigger, paused.LeftTrigger);
        Assert.AreEqual(legacy.RightTrigger, paused.RightTrigger);
        Assert.AreEqual(mod, paused.ForLocalTriggerReport().LeftTrigger);
    }

    private static DualSenseDevice CreateDevice(ConnectionType connection)
    {
        var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
        Set(hid, typeof(HidDevice), "_deviceAttributes", new HidDeviceAttributes(
            new NativeMethods.HIDD_ATTRIBUTES { VendorID = 0x054C, ProductID = 0x0CE6 }));
        var device = new DualSenseDevice(hid, "DSX native underlay regression");
        Set(device, typeof(DS4Device), "conType", connection);
        Set(device, typeof(DS4Device), "outputReport", new byte[connection == ConnectionType.USB ? 64 : 78]);
        return device;
    }

    private static byte[] RawNative(byte mode, byte seed)
    {
        byte[] report = new byte[48];
        report[0] = 0x02;
        report[1] = 0x08;
        report[2] = 0x15;
        report[22] = mode;
        for (int index = 1; index < 11; index++) report[22 + index] = (byte)(seed + index);
        report[9] = 2;
        report[44] = 0x11;
        report[45] = 17;
        report[46] = 83;
        report[47] = 201;
        return report;
    }

    private static byte[] NativeEnvelope(byte[] raw, byte heavy)
    {
        byte[] feedback = new byte[ViiperOutDevice.DualSenseAtomicFeedbackLength];
        feedback[0] = heavy;
        Array.Copy(raw, 0, feedback, 28, raw.Length);
        return feedback;
    }

    private static byte[] PreparedNative(byte[] raw)
    {
        var prepared = (byte[])raw.Clone();
        prepared[1] |= 0x0C;
        DualSenseDsxOverlay.WriteTrigger(prepared, 22, Effect(0x25, 31));
        DualSenseDsxOverlay.WriteTrigger(prepared, 11, Effect(0x25, 47));
        prepared[9] = 0;
        prepared[44] = 0x04;
        prepared[45] = 1;
        prepared[46] = 2;
        prepared[47] = 3;
        return prepared;
    }

    private static byte[] Combined(byte[] prepared)
    {
        byte[] combined = new byte[398];
        combined[0] = 0x36;
        combined[11] = 0x90;
        combined[12] = 63;
        combined[76] = 0x92;
        combined[77] = 64;
        Array.Copy(prepared, 1, combined, 13, 47);
        return combined;
    }

    private static DualSenseDevice.TriggerEffectData Effect(byte mode, byte seed) => new()
    {
        triggerMotorMode = mode,
        triggerStartResistance = seed,
        triggerEffectForce = (byte)(seed + 1),
    };

    private static DualSensePhysicalOutputStateMailbox Mailbox(DualSenseDevice device) =>
        Get<DualSensePhysicalOutputStateMailbox>(device, typeof(DualSenseDevice), "physicalOutputStateMailbox");

    private static void ModelSuccessfulLocalTriggerAcknowledgement(DualSenseDevice device)
    {
        Set(device, typeof(DualSenseDevice), "submittedLocalLeftTriggerGeneration",
            Get<long>(device, typeof(DualSenseDevice), "preparedLocalLeftTriggerGeneration"));
        Set(device, typeof(DualSenseDevice), "submittedLocalRightTriggerGeneration",
            Get<long>(device, typeof(DualSenseDevice), "preparedLocalRightTriggerGeneration"));
    }

    private static T Get<T>(object target, Type type, string name) =>
        (T)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void Set(object target, Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string name) =>
        typeof(DualSenseDevice).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
}
