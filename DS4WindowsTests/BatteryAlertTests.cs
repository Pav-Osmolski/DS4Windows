using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms.ViewModels.SpecialActions;
using NAudio.Wave;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class BatteryAlertTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow(false, false, true)]
    [DataRow(false, false, false)]
    [DataRow(true, true, false)]
    public async Task UnavailableControllerSpeakerReportsOneHelpfulMessageWithoutExceptionTrace(bool speaker, bool headsetOnly, bool disconnected)
    {
        var previous = Global.BatteryAlerts;
        bool previousSpeaker = Global.DualSenseEnableSpeakerOutput[0];
        bool previousHeadset = Global.DualSenseHeadsetOnlyAudio[0];
        using var device = new Device();
        var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        hub.DS4Controllers = new DS4Device[8];
        if (!disconnected) hub.DS4Controllers[0] = device;
        var service = new BatteryAlertService(hub);
        var statuses = new List<string>();
        var logs = new List<string>();
        service.StatusChanged += statuses.Add;
        EventHandler<DebugEventArgs> log = (_, e) => logs.Add(e.Data);
        AppLogger.GuiLog += log;
        try
        {
            Global.BatteryAlerts = new BatteryAlertSettings { Endpoint = "controller" };
            Global.DualSenseEnableSpeakerOutput[0] = speaker;
            Global.DualSenseHeadsetOnlyAudio[0] = headsetOnly;
            foreach (var kind in new[] { BatteryAlertKind.Low, BatteryAlertKind.Full, BatteryAlertKind.Check })
            {
                statuses.Clear(); logs.Clear();
                await service.PreviewAsync(0, kind);
                string expected = disconnected
                    ? "Battery alert: Connect the controller before previewing its speaker."
                    : "Battery alert: Enable 'Stream audio to controller' in Profile Editor > Advanced > Audio and turn off 'Headset only'.";
                CollectionAssert.AreEqual(new[] { expected }, statuses);
                CollectionAssert.AreEqual(new[] { expected }, logs);
                Assert.IsFalse(device.Preview);
            }
        }
        finally
        {
            service.Stop(); AppLogger.GuiLog -= log;
            Global.BatteryAlerts = previous;
            Global.DualSenseEnableSpeakerOutput[0] = previousSpeaker;
            Global.DualSenseHeadsetOnlyAudio[0] = previousHeadset;
        }
    }

    [TestMethod]
    public async Task OptInDefaultEndpointPlaysBatterySpeech()
    {
        if (Environment.GetEnvironmentVariable("DS4W_BATTERY_PLAYBACK_SMOKE") != "1")
            Assert.Inconclusive("Set DS4W_BATTERY_PLAYBACK_SMOKE=1 to play a short battery preview on the default output.");
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
        TestContext.WriteLine($"Playback endpoint: {device.FriendlyName}; mix: {device.AudioClient.MixFormat}");
        var previous = Global.BatteryAlerts;
        try
        {
            Global.BatteryAlerts = new BatteryAlertSettings { Volume = 15 };
            var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
            hub.DS4Controllers = new DS4Device[8];
            var service = new BatteryAlertService(hub);
            var messages = new List<string>();
            service.StatusChanged += messages.Add;
            foreach (var kind in new[] { BatteryAlertKind.Low, BatteryAlertKind.Full, BatteryAlertKind.Check })
            {
                messages.Clear();
                await service.PreviewAsync(0, kind);
                Assert.AreEqual("Battery alert finished.", messages.Last());
            }
        }
        finally { Global.BatteryAlerts = previous; }
    }

    [TestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    public void EndpointPcmFormatIsPreservedAndSamplesAreClamped(int bits)
    {
        WaveFormat format = bits == 32 ? new WaveFormat(48000, bits, 4) : new WaveFormatExtensible(48000, bits, 4);
        var provider = new BatteryAlertOutputProvider(new BatteryAlertChannelProvider(new Samples(-2, 2), 4, true), format);
        Assert.AreSame(format, provider.WaveFormat);
        var buffer = new byte[format.BlockAlign * 2 + 7];
        int bytes = bits / 8;
        Assert.AreEqual(format.BlockAlign * 2, provider.Read(buffer, 3, format.BlockAlign * 2));
        Assert.AreEqual(0x80, buffer[3 + bytes * 2 - 1]);
        Assert.AreEqual(0x7f, buffer[3 + format.BlockAlign + bytes * 2 - 1]);
        for (int frame = 0; frame < 2; frame++)
            for (int channel = 0; channel < 4; channel++)
                if (channel != 1)
                    for (int b = 0; b < bytes; b++) Assert.AreEqual(0, buffer[3 + frame * format.BlockAlign + channel * bytes + b]);
        Assert.AreEqual(0, provider.Read(buffer, 0, buffer.Length));
    }

    [TestMethod]
    public void EndpointFloatFormatKeepsItsChannelMaskAndSpeakerRouting()
    {
        var format = new WaveFormatExtensible(48000, 32, 4);
        var provider = new BatteryAlertOutputProvider(new BatteryAlertChannelProvider(new Samples(.5f), 4, true), format);
        Assert.AreSame(format, provider.WaveFormat);
        var buffer = new byte[format.BlockAlign];
        Assert.AreEqual(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        var values = new float[4]; Buffer.BlockCopy(buffer, 0, values, 0, buffer.Length);
        CollectionAssert.AreEqual(new float[] { 0, .5f, 0, 0 }, values);
        Assert.Throws<NotSupportedException>(() => new BatteryAlertOutputProvider(new Samples(0), new WaveFormat(44100, 16, 1)));
    }

    [TestMethod]
    public async Task OptInInstalledVoicesProducePcmWithoutPlayingAudio()
    {
        if (Environment.GetEnvironmentVariable("DS4W_BATTERY_SPEECH_SMOKE") != "1")
            Assert.Inconclusive("Set DS4W_BATTERY_SPEECH_SMOKE=1 to check installed Windows voices without audio playback.");
        var voices = BatteryAlertAudio.Voices();
        Assert.IsTrue(voices.Count > 1, "Install a Windows speech voice before running this check.");
        foreach (var voice in voices.Where(v => v.Id != "").Take(2))
        {
            var audio = await BatteryAlertAudio.SpeakAsync("Controller one battery: fifty percent.",
                new BatteryAlertSettings { Voice = voice.Id }, CancellationToken.None);
            Assert.IsTrue(audio.Length > 4800);
            Assert.IsTrue(audio.Any(sample => Math.Abs(sample) > .001));
            TestContext.WriteLine($"Synthesised {voice.Name}: {audio.Length} mono samples at 48 kHz; not played.");
        }
        var fallback = await BatteryAlertAudio.SpeakAsync("Battery alert preview.",
            new BatteryAlertSettings { Voice = "missing-voice-fixture", Rate = 1.3 }, CancellationToken.None);
        Assert.IsTrue(fallback.Length > 0);
    }

    [TestMethod]
    public void OptionalDefaultsAndLegacyConfigurationRemainSilent()
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        var dto = (AppSettingsDTO)serializer.Deserialize(new StringReader("<Profile/>"))!;
        Assert.IsFalse(dto.BatteryAlerts.LowEnabled);
        Assert.IsFalse(dto.BatteryAlerts.FullEnabled);
        Assert.IsFalse(dto.BatteryAlerts.Check.Vibrate);
        var policy = new BatteryAlertPolicy();
        for (int i = 0; i < 10; i++) Assert.IsNull(policy.Observe(new(5, false, true, true), dto.BatteryAlerts, i));
    }

    [TestMethod]
    public void LowAlertRequiresStableLowBandAndRearmsOnlyAfterRecovery()
    {
        var settings = new BatteryAlertSettings { LowEnabled = true };
        var policy = new BatteryAlertPolicy();
        Assert.IsNull(policy.Observe(new(20, false, false, true), settings, 0));
        Assert.IsNull(policy.Observe(new(19, false, false, true), settings, 1));
        Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(new(18, false, false, true), settings, 2));
        policy.Pause(); // Disconnect/reconnect retains the latch, not a half-complete reading.
        for (int i = 3; i < 12; i++) Assert.IsNull(policy.Observe(new(10, false, false, true), settings, i));
        for (int i = 0; i < 3; i++) Assert.IsNull(policy.Observe(new(30, false, false, true), settings, 600 + i));
        Assert.IsNull(policy.Observe(new(20, false, false, true), settings, 603));
        Assert.IsNull(policy.Observe(new(20, false, false, true), settings, 604));
        Assert.AreEqual(BatteryAlertKind.Low, policy.Observe(new(20, false, false, true), settings, 605));
    }

    [TestMethod]
    public void UnknownReadingsChargingAndTransientThresholdCrossingsDoNotAlert()
    {
        var settings = new BatteryAlertSettings { LowEnabled = true };
        var policy = new BatteryAlertPolicy();
        for (int i = 0; i < 5; i++) Assert.IsNull(policy.Observe(new(0, false, false, false), settings, i));
        for (int i = 5; i < 10; i++) Assert.IsNull(policy.Observe(new(10, true, false, true), settings, i));
        Assert.IsNull(policy.Observe(new(10, false, false, true), settings, 10));
        Assert.IsNull(policy.Observe(new(25, false, false, true), settings, 11));
        Assert.IsNull(policy.Observe(new(10, false, false, true), settings, 12));
        policy.Pause();
        Assert.IsNull(policy.Observe(new(10, false, false, true), settings, 13));
    }

    [TestMethod]
    public void FullAlertRequiresExplicitCompletionAndDoesNotRepeatFromChargingNoise()
    {
        var settings = new BatteryAlertSettings { FullEnabled = true };
        var policy = new BatteryAlertPolicy();
        for (int i = 0; i < 6; i++) Assert.IsNull(policy.Observe(new(100, true, false, true), settings, i));
        Assert.IsNull(policy.Observe(new(100, false, true, true), settings, 6));
        Assert.IsNull(policy.Observe(new(100, false, true, true), settings, 7));
        Assert.AreEqual(BatteryAlertKind.Full, policy.Observe(new(100, false, true, true), settings, 8));
        for (int i = 600; i < 603; i++) Assert.IsNull(policy.Observe(new(100, true, false, true), settings, i));
        for (int i = 603; i < 610; i++) Assert.IsNull(policy.Observe(new(100, false, true, true), settings, i));
    }

    [TestMethod]
    public void SonyCompletionDecodingRejectsHighCapacityAndChargingErrors()
    {
        Assert.IsFalse(BatteryAlertReports.DualShock4Complete(0x1A));
        Assert.IsTrue(BatteryAlertReports.DualShock4Complete(0x1B));
        Assert.IsFalse(BatteryAlertReports.DualShock4Complete(0x1E));
        Assert.IsFalse(BatteryAlertReports.DualShock4Valid(0x1F));
        Assert.IsTrue(BatteryAlertReports.DualSenseComplete(0x20));
        Assert.IsFalse(BatteryAlertReports.DualSenseComplete(0xA0));
        Assert.IsFalse(BatteryAlertReports.DualSenseComplete(0xF0));
        Assert.IsFalse(BatteryAlertReports.DualSenseValid(0xF0));
        Assert.IsTrue(BatteryAlertReports.DualSenseValid(0x1A));
    }

    [TestMethod]
    public void SettingsRoundTripAndCopiesKeepIndependentPatternsAndSafeRanges()
    {
        var settings = new BatteryAlertSettings { LowEnabled = true, FullEnabled = true, Voice = "voice-id", Endpoint = "headset-id",
            Rate = double.NaN, Volume = 500, LowThreshold = 1 };
        settings.Low.Mode = BatteryAlertMode.Both;
        settings.Low.SoundFile = new string('a', 64) + ".wav";
        settings.Low.Duration = 500;
        settings.Full.Pulses = 4;
        settings.Check.Vibrate = true;
        var serializer = new XmlSerializer(typeof(BatteryAlertSettings));
        using var text = new StringWriter(); serializer.Serialize(text, settings);
        var copy = (BatteryAlertSettings)serializer.Deserialize(new StringReader(text.ToString()))!;
        Assert.AreEqual(1d, copy.Rate);
        Assert.AreEqual(100, copy.Volume);
        Assert.AreEqual(5, copy.LowThreshold);
        Assert.AreEqual(500, copy.Low.Duration);
        Assert.AreEqual(4, copy.Full.Pulses);
        Assert.IsTrue(copy.Check.Vibrate);
        Assert.AreEqual(settings.Low.SoundFile, copy.Low.SoundFile);
        var cloned = copy.Clone(); cloned.Low.Duration = 50;
        Assert.AreEqual(500, copy.Low.Duration);
        var store = new BackingStore { batteryAlerts = copy };
        var dto = new AppSettingsDTO(); dto.MapFrom(store);
        var restored = new BackingStore(); dto.MapTo(restored);
        Assert.AreEqual("headset-id", restored.batteryAlerts.Endpoint);
        Assert.AreEqual(4, restored.batteryAlerts.Full.Pulses);
    }

    [TestMethod]
    public void SpecialActionKeepsLegacyOptionsAndRecognisesTheOptionalAlertFlag()
    {
        var legacy = new SpecialAction("Battery", "PS", "BatteryCheck", "0|True|False|255|0|0|0|255|0");
        var modern = new SpecialAction("Battery", "PS", "BatteryCheck", "0|False|False|255|0|0|0|255|0|True");
        var vm = new CheckBatteryViewModel();
        vm.LoadAction(legacy); Assert.IsTrue(vm.Notification); Assert.IsFalse(vm.BatteryAlerts);
        vm.LoadAction(modern); Assert.IsTrue(vm.BatteryAlerts); Assert.IsFalse(vm.Notification);
    }

    [TestMethod]
    public void BatteryAvailabilityAndCoarseReadingsDoNotInventPercentagesOrFullCharge()
    {
        using var device = new Device();
        Assert.IsFalse(BatteryAlertService.Read(device).Reliable);
        device.Reading(10, false);
        var reading = BatteryAlertService.Read(device);
        Assert.IsTrue(reading.Reliable);
        Assert.IsFalse(reading.Full);
        Assert.AreEqual("Controller 1 battery: 10 percent.", BatteryAlertService.Message(0, BatteryAlertKind.Check, reading));
        Assert.AreEqual("Controller 2 battery low. 10 percent.", BatteryAlertService.Message(1, BatteryAlertKind.Low, reading));
        StringAssert.Contains(BatteryAlertService.Message(0, BatteryAlertKind.Check, new(90, false, false, true, "high")), "level: high");
    }

    [TestMethod]
    public void LegacyAlertReleasePreservesFeedbackPublishedDuringThePulse()
    {
        using var device = new Device();
        using var lease = device.TryBeginBatteryAlertRumble(64);
        Assert.IsNotNull(lease); Assert.IsTrue(device.Preview);
        device.setRumble(10, 20);
        lease.Dispose();
        Assert.IsFalse(device.Preview);
        Assert.AreEqual((byte)20, device.Rumble.RumbleMotorStrengthLeftHeavySlow);
        Assert.AreEqual((byte)10, device.Rumble.RumbleMotorStrengthRightLightFast);
        Assert.IsNull(device.TryBeginBatteryAlertRumble(64), "Active game rumble takes priority over starting a notification.");
    }

    [TestMethod]
    public void DualSenseAlertReleasePreservesNativeStateAndCannotClearAnEditorPreview()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        int changed = 0;
        using var lease = mailbox.TryBeginBatteryAlertRumble(80, () => changed++);
        Assert.IsNotNull(lease);
        var game = new DS4ForceFeedbackState { RumbleMotorStrengthLeftHeavySlow = 33 };
        mailbox.SetRumbleState(game, out _);
        lease.Dispose();
        Assert.AreEqual(1, changed);
        Assert.AreEqual((byte)33, mailbox.ReadLatest().RumbleState.RumbleMotorStrengthLeftHeavySlow);
        Assert.IsFalse(mailbox.ReadLatest().PreviewHeavyRumbleActive);
        mailbox.SetRumbleState(default, out _);
        using var old = mailbox.TryBeginBatteryAlertRumble(80, () => changed++);
        mailbox.SetRumblePreview(true, 12, true, 24, out _);
        old!.Dispose();
        Assert.IsTrue(mailbox.ReadLatest().PreviewHeavyRumbleActive);
        Assert.AreEqual((byte)24, mailbox.ReadLatest().PreviewHeavyRumbleStrength);
    }

    [TestMethod]
    public async Task CancellingAVibrationPreviewReleasesTheOwnedFeedback()
    {
        var previous = Global.BatteryAlerts;
        using var device = new Device();
        var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        hub.DS4Controllers = new DS4Device[8]; hub.DS4Controllers[0] = device;
        var service = new BatteryAlertService(hub);
        using var cancellation = new CancellationTokenSource();
        var playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += message => { if (message.StartsWith("Playing")) playing.TrySetResult(); };
        try
        {
            var settings = new BatteryAlertSettings();
            settings.Check.Mode = BatteryAlertMode.Off; settings.Check.Vibrate = true;
            settings.Check.Duration = 1000; settings.Check.Pulses = 5;
            Global.BatteryAlerts = settings;
            var preview = service.PreviewAsync(0, BatteryAlertKind.Check, cancellation.Token);
            await playing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(device.Preview);
            cancellation.Cancel();
            await preview.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(device.Preview);
        }
        finally { service.Stop(); Global.BatteryAlerts = previous; }
    }

    [TestMethod]
    public async Task ServiceStopCancelsPlaybackAndCannotRetargetQueuedAlertsAfterRestart()
    {
        var previous = Global.BatteryAlerts;
        using var first = new Device(); using var stale = new Device(); using var replacement = new Device();
        var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        hub.DS4Controllers = new DS4Device[8]; hub.DS4Controllers[0] = first; hub.DS4Controllers[1] = stale;
        var service = new BatteryAlertService(hub);
        var firstPlaying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += message =>
        {
            if (message.StartsWith("Playing") && first.Preview) firstPlaying.TrySetResult();
            if (message.StartsWith("Playing") && replacement.Preview) restarted.TrySetResult();
            if (message.Contains("stopped")) stopped.TrySetResult();
        };
        try
        {
            var settings = new BatteryAlertSettings();
            settings.Check.Mode = BatteryAlertMode.Off; settings.Check.Vibrate = true;
            settings.Check.Duration = 1000; settings.Check.Pulses = 5;
            Global.BatteryAlerts = settings;
            service.Start(); service.RequestCheck(0);
            await firstPlaying.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.RequestCheck(1); hub.DS4Controllers[1] = null;
            service.Stop();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(first.Preview); Assert.IsFalse(stale.Preview);
            hub.DS4Controllers[2] = replacement;
            service.Start(); service.RequestCheck(2);
            await restarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(stale.Preview);
        }
        finally
        {
            service.Stop();
            for (int i = 0; i < 100 && replacement.Preview; i++) await Task.Delay(10);
            Assert.IsFalse(replacement.Preview);
            Global.BatteryAlerts = previous;
        }
    }

    [TestMethod]
    [DataRow("tone.wav")]
    [DataRow("tone.mp3")]
    [DataRow("tone.aiff")]
    public async Task ImportsCommonFormatsAsOwnedValidatedPcm(string file)
    {
        using var folder = new Folder();
        string source = Path.Combine(folder.Root, file);
        File.Copy(Path.Combine(SourceDirectory(), "Fixtures", "BatteryAlerts", file), source);
        string imported = await BatteryAlertAudio.ImportAsync(source, Path.Combine(folder.Root, "sounds"), CancellationToken.None);
        string destination = BatteryAlertAudio.ResolveSound(Path.Combine(folder.Root, "sounds"), imported);
        Assert.IsTrue(File.Exists(destination));
        Assert.AreEqual(imported, await BatteryAlertAudio.ImportAsync(source, Path.Combine(folder.Root, "sounds"), CancellationToken.None));
        File.Delete(source);
        using var decoded = new WaveFileReader(destination);
        Assert.AreEqual(48000, decoded.WaveFormat.SampleRate);
        Assert.AreEqual(1, decoded.WaveFormat.Channels);
        Assert.AreEqual(WaveFormatEncoding.IeeeFloat, decoded.WaveFormat.Encoding);
        Assert.IsTrue(decoded.TotalTime.TotalSeconds > .05);
        Assert.IsTrue(decoded.TotalTime.TotalSeconds < 1);
    }

    [TestMethod]
    public async Task RejectsInvalidSoundsAndExternalStoredPaths()
    {
        using var folder = new Folder();
        string fake = Path.Combine(folder.Root, "broken.mp3"); File.WriteAllText(fake, "not an audio file");
        bool rejected = false;
        try { await BatteryAlertAudio.ImportAsync(fake, Path.Combine(folder.Root, "sounds"), CancellationToken.None); }
        catch { rejected = true; }
        Assert.IsTrue(rejected);
        foreach (string name in new[] { "../escape.wav", @"C:\\external.wav", "missing.wav" })
            Assert.Throws<InvalidDataException>(() => BatteryAlertAudio.ResolveSound(folder.Root, name));
        Assert.Throws<InvalidDataException>(() => BatteryAlertAudio.Decode(new Samples(float.NaN), CancellationToken.None));
    }

    [TestMethod]
    public void ControllerPlaybackKeepsHapticChannelsSilent()
    {
        var audio = new BatteryAlertChannelProvider(new Samples(.5f, -.5f), 4, true);
        var output = new float[8];
        Assert.AreEqual(8, audio.Read(output, 0, 8));
        CollectionAssert.AreEqual(new float[] { 0, .5f, 0, 0, 0, -.5f, 0, 0 }, output);
        audio = new BatteryAlertChannelProvider(new Samples(.5f), 2, false);
        Assert.AreEqual(2, audio.Read(output, 2, 2));
        Assert.AreEqual(.5f, output[2]); Assert.AreEqual(.5f, output[3]);
    }

    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
    private sealed class Samples(params float[] samples) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        public int Read(float[] buffer, int offset, int count)
        { int read = Math.Min(count, samples.Length - position); Array.Copy(samples, position, buffer, offset, read); position += read; return read; }
    }
    private sealed class Device : DS4Device, IDisposable
    {
        internal Device() : base("Battery alerts test", InputDeviceType.DS4, ConnectionType.USB) { Synced = true; FeatureSet = 0; }
        internal bool Preview => TryGetRumblePreview(out _, out _, out _, out _);
        internal DS4ForceFeedbackState Rumble => GetLatestRumbleState();
        internal void Reading(int percentage, bool charge) { battery = percentage; charging = charge; batteryReadingAvailable = true; }
        public void Dispose() => ReadWaitEv.Dispose();
    }
    private sealed class Folder : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ds4w-battery-" + Guid.NewGuid().ToString("N"));
        internal Folder() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Unexpected temporary directory.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
