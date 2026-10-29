using System;
using System.Threading;
using DS4Windows.InputDevices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DS4Windows;

// A physical USB capture endpoint -> VIIPER microphone PCM route. It never
// opens a render endpoint or a loopback capture, so cannot mix in game audio.
internal sealed class UsbControllerMicrophoneBridge : IDisposable
{
    private readonly ViiperOutDevice owner;
    private readonly int slot;
    private readonly long generation;
    private readonly long routeGeneration;
    private readonly ManualResetEvent stop = new(false);
    private readonly object lifetime = new();
    private bool retired;
    private bool finished;
    private long muteRevision;
    private int status = (int)ControllerRuntimeLaneState.Starting;
    internal DS4Device Source { get; }
    internal ControllerRuntimeLaneState Status => (ControllerRuntimeLaneState)Volatile.Read(ref status);

    internal UsbControllerMicrophoneBridge(DS4Device source, int slot, ViiperOutDevice owner)
    {
        Source = source;
        this.slot = slot;
        this.owner = owner;
        generation = owner.RuntimeFeedbackGeneration;
        routeGeneration = owner.UsbMicrophoneRouteGeneration;
        if (source is DualSenseDevice dualSense)
            dualSense.ProfileMicrophoneMuteStateChanged += MuteChanged;
        var thread = new Thread(Run) { IsBackground = true, Name = "USB controller microphone" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    internal bool Matches(DS4Device source, int index) => !Volatile.Read(ref retired) &&
        !Volatile.Read(ref finished) && ReferenceEquals(Source, source) && slot == index &&
        generation == owner.RuntimeFeedbackGeneration;
    internal bool CanSubmit => Current() && owner.IsVirtualMicrophoneInterfaceActive;
    private bool Current() => !Volatile.Read(ref retired) &&
        owner.IsUsbMicrophoneSourceCurrent(Source, slot, generation, routeGeneration);

    private void MuteChanged(object sender, EventArgs e)
    {
        lock (lifetime)
        {
            Interlocked.Increment(ref muteRevision);
            if (!retired && Current()) owner.UpdateUsbMicrophoneMute(Source, slot, generation, routeGeneration);
        }
    }

    private void Run()
    {
        string lastError = null;
        long createdAt = Environment.TickCount64;
        try
        {
            while (Current())
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var endpoint = FindPhysicalCaptureEndpoint(enumerator, Source);
                    if (endpoint == null && Environment.TickCount64 - createdAt < 5000)
                    {
                        if (stop.WaitOne(250)) break;
                        continue;
                    }
                    if (endpoint == null)
                        throw new InvalidOperationException("The matching physical USB controller microphone is not available. Check the controller connection, headset and Windows microphone access.");
                    RunEndpoint(endpoint);
                    lastError = null;
                }
                catch (Exception ex)
                {
                    if (!Current()) break;
                    Volatile.Write(ref status, (int)ControllerRuntimeLaneState.Unavailable);
                    if (lastError != ex.Message)
                    {
                        lastError = ex.Message;
                        AppLogger.LogToGui($"USB controller {slot + 1} microphone forwarding: {ex.Message}", true);
                    }
                }
                if (stop.WaitOne(500)) break;
            }
        }
        finally
        {
            if (Source is DualSenseDevice dualSense)
                dualSense.ProfileMicrophoneMuteStateChanged -= MuteChanged;
            lock (lifetime)
            {
                finished = true;
                stop.Dispose();
            }
        }
    }

