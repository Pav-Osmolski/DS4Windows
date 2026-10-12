using System.Reflection;
using DS4Windows;
using NAudio.Wave;

namespace DS4WindowsTests;

[TestClass]
public class DualSenseUsbSpeakerSampleRateTests
{
    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 2)]
    public void NativeFortyEightFloatIsImmediateAndBitTransparent(bool extensible, int channels)
    {
        WaveFormat source = extensible ? new WaveFormatExtensible(48000, 32, channels) :
            WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
        var playback = new Playback(new WaveFormatExtensible(48000, 32, 4));
        float[] values = { 0.0f, 0.0000012345f, -0.0000023456f, 0.5f, -0.875f };
        foreach (float value in values)
        {
            byte[] input = new byte[source.BlockAlign];
            for (int channel = 0; channel < channels; channel++)
                BitConverter.TryWriteBytes(input.AsSpan(channel * sizeof(float)), value);
            playback.Write(input, 1, source);
            byte[] actual = playback.Drain();
            Assert.AreEqual(4 * sizeof(float), actual.Length,
                "One native-rate frame must be available without waiting for a processing block.");
            Assert.AreEqual(BitConverter.SingleToInt32Bits(value), BitConverter.ToInt32(actual, sizeof(float)),
                "Quiet native float must not pass through PCM16 or a 32 kHz intermediary.");
            AssertSilentOtherChannels(actual, 4, 1);
        }
        Assert.IsNull(playback.Field("resampler"));
    }

    [TestMethod]
    [DataRow(48000)]
    [DataRow(32000)]
    [DataRow(44100)]
    [DataRow(96000)]
    [DataRow(192000)]
    public void LargeCallbacksAreNotTruncatedAndRemainAtThePhysicalRate(int sourceRate)
    {
        const int frames = 12001;
        var source = WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, 2);
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        playback.Write(CreateFloat(frames, sourceRate), frames, source);
        byte[] actual = playback.Drain();
        int outputFrames = actual.Length / 16;
        int expected = (int)((long)frames * 48000 / sourceRate);
        Assert.IsTrue(outputFrames >= expected - 2 && outputFrames <= expected,
            $"{sourceRate} Hz supplied {frames} frames but produced {outputFrames}, expected about {expected}.");
        Assert.AreEqual(sourceRate, playback.Field("sourceSampleRate"));
        AssertSilentOtherChannels(actual, 4, 1);
    }

    [TestMethod]
    [DataRow(32000)]
    [DataRow(44100)]
    [DataRow(96000)]
    [DataRow(192000)]
    public void PartialCallbacksAreProcessedImmediatelyWithoutStagingAFullBlock(int sourceRate)
    {
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var source = WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, 2);
        playback.Write(CreateFloat(17, sourceRate), 17, source);
        int outputFrames = playback.Drain().Length / 16;
        Assert.IsTrue(outputFrames > 0, "An incomplete 256-frame work block was unnecessarily staged.");
        Assert.IsTrue(outputFrames <= 17L * 48000 / sourceRate,
            "Resampling must not advance beyond the source time actually received.");
    }

    [TestMethod]
    [DataRow(32000)]
    [DataRow(44100)]
    [DataRow(96000)]
    [DataRow(192000)]
    public void StreamPhaseAndFrameCountDoNotDependOnCaptureCallbackBoundaries(int sourceRate)
    {
        int frames = sourceRate * 3 + 137;
        byte[] input = CreateFloat(frames, sourceRate);
        float[] whole = Render(input, sourceRate, new[] { frames });
        float[] split = Render(input, sourceRate, new[] { 1, 7, 511, 2, 8093, 17, 257 });
        Assert.AreEqual(whole.Length, split.Length, "Capture chunking changed stream duration.");
        long expected = (long)frames * 48000 / sourceRate;
        Assert.IsTrue(whole.Length >= expected - 2 && whole.Length <= expected,
            $"Long-run frame count {whole.Length} diverged from source duration {expected}.");
        for (int i = 0; i < whole.Length; i++)
            Assert.AreEqual(whole[i], split[i], 0.00002f,
                $"Sample {i} changed when the same source was split between callbacks.");
    }

    [TestMethod]
    public void NinetySixToFortyEightRetainsContentAboveTheVirtualDs4NyquistLimit()
    {
        const int sourceRate = 96000;
        float[] actual = Render(CreateFloat(sourceRate, sourceRate, 18000), sourceRate,
            new[] { 83, 509, 13 });
        double intended = Projection(actual, 18000);
        double aliasFromThirtyTwo = Projection(actual, 14000);
        Assert.IsTrue(intended > 0.01, "18 kHz content was removed by an unnecessary 32 kHz conversion.");
        Assert.IsTrue(intended > aliasFromThirtyTwo * 4,
            "Output resembles a 32 kHz intermediate's folded spectrum, not direct 96-to-48 conversion.");
    }

    [TestMethod]
    public void EquivalentFormatObjectsDoNotResetAResampledStream()
    {
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        playback.Write(CreateFloat(13, 32000), 13, WaveFormat.CreateIeeeFloatWaveFormat(32000, 2));
        object filter = playback.Field("resampler");
        playback.Write(CreateFloat(7, 32000), 7, WaveFormat.CreateIeeeFloatWaveFormat(32000, 2));
        Assert.AreSame(filter, playback.Field("resampler"));
        Assert.AreEqual(20L, playback.Field("sourceFramesReceived"));
    }

    [TestMethod]
    public void ReturningToNativeRateDiscardsOnlyThePreviousConverterState()
    {
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        playback.Write(CreateFloat(13, 32000), 13, WaveFormat.CreateIeeeFloatWaveFormat(32000, 2));
        playback.Drain();
        byte[] native = CreateFloat(7, 48000);
        playback.Write(native, 7, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        byte[] actual = playback.Drain();
        Assert.AreEqual(7 * 16, actual.Length);
        Assert.IsNull(playback.Field("resampler"));
        for (int i = 0; i < 7; i++)
            Assert.AreEqual(BitConverter.ToInt32(native, i * 8), BitConverter.ToInt32(actual, i * 16 + 4));
    }

    [TestMethod]
    public void InvalidFrameCountCannotReadPastCaptureOrInventAudio()
    {
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var source = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => playback.Write(new byte[8], 2, source));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => playback.Write(new byte[8], -1, source));
        Assert.AreEqual(0, playback.Drain().Length);
    }

    [TestMethod]
    public void ZeroSampleRateCannotMatchTheUninitializedConverterCache()
    {
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            playback.Write(new byte[4], 1, new WaveFormat(0, 16, 2)));
        Assert.AreEqual(0, playback.Drain().Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ParentCaptureStopClearsEverySlotEvenWhenCaptureIsAlreadyAbsent(bool capturePresent)
    {
        var first = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var second = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        byte[] input = CreateFloat(257, 44100);
        first.Write(input, 257, format);
        second.Write(input, 257, format);
        var parent = new CaptureOwner(first, second);
        if (capturePresent)
            parent.Install(new Capture(format));
        parent.Stop();

        foreach (Playback playback in new[] { first, second })
        {
            Assert.AreEqual(0, playback.Drain().Length, "Retired-source samples remained queued for playback.");
            Assert.IsNull(playback.Field("resampler"));
            Assert.IsNull(playback.Field("lastCaptureFormat"));
            Assert.AreEqual(0, playback.Field("sourceSampleRate"));
            Assert.AreEqual(0L, playback.Field("sourceFramesReceived"));
            Assert.AreEqual(0L, playback.Field("outputFramesProduced"));
        }
    }

    [TestMethod]
    [DataRow(32000)]
    [DataRow(44100)]
    public void SameRateSourceReplacementDoesNotRetainOldFilterHistory(int sampleRate)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        var reused = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var fresh = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var parent = new CaptureOwner(reused);
        var oldCapture = new Capture(format);
        parent.Install(oldCapture);
        parent.Deliver(oldCapture, ConstantFloat(1031, -0.75f));
        parent.Stop();
        var replacement = new Capture(format);
        parent.Install(replacement);
        byte[] positive = ConstantFloat(1031, 0.25f);
        parent.Deliver(replacement, positive);
        fresh.Write(positive, 1031, format);
        CollectionAssert.AreEqual(fresh.Drain(), reused.Drain(),
            "A new source inherited buffered audio, fractional phase, or negative filter history from its predecessor.");
        parent.Stop();
    }

    [TestMethod]
    public void LateOldCaptureCallbackCannotUseTheReplacementFormatOrFillItsQueue()
    {
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var parent = new CaptureOwner(playback);
        var oldCapture = new Capture(new WaveFormat(32000, 16, 2));
        parent.Install(oldCapture);
        parent.Stop();
        var replacement = new Capture(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        parent.Install(replacement);
        parent.Deliver(oldCapture, ConstantFloat(41, -0.5f));
        Assert.AreEqual(0, playback.Drain().Length, "A callback from the retired capture was accepted.");
        Assert.AreEqual(0, playback.Field("sourceSampleRate"));
        parent.Deliver(replacement, ConstantFloat(41, 0.25f));
        byte[] actual = playback.Drain();
        Assert.AreEqual(41 * 16, actual.Length);
        Assert.AreEqual(0.25f, BitConverter.ToSingle(actual, 4));
        parent.Stop();
    }

    [TestMethod]
    public void RetiredCaptureCanJoinItsWaitingCallbackWithoutHoldingTheManagerLock()
    {
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var parent = new CaptureOwner(playback);
        var sourceFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var oldCapture = new Capture(sourceFormat);
        var replacement = new Capture(sourceFormat);
        using var callbackEntered = new ManualResetEventSlim();
        using var disposed = new ManualResetEventSlim();
        bool callbackJoined = false;
        Exception callbackFailure = null;
        var callback = new Thread(() =>
        {
            callbackEntered.Set();
            try { parent.Deliver(oldCapture, ConstantFloat(11, -0.5f)); }
            catch (Exception ex) { callbackFailure = ex; }
        }) { IsBackground = true };
        oldCapture.OnDispose = () =>
        {
            callbackJoined = callback.Join(2000);
            disposed.Set();
        };
        parent.Install(oldCapture);
        lock (parent.Gate)
        {
            callback.Start();
            Assert.IsTrue(callbackEntered.Wait(2000));
            parent.Stop();
            parent.Install(replacement);
        }
        Assert.IsTrue(disposed.Wait(3000), "The retired capture's cleanup did not complete.");
        Assert.IsTrue(callbackJoined, "Cleanup joined a callback while the manager lock was still held.");
        Assert.IsNull(callbackFailure);
        Assert.AreEqual(0, playback.Drain().Length, "A blocked old callback entered the replacement stream.");
        parent.Deliver(replacement, ConstantFloat(11, 0.25f));
        Assert.AreEqual(11 * 16, playback.Drain().Length, "Retiring the old source disrupted current-source callbacks.");
        parent.Stop();
    }

    [TestMethod]
    public void CaptureStopFailureDoesNotPreventDeferredDisposal()
    {
        var parent = new CaptureOwner();
        using var disposed = new ManualResetEventSlim();
        var source = new Capture(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
        {
            OnStop = () => throw new InvalidOperationException("Synthetic capture-stop failure"),
            OnDispose = () => disposed.Set()
        };
        parent.Install(source);
        parent.Stop();
        Assert.IsTrue(disposed.Wait(3000), "Dispose must still run when StopRecording fails.");
    }

    [TestMethod]
    public void ActualDs4Pcm16SourceConvertsDirectlyTo48WithExactFloatNormalization()
    {
        const int frames = 32000 + 137;
        byte[] pcm = new byte[frames * 4];
        byte[] normalized = new byte[frames * 8];
        for (int frame = 0; frame < frames; frame++)
        {
            short value = (short)(Math.Sin(frame * 0.031) * 23000);
            for (int channel = 0; channel < 2; channel++)
            {
                BitConverter.TryWriteBytes(pcm.AsSpan(frame * 4 + channel * 2), value);
                BitConverter.TryWriteBytes(normalized.AsSpan(frame * 8 + channel * 4), value / 32768.0f);
            }
        }
        var actual = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var reference = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        actual.Write(pcm, frames, new WaveFormat(32000, 16, 2));
        reference.Write(normalized, frames, WaveFormat.CreateIeeeFloatWaveFormat(32000, 2));
        byte[] converted = actual.Drain();
        CollectionAssert.AreEqual(reference.Drain(), converted,
            "The virtual DS4 PCM16 source was reinterpreted or quantized again before conversion.");
        Assert.IsTrue(Math.Abs(converted.Length / 16 - frames * 3 / 2) <= 2);
    }

    private static byte[] ConstantFloat(int frames, float value)
    {
        byte[] result = new byte[frames * 8];
        for (int sample = 0; sample < frames * 2; sample++)
            BitConverter.TryWriteBytes(result.AsSpan(sample * 4), value);
        return result;
    }

    private static float[] Render(byte[] input, int sampleRate, int[] chunks)
    {
        var playback = new Playback(WaveFormat.CreateIeeeFloatWaveFormat(48000, 4));
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        var result = new List<float>();
        int offset = 0, chunk = 0;
        while (offset < input.Length)
        {
            int frames = Math.Min(chunks[chunk++ % chunks.Length], (input.Length - offset) / 8);
            byte[] part = input.AsSpan(offset, frames * 8).ToArray();
            playback.Write(part, frames, format);
            byte[] output = playback.Drain();
            for (int i = 4; i < output.Length; i += 16)
                result.Add(BitConverter.ToSingle(output, i));
            offset += frames * 8;
        }
        return result.ToArray();
    }

    private static byte[] CreateFloat(int frames, int sampleRate, double frequency = 997)
    {
        byte[] result = new byte[frames * 8];
        for (int i = 0; i < frames; i++)
        {
            float value = (float)(0.4 * Math.Sin(i * 2 * Math.PI * frequency / sampleRate));
            BitConverter.TryWriteBytes(result.AsSpan(i * 8), value);
            BitConverter.TryWriteBytes(result.AsSpan(i * 8 + 4), value);
        }
        return result;
    }

    private static double Projection(float[] samples, double frequency)
    {
        double real = 0, imaginary = 0;
        const int start = 4800;
        for (int i = start; i < samples.Length; i++)
        {
            double phase = i * 2 * Math.PI * frequency / 48000;
            real += samples[i] * Math.Cos(phase);
            imaginary += samples[i] * Math.Sin(phase);
        }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / (samples.Length - start);
    }

    private static void AssertSilentOtherChannels(byte[] samples, int channels, int speaker)
    {
        for (int offset = 0; offset < samples.Length; offset += channels * sizeof(float))
            for (int channel = 0; channel < channels; channel++)
                if (channel != speaker)
                    Assert.AreEqual(0, BitConverter.ToInt32(samples, offset + channel * sizeof(float)),
                        "Speaker passthrough must not populate the haptic actuator channels.");
    }

    // Actual production SlotPlayback and BufferedWaveProvider, but no MMDevice,
    // WasapiOut instance, native audio client, or controller is created by this fixture.
    private sealed class Playback
    {
        private static readonly Type Type = typeof(DualSenseAudioPassthrough)
            .GetNestedType("SlotPlayback", BindingFlags.NonPublic)!;
        private readonly object instance;
        private readonly BufferedWaveProvider provider;
        private readonly Action<byte[], int, WaveFormat> write;

        public Playback(WaveFormat format)
        {
            provider = new BufferedWaveProvider(format)
            {
                BufferLength = format.AverageBytesPerSecond * 8,
                ReadFully = false,
                DiscardOnBufferOverflow = false
            };
            instance = Activator.CreateInstance(Type, BindingFlags.Instance | BindingFlags.Public,
                null, new object[] { "memory-only-speaker", null!, provider, format, (byte)255 }, null)!;
            write = Type.GetMethod("WriteFromCapture")!
                .CreateDelegate<Action<byte[], int, WaveFormat>>(instance);
        }

        public void Write(byte[] buffer, int frames, WaveFormat format) => write(buffer, frames, format);
        public object Instance => instance;
        public object Field(string name) => Type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance);
        public byte[] Drain()
        {
            byte[] result = new byte[provider.BufferedBytes];
            Assert.AreEqual(result.Length, provider.Read(result, 0, result.Length));
            return result;
        }
    }

    private sealed class CaptureOwner
    {
        private readonly DualSenseAudioPassthrough owner = new();
        private readonly Action stop;
        private readonly Action<object, WaveInEventArgs> deliver;
        private readonly object gate;

        public CaptureOwner(params Playback[] playbacks)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            gate = Field("syncRoot").GetValue(owner)!;
            var slots = (Array)Field("slots").GetValue(owner)!;
            for (int i = 0; i < playbacks.Length; i++)
                slots.SetValue(playbacks[i].Instance, i);
            stop = typeof(DualSenseAudioPassthrough).GetMethod("StopCapture", flags)!
                .CreateDelegate<Action>(owner);
            deliver = typeof(DualSenseAudioPassthrough).GetMethod("Capture_DataAvailable", flags)!
                .CreateDelegate<Action<object, WaveInEventArgs>>(owner);
        }

        public void Install(Capture capture)
        {
            lock (gate)
            {
                Field("capture").SetValue(owner, capture);
                Field("captureFormat").SetValue(owner, capture.WaveFormat);
            }
        }
        public void Stop() { lock (gate) stop(); }
        public object Gate => gate;
        public void Deliver(Capture source, byte[] bytes) => deliver(source, new WaveInEventArgs(bytes, bytes.Length));
        private static FieldInfo Field(string name) => typeof(DualSenseAudioPassthrough)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    private sealed class Capture : IWaveIn
    {
        public Capture(WaveFormat format) => WaveFormat = format;
        public Action OnStop { get; set; }
        public Action OnDispose { get; set; }
        public WaveFormat WaveFormat { get; set; }
        public event EventHandler<WaveInEventArgs> DataAvailable { add { } remove { } }
        public event EventHandler<StoppedEventArgs> RecordingStopped { add { } remove { } }
        public void StartRecording() { }
        public void StopRecording() => OnStop?.Invoke();
        public void Dispose() => OnDispose?.Invoke();
    }
}
