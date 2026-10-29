using DS4Windows;
using DS4Windows.InputDevices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.CompilerServices;

namespace DS4WindowsTests;

[TestClass]
public class UsbControllerMicrophoneTests
{
    [TestMethod]
    public void FragmentedFramesKeepOrderAndResetDiscardsPartialPacket()
    {
        var packetizer = new UsbMicrophonePacketizer();
        var packets = new List<short[]>();
        void Submit(short[] pcm) => packets.Add((short[])pcm.Clone());
        packetizer.Push(Enumerable.Repeat(.25f, 239).ToArray(), 239, Submit);
        Assert.AreEqual(0, packets.Count);
        packetizer.Push(Enumerable.Repeat(.5f, 241).ToArray(), 241, Submit);
        Assert.AreEqual(1, packets.Count);
        Assert.AreEqual((short)8192, packets[0][238]);
        Assert.AreEqual((short)16384, packets[0][239]);
        packetizer.Push(Enumerable.Repeat(1f, 100).ToArray(), 100, Submit);
        packetizer.Reset();
        packetizer.Push(new float[480], 480, Submit);
        Assert.IsTrue(packets[1].All(v => v == 0));
    }

    [TestMethod]
    public void PacketizerClipsPcmAndRejectsNonFiniteSamples()
    {
        var input = new float[480];
        input[0] = 2; input[1] = -2; input[2] = float.NaN; input[3] = float.PositiveInfinity;
        new UsbMicrophonePacketizer().Push(input, input.Length, pcm =>
        {
            Assert.AreEqual(short.MaxValue, pcm[0]);
            Assert.AreEqual(short.MinValue, pcm[1]);
            Assert.AreEqual((short)0, pcm[2]); Assert.AreEqual((short)0, pcm[3]);
        });
    }

