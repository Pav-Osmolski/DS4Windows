using DS4Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DS4WindowsTests;

[TestClass]
public class DualSenseSonarCaptureParityTests
{
    [TestMethod]
    [DataRow("SteelSeries Sonar - Gaming", "", @"{1}.ROOT\MEDIA\0000", true)]
    [DataRow("SteelSeries Sonar - Chat", @"ROOT\MEDIA\0000", "", true)]
    [DataRow("SteelSeries Sonar - Media", "", @"USB\VID_1038&PID_12E0", false)]
    [DataRow("SteelSeries Sonar - Gaming", @"SWD\MMDEVAPI\{sonar}", "", false)]
    [DataRow("DualSense Wireless Controller", @"USB\VID_054C&PID_0CE6", "", false)]
    [DataRow("DualShock 4 Controller", @"USB\VID_054C&PID_05C4", "", false)]
    [DataRow("VB-Audio Virtual Cable", @"ROOT\MEDIA\0000", "", false)]
    public void SharedFactoryUsesSonarPolicyAndPreservesCallersStandardCapture(
        string name, string instance, string controller, bool expectPolling)
    {
        var backend = DualShock4EndpointCapturePolicy.SelectBackend(
            name, instance, controller, "");
        foreach (string standardPath in new[] { "USB standard loopback", "Bluetooth event loopback" })
        {
            using var standard = new Capture(standardPath);
            using var polling = new Capture("Sonar polling");
            int standardCalls = 0, pollingCalls = 0;

            IWaveIn selected = ControllerEndpointLoopbackCaptureFactory.Create(
                backend,
                () => { standardCalls++; return standard; },
                () => { pollingCalls++; return polling; });

            Assert.AreSame(expectPolling ? polling : standard, selected);
            Assert.AreEqual(expectPolling ? 0 : 1, standardCalls);
            Assert.AreEqual(expectPolling ? 1 : 0, pollingCalls);
            Assert.AreEqual(0, standard.Starts + polling.Starts,
                "Choosing a backend must not start either capture client.");
            Assert.AreEqual(0, standard.Disposals + polling.Disposals,
                "The selected client belongs to the existing playback lifecycle.");
        }
    }

    [TestMethod]
    public void UnknownBackendRetainsStandardCaptureWithoutOpeningSonar()
    {
        using var standard = new Capture("standard");
        IWaveIn selected = ControllerEndpointLoopbackCaptureFactory.Create(
            (DualShock4EndpointCaptureBackend)999, () => standard,
            () => throw new AssertFailedException("Unexpected Sonar client"));
        Assert.AreSame(standard, selected);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedBackendFailureDoesNotSilentlyChangeAudioRoute(bool sonar)
    {
        var failure = new InvalidOperationException("capture initialization failed");
        int unselectedCalls = 0;
        Func<IWaveIn> selected = () => throw failure;
        Func<IWaveIn> unselected = () => { unselectedCalls++; return new Capture("unselected"); };
        Exception actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
            ControllerEndpointLoopbackCaptureFactory.Create(
                sonar ? DualShock4EndpointCaptureBackend.SoftwareRouterPollingLoopback
                    : DualShock4EndpointCaptureBackend.StandardLoopback,
                sonar ? unselected : selected, sonar ? selected : unselected));
        Assert.AreSame(failure, actual);
        Assert.AreEqual(0, unselectedCalls);
    }

    [TestMethod]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "MSTEST0032", Justification = "Intentional protocol/default contract assertion; changing the pinned constant must fail this test.")]
    public void SharedSonarBackendRetainsShortPollingAndFloatSampleInterpretation()
    {
        Assert.AreEqual(4, DualShock4SoftwareRouterLoopbackCapture.RequestedBufferMilliseconds);
        AudioClientStreamFlags flags = DualShock4SoftwareRouterLoopbackCapture.CaptureStreamFlags;
        Assert.AreEqual(AudioClientStreamFlags.Loopback |
            AudioClientStreamFlags.AutoConvertPcm |
            AudioClientStreamFlags.SrcDefaultQuality, flags);
        Assert.AreEqual((AudioClientStreamFlags)0, flags & AudioClientStreamFlags.EventCallback);

        WaveFormat normalized = DualShock4EndpointCapturePolicy.NormalizeCaptureWaveFormat(
            new WaveFormatExtensible(48000, 32, 2));
        Assert.AreEqual(WaveFormatEncoding.IeeeFloat, normalized.Encoding);
        Assert.AreEqual(48000, normalized.SampleRate);
        Assert.AreEqual(2, normalized.Channels);
        Assert.AreEqual(8, normalized.BlockAlign);
        var provider = new BufferedWaveProvider(normalized) { ReadFully = false };
        float[] expected = { 0.25f, -0.5f, 0.75f, -0.125f };
        byte[] bytes = new byte[expected.Length * sizeof(float)];
        Buffer.BlockCopy(expected, 0, bytes, 0, bytes.Length);
        provider.AddSamples(bytes, 0, bytes.Length);
        float[] actual = new float[expected.Length];
        Assert.AreEqual(actual.Length, provider.ToSampleProvider().Read(actual, 0, actual.Length));
        CollectionAssert.AreEqual(expected, actual);
    }

    private sealed class Capture(string name) : IWaveIn
    {
        public string Name { get; } = name;
        public int Starts { get; private set; }
        public int Disposals { get; private set; }
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public event EventHandler<WaveInEventArgs> DataAvailable { add { } remove { } }
        public event EventHandler<StoppedEventArgs> RecordingStopped { add { } remove { } }
        public void StartRecording() => Starts++;
        public void StopRecording() { }
        public void Dispose() => Disposals++;
    }
}
