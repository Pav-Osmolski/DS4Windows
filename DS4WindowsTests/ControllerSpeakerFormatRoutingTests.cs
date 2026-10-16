using System.Reflection;
using System.Runtime.CompilerServices;
using Concentus;
using DS4Windows;
using DS4Windows.InputDevices;
using NAudio.Wave;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class ControllerSpeakerFormatRoutingTests
{
    // Exercise the real managed routing, PCM callbacks, and codec entry points.
    // No Start(), endpoint activation, HID handle, broker, or helper is used.
    [TestMethod]
    public void SelectedAppWithVirtualDs4UsesFloat48ThroughPhysicalDualSenseOpus()
    {
        object[] arguments = { ProcessLoopbackWaveCapture.BuildEndpointId(1),
            ControllerAudioEndpointKind.DualShock4, null };
        using var capture = (IWaveIn)Method(typeof(DualSenseBluetoothSpeakerPassthrough),
            "CreateCapture", true).Invoke(null, arguments);
        Assert.IsInstanceOfType<ProcessLoopbackWaveCapture>(capture,
            "Chrome and other explicit app selections must win over the virtual pad's endpoint kind.");
        Assert.AreEqual(48000, capture.WaveFormat.SampleRate);
        Assert.AreEqual(2, capture.WaveFormat.Channels);
        Assert.AreEqual(WaveFormatEncoding.IeeeFloat, capture.WaveFormat.Encoding);

        using var fixture = new DualSenseFixture(null);
        var bridge = fixture.Bridge;
        Assert.IsNull(Get<object>(bridge, "directPcmRateConverter"),
            "Selected application audio must not inherit the virtual DS4's 32 kHz PCM converter.");
        var buffer = new BufferedWaveProvider(capture.WaveFormat) { ReadFully = false };
        Set(bridge, "capture", capture);
        Set(bridge, "captureBuffer", buffer);
        const int frames = 1024;
        float[] input = StereoFloat(frames);
        byte[] bytes = new byte[input.Length * sizeof(float)];
        Buffer.BlockCopy(input, 0, bytes, 0, bytes.Length);
        Method(bridge.GetType(), "Capture_DataAvailable").Invoke(bridge,
            new object[] { capture, new WaveInEventArgs(bytes, bytes.Length) });
        ISampleProvider decoded = buffer.ToSampleProvider();
        ISampleProvider stereo = (ISampleProvider)Method(bridge.GetType(), "ToStereo", true)
            .Invoke(null, new object[] { decoded });
        Assert.AreSame(decoded, stereo, "Stereo 48 kHz app PCM needs no channel or source-rate conversion.");
        Assert.AreEqual(48000, stereo.WaveFormat.SampleRate);
        float[] samples = new float[input.Length];
        Assert.AreEqual(samples.Length, stereo.Read(samples, 0, samples.Length));
        CollectionAssert.AreEqual(input, samples, "Float app samples were quantized or reinterpreted before encoding.");
        Method(bridge.GetType(), "AppendCaptureSamples").Invoke(bridge,
            new object[] { samples, samples.Length });
        Assert.AreEqual((long)frames, Get<long>(bridge, "captureInputFrames"));
        Assert.AreEqual(0L, Get<long>(bridge, "directPcmInputFrames"));
        AssertDecodableDualSenseFrame(bridge);
    }

    [TestMethod]
    public void VirtualDs4Headset320FrameCallbacksConvertOnceTo48WithoutPhaseLoss()
    {
        const int callbackFrames = 320;
        const int callbackCount = 1000;
        using var fixture = new DualSenseFixture(CreateDs4Source());
        AssertDs4ToDualSenseSourceFormat(fixture);
        byte[] pcm = StereoPcm16(callbackFrames * callbackCount);
        var receive = DirectReceiver(fixture.Bridge);
        for (int callback = 0; callback < callbackCount; callback++)
            receive(fixture.Output, pcm, callback * callbackFrames * 4,
                callbackFrames * 4, callback + 1L);

        Assert.AreEqual((long)callbackFrames * callbackCount,
            Get<long>(fixture.Bridge, "directPcmInputFrames"));
        Assert.AreEqual((long)callbackFrames * callbackCount * 3 / 2 - 1,
            Get<long>(fixture.Bridge, "directPcmOutputFrames"),
            "Continuous 320-frame callbacks must retain fractional phase, not lose 64 staged frames per callback.");
        Assert.AreEqual(0L, Get<long>(fixture.Bridge, "captureOverflowFrames"),
            "This is normal source conversion, not overflow/discontinuity recovery.");
        Assert.AreEqual(callbackCount, Get<long>(fixture.Bridge, "directPcmCallbacks"));
        AssertDecodableDualSenseFrame(fixture.Bridge);
    }

    [TestMethod]
    public void VirtualDs4CallbacksAtEveryStagingBoundaryMatchOneContinuousConversion()
    {
        const int frames = 256 * 256;
        byte[] pcm = StereoPcm16(frames);
        using var fixture = new DualSenseFixture(CreateDs4Source());
        var receive = DirectReceiver(fixture.Bridge);
        int offsetFrames = 0;
        int chunkFrames = 1;
        long callback = 0;
        // Covers every 1..257-frame callback length, including both sides of
        // the converter's 256-frame staging boundary, repeatedly without reset.
        while (offsetFrames < frames)
        {
            int count = Math.Min(chunkFrames, frames - offsetFrames);
            receive(fixture.Output, pcm, offsetFrames * 4, count * 4, ++callback);
            offsetFrames += count;
            chunkFrames = chunkFrames == 257 ? 1 : chunkFrames + 1;
        }

        var reference = new DualSensePcm16SourceRateConverter(32000, 48000);
        float[] expected = new float[reference.GetMaximumOutputFrames(frames) * 2];
        int produced = reference.Convert(pcm, 0, pcm.Length, expected);
        Assert.AreEqual(frames * 3 / 2 - 1, produced);
        Assert.AreEqual((long)produced, Get<long>(fixture.Bridge, "directPcmOutputFrames"));
        Assert.AreEqual(produced, Get<int>(fixture.Bridge, "captureRingBufferedFrames"));
        Assert.AreEqual(0L, Get<long>(fixture.Bridge, "captureOverflowFrames"));
        float[] actual = Get<float[]>(fixture.Bridge, "captureRing");
        for (int sample = 0; sample < produced * 2; sample++)
            Assert.AreEqual(expected[sample], actual[sample], 0.0f,
                "The real callback route reset, duplicated, or converted PCM twice at sample " + sample);
        AssertDecodableDualSenseFrame(fixture.Bridge);
    }

    [TestMethod]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "MSTEST0032", Justification = "Intentional protocol/default contract assertion; changing the pinned constant must fail this test.")]
    public void PhysicalDs4Keeps32KhzVirtualSourceAndExisting16KhzBluetoothSbc()
    {
        ViiperOutDevice output = CreateDs4Source();
        Assert.AreEqual(32000, output.DirectSpeakerPcmSampleRate);
        Assert.AreEqual(32000, DualShock4BluetoothAudioProtocol.SpeakerSampleRate);
        var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
        var device = new DS4Device(hid, "DS4 source-format regression");
        var bridge = new DualShock4BluetoothSpeakerPassthrough(device, 255,
            DualSenseSpeakerCompression.Off, 0, string.Empty,
            ControllerAudioEndpointKind.DualShock4, output);
        try
        {
            Assert.AreEqual(32000, Get<int>(bridge, "directSpeakerSampleRate"));
            byte[] pcm = StereoPcm16(320);
            Method(bridge.GetType(), "DirectSpeakerPcmReceived").Invoke(bridge,
                new object[] { output, pcm, pcm.Length });
            Assert.AreEqual(160L, Get<long>(bridge, "directDriftInputFrames"),
                "The established physical DS4 Bluetooth route downsamples the 32 kHz headset source by two.");
            var encoded = Get<Queue<byte[]>>(bridge, "encodedFrames");
            Assert.IsTrue(encoded.Count > 0, "The real DS4 callback must produce an SBC frame.");
            foreach (byte[] frame in encoded)
            {
                Assert.AreEqual(109, frame.Length);
                Assert.AreEqual((byte)0x9C, frame[0]);
                Assert.AreEqual(0, frame[1] >> 6,
                    "The existing DS4 realtime transport intentionally uses 16 kHz SBC; DS5 format work must not change it.");
            }
            Assert.IsNull(Get<object>(bridge, "worker"));
            Assert.IsNull(Get<object>(bridge, "speakerWriteHandle"));
        }
        finally
        {
            // Start was never called. Dispose's production retirement path may
            // attempt a final physical disable, so release only these two
            // constructor-owned managed wait handles in this hardware-free test.
            Get<AutoResetEvent>(bridge, "captureAvailable").Dispose();
            Get<ManualResetEvent>(bridge, "stoppingSignal").Dispose();
        }
    }

    private static void AssertDs4ToDualSenseSourceFormat(DualSenseFixture fixture)
    {
        Assert.AreEqual(32000, fixture.Output.DirectSpeakerPcmSampleRate);
        Assert.AreEqual(32000, Get<int>(fixture.Bridge, "directSpeakerSampleRate"));
        var converter = Get<DualSensePcm16SourceRateConverter>(fixture.Bridge, "directPcmRateConverter");
        Assert.AreEqual(32000, converter.SourceSampleRate);
        Assert.AreEqual(48000, converter.OutputSampleRate);
        Assert.IsFalse(Get<bool>(fixture.Bridge, "directSpeakerUsesV5Source"));
        Assert.IsNull(Get<object>(fixture.Bridge, "directV5FrameResampler"));
        Assert.IsNotNull(Get<object>(fixture.Bridge, "directSpeakerFrameResampler"),
            "The following stage reconciles speaker clocks, not another 32→48 kHz conversion.");
        Assert.IsNull(Get<object>(fixture.Bridge, "capture"), "Direct DS4 headset PCM must not open loopback capture.");
    }

    private static void AssertDecodableDualSenseFrame(DualSenseBluetoothSpeakerPassthrough bridge)
    {
        object[] arguments = { 0 };
        Assert.IsTrue((bool)Method(bridge.GetType(), "TryFillOutputFrame").Invoke(bridge, arguments));
        Assert.IsTrue((int)arguments[0] > 0);
        Assert.AreEqual(480 * 2, Get<float[]>(bridge, "frame").Length);
        using var encoder = DualSenseBluetoothSpeakerPassthrough.CreateSpeakerOpusEncoder();
        Set(bridge, "opusEncoder", encoder);
        Assert.IsTrue((bool)Method(bridge.GetType(), "EncodeCurrentFrame").Invoke(bridge, null));
        byte[] packet = Get<byte[]>(bridge, "opusFrame");
        Assert.AreEqual(200, packet.Length);
        using var decoder = OpusCodecFactory.CreateDecoder(48000, 2);
        float[] decoded = new float[480 * 2];
        Assert.AreEqual(480, decoder.Decode(packet.AsSpan(), decoded.AsSpan(), 480, false));
        Assert.IsTrue(decoded.Any(value => Math.Abs(value) > 0.0001f),
            "The format route must encode the supplied audio, not a silent carrier.");
        Set(bridge, "opusEncoder", null);
    }

    private static Action<ViiperOutDevice, byte[], int, int, long> DirectReceiver(object bridge) =>
        (Action<ViiperOutDevice, byte[], int, int, long>)Method(bridge.GetType(),
            "ProcessDirectSpeakerPcmLocked").CreateDelegate(
                typeof(Action<ViiperOutDevice, byte[], int, int, long>), bridge);

    private static ViiperOutDevice CreateDs4Source()
    {
        // Only the real format getters and safe event removal are needed.
        // A disconnected fixture must not allocate/start a broker or USB pad.
        var output = (ViiperOutDevice)RuntimeHelpers.GetUninitializedObject(typeof(ViiperOutDevice));
        Set(output, "viiperType", ViiperVirtualDeviceType.DualShock4);
        Set(output, "connected", true);
        Set(output, "activeStreamSupportsDirectSpeaker", true);
        Set(output, "virtualSpeakerSubscriberLock", new object());
        return output;
    }

    private static float[] StereoFloat(int frames)
    {
        float[] samples = new float[frames * 2];
        for (int frame = 0; frame < frames; frame++)
        {
            samples[frame * 2] = (float)(0.3 * Math.Sin(frame * 0.071));
            samples[frame * 2 + 1] = (float)(0.2 * Math.Sin(frame * 0.117));
        }
        return samples;
    }

    private static byte[] StereoPcm16(int frames)
    {
        byte[] bytes = new byte[frames * 4];
        for (int sample = 0; sample < frames * 2; sample++)
        {
            short value = (short)(Math.Sin(sample * 0.071) * 12000);
            bytes[sample * 2] = (byte)value;
            bytes[sample * 2 + 1] = (byte)(value >> 8);
        }
        return bytes;
    }

    private sealed class DualSenseFixture : IDisposable
    {
        public ViiperOutDevice Output { get; }
        public DualSenseBluetoothSpeakerPassthrough Bridge { get; }
        public DualSenseFixture(ViiperOutDevice output)
        {
            Output = output;
            var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
            var device = new DualSenseDevice(hid, "Speaker format regression");
            const string traceVariable = "DS4WINDOWS_DUALSENSE_PCM_TRACE_DIRECTORY";
            string oldTrace = Environment.GetEnvironmentVariable(traceVariable);
            try
            {
                Environment.SetEnvironmentVariable(traceVariable, null);
                Bridge = new DualSenseBluetoothSpeakerPassthrough(device, 255,
                    DualSenseSpeakerCompression.Off, 0, string.Empty,
                    ControllerAudioEndpointKind.DualShock4, output);
            }
            finally { Environment.SetEnvironmentVariable(traceVariable, oldTrace); }
        }
        public void Dispose() => Bridge.Dispose();
    }

    private static MethodInfo Method(Type type, string name, bool isStatic = false) =>
        type.GetMethod(name, BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance)) ??
        throw new InvalidOperationException("Missing managed route method " + name);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static FieldInfo Field(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new InvalidOperationException("Missing managed route field " + name);
    }
}