    [TestMethod]
    [DataRow(48000, 1)]
    [DataRow(48000, 2)]
    [DataRow(44100, 1)]
    [DataRow(44100, 2)]
    [DataRow(16000, 1)]
    public void PcmFormatsBecome48kMonoPacketsWithoutChannelDuplication(int rate, int channels)
    {
        var provider = new BufferedWaveProvider(new WaveFormat(rate, 16, channels)) { ReadFully = false };
        int frames = rate / 10;
        byte[] input = new byte[frames * channels * 2];
        for (int i = 0; i < frames; i++)
        for (int ch = 0; ch < channels; ch++)
        {
            short value = (short)(ch == 0 ? 8192 : 24576);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan((i * channels + ch) * 2), value);
        }
        provider.AddSamples(input, 0, input.Length);
        var mono = UsbControllerMicrophoneBridge.CreateMono48k(provider.ToSampleProvider());
        var pcm = new UsbMicrophonePacketizer();
        int packetCount = 0;
        var buffer = new float[480];
        int read;
        while ((read = mono.Read(buffer, 0, buffer.Length)) > 0)
            pcm.Push(buffer, read, packet =>
            {
                packetCount++;
                short expected = (short)(channels == 1 ? 8192 : 16384);
                // Resampling filter startup/end transients do not represent gain.
                if (packetCount == 5) Assert.IsTrue(Math.Abs(packet[240] - expected) < 64);
            });
        Assert.IsTrue(packetCount >= 9 && packetCount <= 11, $"Packets: {packetCount}");
    }

    [TestMethod]
    public void Streaming44100CaptureCanResumeAfterEachPartialBuffer()
    {
        var buffered = new BufferedWaveProvider(new WaveFormat(44100, 16, 1)) { ReadFully = false };
        var converted = UsbControllerMicrophoneBridge.CreateMono48k(buffered.ToSampleProvider());
        float[] output = new float[480];
        byte[] chunk = new byte[882 * 2]; // Twenty milliseconds of actual capture.
        long samples = 0;
        for (int i = 0; i < 50; i++)
        {
            buffered.AddSamples(chunk, 0, chunk.Length);
            int read;
            while ((read = converted.Read(output, 0, output.Length)) > 0) samples += read;
        }
        Assert.IsTrue(Math.Abs(samples - 48000) < 480, $"One second became {samples} samples");
    }

    [TestMethod]
    [DataRow(InputDeviceType.DS4, 0x05C4)]
    [DataRow(InputDeviceType.DS4, 0x09CC)]
    [DataRow(InputDeviceType.DualSense, 0x0CE6)]
    [DataRow(InputDeviceType.DualSense, 0x0DF2)]
    public void UsbSonySourcesCanUseEveryAudioCapableVirtualPersona(InputDeviceType type, int pid)
    {
        var source = new TestDevice(type, ConnectionType.USB, 0x054C, pid);
        Assert.IsTrue(ControllerMicrophoneRoutePolicy.IsEligibleUsbSource(source));
        Assert.IsFalse(ControllerMicrophoneRoutePolicy.IsEligibleBluetoothSource(source));
        foreach (var output in new[] { OutContType.ViiperDS4, OutContType.ViiperDualSense,
            OutContType.ViiperDualSenseEdge, OutContType.ViiperX360, OutContType.ViiperXboxOne, OutContType.ViiperSwitch2Pro })
        {
            Assert.IsTrue(ControllerMicrophoneRoutePolicy.CanRouteDirectViiperMicrophone(true, true, output, true));
            if (PlayStationFeatureOutputPolicy.NeedsAudioOnlySidecar(output))
            {
                Assert.AreEqual(OutContType.None, PlayStationFeatureOutputPolicy.GetAudioOnlySidecarType(
                    type, ConnectionType.USB, 0x054C, pid, output, false));
                Assert.AreNotEqual(OutContType.None, PlayStationFeatureOutputPolicy.GetAudioOnlySidecarType(
                    type, ConnectionType.USB, 0x054C, pid, output, false, allowUsbMicrophone: true));
            }
        }
    }

    [TestMethod]
    [DataRow(InputDeviceType.Switch2Pro, 0x057E, 0x2069)]
    [DataRow(InputDeviceType.SwitchPro, 0x057E, 0x2009)]
    [DataRow(InputDeviceType.DS3, 0x054C, 0x0268)]
    [DataRow(InputDeviceType.DualSense, 0x1234, 0x0CE6)]
    public void OtherHeadsetPathsAndUnrecognizedDevicesAreNotHijacked(InputDeviceType type, int vid, int pid)
    {
        var source = new TestDevice(type, ConnectionType.USB, vid, pid);
        Assert.IsFalse(ControllerMicrophoneRoutePolicy.IsEligibleUsbSource(source));
        Assert.AreEqual(OutContType.None, PlayStationFeatureOutputPolicy.GetAudioOnlySidecarType(
            type, ConnectionType.USB, vid, pid, OutContType.ViiperX360, false, allowUsbMicrophone: true));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(OutContType.ViiperDS4, ViiperVirtualDeviceType.DualShock4, 320)]
    [DataRow(OutContType.ViiperDualSense, ViiperVirtualDeviceType.DualSense, 1920)]
    [DataRow(OutContType.ViiperDualSenseEdge, ViiperVirtualDeviceType.DualSenseEdge, 1920)]
    public void UsbPcmUsesProductionMicrophoneQueueAndRejectsRetiredSources(
        OutContType output, ViiperVirtualDeviceType type, int expectedBytes)
    {
        var previousHub = Program.rootHub;
        bool enabled = Global.DualSenseEnableMicrophonePassthrough[0];
        byte volume = Global.DualSenseMicrophoneVolume[0];
        byte suppression = Global.DualSenseMicrophoneNoiseSuppression[0];
        var owner = new ViiperOutDevice(output, type);
        var source = new TestDevice(InputDeviceType.DS4, ConnectionType.USB, 0x054C, 0x09CC);
        var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        hub.DS4Controllers = new DS4Device[ControlService.MAX_DS4_CONTROLLER_COUNT];
        hub.DS4Controllers[0] = source;
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(ViiperOutDevice).GetField(name, fields).SetValue(owner, value);
        T Get<T>(string name) => (T)typeof(ViiperOutDevice).GetField(name, fields).GetValue(owner);
        try
        {
            Program.rootHub = hub;
            Global.DualSenseEnableMicrophonePassthrough[0] = true;
            Global.DualSenseMicrophoneVolume[0] = 255;
            Global.DualSenseMicrophoneNoiseSuppression[0] = 0;
            Set("connected", true); Set("writerStopRequested", false);
            Set("lastInputDeviceIndex", 0); Set("activeStreamSupportsMicrophone", true);
            Set("virtualMicrophoneInterfaceActive", 1);
            short[] pcm = Enumerable.Range(0, 480).Select(i => (short)(Math.Sin(i * .1) * 8000)).ToArray();
            Assert.IsTrue(owner.TryQueueUsbMicrophonePcm(source, 0, owner.RuntimeFeedbackGeneration, owner.UsbMicrophoneRouteGeneration, pcm));
            Assert.AreEqual(1, Get<int>("preparedMicrophoneCount"));
            Assert.AreEqual(expectedBytes, Get<int[]>("preparedMicrophoneLengths")[0]);
            byte[] bytes = Get<byte[][]>("preparedMicrophoneFrames")[0];
            Assert.IsTrue(bytes.Take(expectedBytes).Any(b => b != 0));
            if (expectedBytes == 1920)
                for (int i = 0; i < expectedBytes; i += 4)
                { Assert.AreEqual(bytes[i], bytes[i + 2]); Assert.AreEqual(bytes[i + 1], bytes[i + 3]); }

            Assert.IsFalse(owner.TryQueueUsbMicrophonePcm(source, 0, owner.RuntimeFeedbackGeneration + 1, owner.UsbMicrophoneRouteGeneration, pcm));
            Assert.IsFalse(owner.TryQueueUsbMicrophonePcm(source, 0, owner.RuntimeFeedbackGeneration, owner.UsbMicrophoneRouteGeneration + 1, pcm));
            Global.DualSenseEnableMicrophonePassthrough[0] = false;
            Assert.IsFalse(owner.TryQueueUsbMicrophonePcm(source, 0, owner.RuntimeFeedbackGeneration, owner.UsbMicrophoneRouteGeneration, pcm));
            Global.DualSenseEnableMicrophonePassthrough[0] = true;
            Set("virtualMicrophoneInterfaceActive", 0);
            Assert.IsFalse(owner.TryQueueUsbMicrophonePcm(source, 0, owner.RuntimeFeedbackGeneration, owner.UsbMicrophoneRouteGeneration, pcm));
            Set("virtualMicrophoneInterfaceActive", 1);
            hub.DS4Controllers[0] = new TestDevice(InputDeviceType.DS4, ConnectionType.USB, 0x054C, 0x09CC);
            Assert.IsFalse(owner.TryQueueUsbMicrophonePcm(source, 0, owner.RuntimeFeedbackGeneration, owner.UsbMicrophoneRouteGeneration, pcm));
            owner.ClearUsbMicrophonePending(source, 0, owner.RuntimeFeedbackGeneration, owner.UsbMicrophoneRouteGeneration);
            Assert.AreEqual(1, Get<int>("preparedMicrophoneCount"), "Old source cleanup must not clear a successor's queue.");
            hub.DS4Controllers[0] = source;
            owner.ClearUsbMicrophonePending(source, 0, owner.RuntimeFeedbackGeneration, owner.UsbMicrophoneRouteGeneration);
            Assert.AreEqual(0, Get<int>("preparedMicrophoneCount"));
        }
        finally
        {
            Set("connected", false);
            Program.rootHub = previousHub;
            Global.DualSenseEnableMicrophonePassthrough[0] = enabled;
            Global.DualSenseMicrophoneVolume[0] = volume;
            Global.DualSenseMicrophoneNoiseSuppression[0] = suppression;
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void UsbMuteSilencesPackedPcmAndClearsOldPacketsOnUnmute()
    {
        var previousHub = Program.rootHub;
        bool enabled = Global.DualSenseEnableMicrophonePassthrough[0];
        byte volume = Global.DualSenseMicrophoneVolume[0];
        byte suppression = Global.DualSenseMicrophoneNoiseSuppression[0];
        var source = new TestDualSense();
        var owner = new ViiperOutDevice(OutContType.ViiperDualSense, ViiperVirtualDeviceType.DualSense);
        var hub = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        hub.DS4Controllers = new DS4Device[ControlService.MAX_DS4_CONTROLLER_COUNT];
        hub.DS4Controllers[0] = source;
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(ViiperOutDevice).GetField(name, fields).SetValue(owner, value);
        T Get<T>(string name) => (T)typeof(ViiperOutDevice).GetField(name, fields).GetValue(owner);
        try
        {
            Program.rootHub = hub;
            Global.DualSenseEnableMicrophonePassthrough[0] = true;
            Global.DualSenseMicrophoneVolume[0] = 255;
            Global.DualSenseMicrophoneNoiseSuppression[0] = 0;
            Set("connected", true); Set("writerStopRequested", false);
            Set("lastInputDeviceIndex", 0); Set("activeStreamSupportsMicrophone", true);
            Set("virtualMicrophoneInterfaceActive", 1);
            source.SetProfileMicrophoneMuteState(true, true);
            Assert.IsTrue(source.IsProfileMicrophoneMuted);
            Assert.IsTrue(owner.TryQueueUsbMicrophonePcm(source, 0, owner.RuntimeFeedbackGeneration,
                owner.UsbMicrophoneRouteGeneration, Enumerable.Repeat((short)8000, 480).ToArray()));
            Assert.IsTrue(Get<byte[][]>("preparedMicrophoneFrames")[0].All(b => b == 0));
            source.SetProfileMicrophoneMuteState(true, false);
            owner.UpdateUsbMicrophoneMute(source, 0, owner.RuntimeFeedbackGeneration, owner.UsbMicrophoneRouteGeneration);
            Assert.AreEqual(0, Get<int>("preparedMicrophoneCount"));
            Assert.IsTrue(owner.TryQueueUsbMicrophonePcm(source, 0, owner.RuntimeFeedbackGeneration,
                owner.UsbMicrophoneRouteGeneration, Enumerable.Range(0,480).Select(i => (short)(Math.Sin(i*.1)*8000)).ToArray()));
            Assert.IsTrue(Get<byte[][]>("preparedMicrophoneFrames")[0].Any(b => b != 0));
        }
        finally
        {
            Set("connected", false);
            Program.rootHub = previousHub;
            Global.DualSenseEnableMicrophonePassthrough[0] = enabled;
            Global.DualSenseMicrophoneVolume[0] = volume;
            Global.DualSenseMicrophoneNoiseSuppression[0] = suppression;
        }
    }

    private sealed class TestDualSense : DualSenseDevice
    {
        internal TestDualSense() : base(Hid(), "USB microphone mute test")
        { deviceType = InputDeviceType.DualSense; conType = ConnectionType.USB; }
        private static HidDevice Hid()
        {
            var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
            var attributes = new HidDeviceAttributes(new NativeMethods.HIDD_ATTRIBUTES { VendorID = 0x054C, ProductID = 0x0CE6 });
            typeof(HidDevice).GetField("_deviceAttributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(hid, attributes);
            return hid;
        }
        public override bool IsAlive() => true;
    }

    private sealed class TestDevice : DS4Device
    {
        internal TestDevice(InputDeviceType type, ConnectionType connection, int vid, int pid)
            : base("USB microphone policy test", type, connection)
        {
            hDevice = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
            var attributes = new HidDeviceAttributes(new NativeMethods.HIDD_ATTRIBUTES
                { VendorID = (ushort)vid, ProductID = (ushort)pid });
            typeof(HidDevice).GetField("_deviceAttributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(hDevice, attributes);
        }
        public override bool IsAlive() => true;
    }
}