    private void RunEndpoint(MMDevice endpoint)
    {
        while (Current())
        {
            if (!owner.IsVirtualMicrophoneInterfaceActive)
            {
                Volatile.Write(ref status, (int)ControllerRuntimeLaneState.Ready);
                if (stop.WaitOne(50)) return;
                continue;
            }
            // This is capture only: no WasapiOut and no Loopback flag.
            using var capture = new WasapiCapture(endpoint, true, 20);
            var buffered = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(100),
                ReadFully = false,
            };
            object samplesGate = new();
            bool discontinuity = false;
            bool closing = false;
            Exception failure = null;
            long capturedBytes = 0;
            var packetizer = new UsbMicrophonePacketizer();
            ISampleProvider mono = CreateMono48k(buffered.ToSampleProvider());
            float[] samples = new float[480];
            long observedMuteRevision = Interlocked.Read(ref muteRevision);
            capture.DataAvailable += (_, e) =>
            {
                lock (samplesGate)
                {
                    if (closing || !Current()) return;
                    int offset = 0, count = e.BytesRecorded;
                    if (buffered.BufferedBytes + count > buffered.BufferLength)
                    {
                        buffered.ClearBuffer();
                        discontinuity = true;
                        if (count > buffered.BufferLength)
                        {
                            count = buffered.BufferLength / capture.WaveFormat.BlockAlign * capture.WaveFormat.BlockAlign;
                            offset = e.BytesRecorded - count;
                        }
                    }
                    buffered.AddSamples(e.Buffer, offset, count);
                    Interlocked.Add(ref capturedBytes, count);
                }
            };
            capture.RecordingStopped += (_, e) =>
            {
                lock (samplesGate)
                    if (!closing) failure = e.Exception ?? new InvalidOperationException("USB microphone capture stopped.");
            };
            try
            {
                capture.StartRecording();
                Volatile.Write(ref status, (int)ControllerRuntimeLaneState.Ready);
                AppLogger.LogToGui($"USB controller {slot + 1} microphone forwarding started: {endpoint.FriendlyName} -> DS4Windows Controller {slot + 1} Microphone.", false);
                long lastReceive = Environment.TickCount64;
                long previousBytes = 0;
                long queuedPackets = 0;
                long nextLog = Environment.TickCount64 + 30000;
                while (Current() && owner.IsVirtualMicrophoneInterfaceActive)
                {
                    int count;
                    long readMuteRevision;
                    lock (samplesGate)
                    {
                        if (failure != null) throw failure;
                        readMuteRevision = Interlocked.Read(ref muteRevision);
                        if (readMuteRevision != observedMuteRevision)
                        {
                            buffered.ClearBuffer();
                            observedMuteRevision = readMuteRevision;
                            discontinuity = true;
                        }
                        if (discontinuity)
                        {
                            mono = CreateMono48k(buffered.ToSampleProvider());
                            packetizer.Reset();
                            discontinuity = false;
                        }
                        count = mono.Read(samples, 0, samples.Length);
                    }
                    if (count > 0)
                        packetizer.Push(samples, count, pcm =>
                        {
                            lock (lifetime)
                                if (!retired && readMuteRevision == Interlocked.Read(ref muteRevision) && Current() && owner.TryQueueUsbMicrophonePcm(Source, slot, generation, routeGeneration, pcm)) queuedPackets++;
                        });
                    long received = Interlocked.Read(ref capturedBytes);
                    if (received != previousBytes) { previousBytes = received; lastReceive = Environment.TickCount64; }
                    else if (Environment.TickCount64 - lastReceive > 3000)
                        throw new InvalidOperationException("No USB microphone samples arrived for three seconds; retrying capture.");
                    if (Global.VerboseStartupLogging && Environment.TickCount64 >= nextLog)
                    {
                        AppLogger.LogToGui($"USB controller {slot + 1} microphone stats: capturedBytes={received} queuedPcmPackets={queuedPackets}; capture only.", false);
                        nextLog = Environment.TickCount64 + 30000;
                    }
                    if (count == 0 && stop.WaitOne(5)) break;
                }
            }
            finally
            {
                lock (samplesGate) closing = true;
                // WASAPI joins its callback thread: never hold samplesGate here.
                try { capture.StopRecording(); } catch { }
                owner.ClearUsbMicrophonePending(Source, slot, generation, routeGeneration);
            }
        }
    }

    internal static ISampleProvider CreateMono48k(ISampleProvider input)
    {
        ISampleProvider mono = input.WaveFormat.Channels switch
        {
            1 => input,
            2 => new StereoToMonoSampleProvider(input) { LeftVolume = .5f, RightVolume = .5f },
            _ => throw new NotSupportedException("USB microphone capture requires a mono or stereo Windows recording format."),
        };
        return mono.WaveFormat.SampleRate == 48000 ? mono : new WdlResamplingSampleProvider(mono, 48000);
    }

    internal static MMDevice FindPhysicalCaptureEndpoint(MMDeviceEnumerator enumerator, DS4Device source)
    {
        if (source?.HidDevice == null) return null;
        string instance = Global.GetInstanceIdFromDevicePath(source.HidDevice.DevicePath);
        if (!ConfigurationManagerHidHideDeviceNodeTree.Instance.TryGetNode(instance, out var node)) return null;
        foreach (MMDevice endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            bool selected = false;
            try
            {
                if (DualSenseAudioPassthrough.TryGetEndpointUsbipPort(endpoint, out _)) continue;
                if (!DualSenseUsbHapticsPassthrough.EndpointMatchesContainer(endpoint, node.ContainerId)) continue;
                selected = true;
                return endpoint;
            }
            finally { if (!selected) endpoint.Dispose(); }
        }
        return null;
    }

    public void Dispose()
    {
        lock (lifetime)
        {
            if (retired) return;
            retired = true;
            if (!finished) stop.Set();
            owner.ClearUsbMicrophonePending(Source, slot, generation, routeGeneration);
        }
    }
}

// One 10 ms, 48 kHz mono packet is the common input to the existing processor;
// VIIPER's output-specific packer then selects DS4 mono or DualSense stereo PCM.
internal sealed class UsbMicrophonePacketizer
{
    private readonly short[] packet = new short[480];
    private int count;
    internal void Reset() => count = 0;
    internal void Push(float[] samples, int length, Action<short[]> submit)
    {
        if (length < 0 || length > samples.Length) throw new ArgumentOutOfRangeException(nameof(length));
        for (int i = 0; i < length; i++)
        {
            float value = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1f, 1f) : 0f;
            packet[count++] = (short)Math.Clamp((int)Math.Round(value * 32768f), short.MinValue, short.MaxValue);
            if (count == packet.Length) { submit(packet); count = 0; }
        }
    }
}
