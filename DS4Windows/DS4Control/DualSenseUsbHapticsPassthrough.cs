using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DS4Windows.InputDevices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DS4Windows;

// This render stream writes only actuator channels 2/3. Windows mixes the
// independent speaker stream into channels 0/1; neither stream rewrites the
// other's samples. HID output remains owned by DualSenseDevice.
internal sealed class DualSenseUsbHapticsPassthrough
{
    private readonly object gate = new();
    private readonly Session[] slots = new Session[ControlService.MAX_DS4_CONTROLLER_COUNT];
    private readonly Task[] retirements = Enumerable.Repeat(Task.CompletedTask,
        ControlService.MAX_DS4_CONTROLLER_COUNT).ToArray();
    private readonly Func<CancellationToken, Task> runOverride;

    internal DualSenseUsbHapticsPassthrough(Func<CancellationToken, Task> runOverride = null) =>
        this.runOverride = runOverride;

    internal Task Retirement(int slot)
    {
        lock (gate) return retirements[slot];
    }

    internal void Update(int slot, DualSenseDevice device, bool enabled,
        string physicalEndpointId, Func<ViiperOutDevice> sourceResolver)
    {
        if (slot < 0 || slot >= slots.Length) return;
        physicalEndpointId ??= string.Empty;
        lock (gate)
        {
            Session previous = slots[slot];
            if (enabled && device?.ConnectionType == ConnectionType.USB &&
                previous != null && ReferenceEquals(previous.Device, device) &&
                previous.EndpointId == physicalEndpointId) return;
            previous?.Cancel();
            slots[slot] = null;
            if (!enabled || device?.ConnectionType != ConnectionType.USB) return;
            var session = new Session(slot, device, physicalEndpointId, sourceResolver, runOverride)
            { Predecessor = retirements[slot] };
            slots[slot] = session;
            // Retire the preceding capture/render pair before opening a new
            // one, including Stop -> Start and concurrent profile changes.
            retirements[slot] = session.Completion = Task.Run(async () =>
            {
                await session.Predecessor.ConfigureAwait(false);
                await session.Run().ConfigureAwait(false);
            });
        }
    }

    internal void Stop(int slot)
    {
        if (slot < 0 || slot >= slots.Length) return;
        lock (gate)
        {
            slots[slot]?.Cancel();
            slots[slot] = null;
        }
    }

    internal void StopAll()
    {
        for (int slot = 0; slot < slots.Length; slot++) Stop(slot);
    }

    private sealed class Session
    {
        private readonly int slot;
        private readonly Func<ViiperOutDevice> resolveSource;
        private readonly Func<CancellationToken, Task> runOverride;
        private readonly CancellationTokenSource stop = new();
        private readonly object callbackGate = new();
        private bool finished;
        private bool accepting;
        private BufferedWaveProvider buffer;
        internal readonly DualSenseDevice Device;
        internal readonly string EndpointId;
        internal Task Predecessor = Task.CompletedTask;
        internal Task Completion = Task.CompletedTask;

        internal Session(int slot, DualSenseDevice device, string endpointId,
            Func<ViiperOutDevice> resolveSource, Func<CancellationToken, Task> runOverride)
        {
            this.slot = slot;
            Device = device;
            EndpointId = endpointId;
            this.resolveSource = resolveSource;
            this.runOverride = runOverride;
        }

        internal void Cancel()
        {
            lock (callbackGate)
            {
                accepting = false;
                buffer?.ClearBuffer();
                if (!finished) stop.Cancel();
            }
        }

        internal async Task Run()
        {
            bool waitingLogged = false;
            try
            {
                stop.Token.ThrowIfCancellationRequested();
                if (runOverride != null)
                {
                    await runOverride(stop.Token).ConfigureAwait(false);
                    return;
                }
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        ViiperOutDevice source = resolveSource?.Invoke();
                        if (source?.IsRuntimeConnected != true || !source.SupportsRealtimeHaptics)
                            throw new InvalidOperationException("Waiting for this controller's virtual audio endpoint.");
                        await Play(source).ConfigureAwait(false);
                        waitingLogged = false;
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        if (!waitingLogged)
                        {
                            AppLogger.LogToGui(
                                $"DualSense USB native haptics waiting for controller {slot + 1}: {ex.Message}", true);
                            waitingLogged = true;
                        }
                    }
                    await Task.Delay(250, stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally
            {
                lock (callbackGate)
                {
                    accepting = false;
                    finished = true;
                    stop.Dispose();
                }
            }
        }

        private async Task Play(ViiperOutDevice source)
        {
            long generation = source.RuntimeFeedbackGeneration;
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice virtualEndpoint = FindVirtualEndpoint(enumerator, source.DirectSpeakerUsbipPort);
            if (virtualEndpoint == null ||
                !DualSenseAudioPassthrough.TryGetEndpointUsbipPort(virtualEndpoint, out int port) ||
                port != source.DirectSpeakerUsbipPort)
                throw new InvalidOperationException("The matching VIIPER four-channel audio endpoint is not ready.");

            using MMDevice physicalEndpoint = FindPhysicalEndpoint(enumerator, Device, EndpointId);
            if (physicalEndpoint == null || physicalEndpoint.ID == virtualEndpoint.ID)
                throw new InvalidOperationException(
                    "The matching physical USB audio endpoint is not ready. Check Advanced > Audio > USB DualSense speaker.");
            using var capture = new LoopbackCapture(virtualEndpoint);
            var pending = new BufferedWaveProvider(physicalEndpoint.AudioClient.MixFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(100),
                DiscardOnBufferOverflow = false,
            };
            var playback = new DualSenseUsbHapticsPlayback(pending, capture.WaveFormat);
            using var output = new WasapiOut(physicalEndpoint, AudioClientShareMode.Shared, true, 20);
            output.Init(pending);
            Exception failure = null;
            bool Current() => !stop.IsCancellationRequested &&
                ReferenceEquals(resolveSource?.Invoke(), source) &&
                source.IsRuntimeConnected && source.RuntimeFeedbackGeneration == generation &&
                source.DirectSpeakerUsbipPort == port;
            capture.DataAvailable += (_, e) =>
            {
                lock (callbackGate)
                {
                    if (!accepting || !Current()) return;
                    try { playback.Write(e.Buffer, e.BytesRecorded); }
                    catch (Exception ex) { failure = ex; accepting = false; pending.ClearBuffer(); }
                }
            };
            capture.RecordingStopped += (_, e) =>
            {
                lock (callbackGate)
                {
                    failure ??= e.Exception ?? new InvalidOperationException("The USB audio capture stopped.");
                    accepting = false;
                }
            };
            output.PlaybackStopped += (_, e) =>
            {
                lock (callbackGate)
                {
                    failure ??= e.Exception ?? new InvalidOperationException("The USB audio render stream stopped.");
                    accepting = false;
                }
            };
            try
            {
                lock (callbackGate)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    buffer = pending;
                    accepting = true;
                }
                output.Play();
                capture.StartRecording();
                AppLogger.LogToGui(
                    $"DualSense USB native haptics started for controller {slot + 1}: " +
                    $"{virtualEndpoint.FriendlyName} -> {physicalEndpoint.FriendlyName}; rear channels only.", false);
                DateTime nextLog = DateTime.UtcNow.AddSeconds(30);
                while (Current())
                {
                    string statistics = null;
                    lock (callbackGate)
                    {
                        if (failure != null) throw failure;
                        if (Global.VerboseStartupLogging && DateTime.UtcNow >= nextLog)
                        {
                            statistics = $"DualSense USB native haptics stats: sourceFrames={playback.SourceFrames} " +
                                $"nonzeroRearBlocks={playback.NonzeroRearBlocks} bufferedBytes={pending.BufferedBytes}";
                            nextLog = DateTime.UtcNow.AddSeconds(30);
                        }
                    }
                    if (statistics != null) AppLogger.LogToGui(statistics, false);
                    await Task.Delay(100, stop.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (callbackGate)
                {
                    accepting = false;
                    pending.ClearBuffer();
                    buffer = null;
                }
                // StopRecording joins its thread. Never hold callbackGate here.
                capture.StopRecording();
                output.Stop();
            }
        }
    }

    private static MMDevice FindVirtualEndpoint(MMDeviceEnumerator enumerator, int port)
    {
        if (port < 0) return null;
        foreach (MMDevice endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            bool selected = false;
            try
            {
                if (!DualSenseAudioPassthrough.IsDualSenseEndpoint(endpoint) ||
                    !DualSenseAudioPassthrough.TryGetEndpointUsbipPort(endpoint, out int endpointPort) ||
                    endpointPort != port) continue;
                selected = true;
                return endpoint;
            }
            finally { if (!selected) endpoint.Dispose(); }
        }
        return null;
    }

    private static MMDevice FindPhysicalEndpoint(MMDeviceEnumerator enumerator,
        DualSenseDevice device, string requestedId)
    {
        Guid container = Guid.Empty;
        string hidInstance = Global.GetInstanceIdFromDevicePath(device.HidDevice.DevicePath);
        if (ConfigurationManagerHidHideDeviceNodeTree.Instance.TryGetNode(hidInstance, out var hidNode))
            container = hidNode.ContainerId;
        foreach (MMDevice endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            bool selected = false;
            try
            {
                bool explicitSelection = !string.IsNullOrEmpty(requestedId);
                if (explicitSelection && endpoint.ID != requestedId) continue;
                if (!DualSenseAudioPassthrough.IsDualSenseEndpoint(endpoint) ||
                    DualSenseAudioPassthrough.TryGetEndpointUsbipPort(endpoint, out _)) continue;
                if (!EndpointMatchesContainer(endpoint, container)) continue;
                selected = true;
                return endpoint;
            }
            finally { if (!selected) endpoint.Dispose(); }
        }
        return null;
    }

    private static bool EndpointMatchesContainer(MMDevice endpoint, Guid container)
    {
        foreach (var key in new[] { PropertyKeys.PKEY_Device_ControllerDeviceId, PropertyKeys.PKEY_Device_InstanceId })
        {
            string instance;
            try { instance = endpoint.Properties[key]?.Value?.ToString(); }
            catch { continue; }
            for (int depth = 0; depth < 16 && !string.IsNullOrEmpty(instance); depth++)
            {
                if (!ConfigurationManagerHidHideDeviceNodeTree.Instance.TryGetNode(instance, out var node)) break;
                if (SamePhysicalContainer(container, node.ContainerId)) return true;
                instance = node.ParentInstanceId;
            }
        }
        return false;
    }

    internal static bool SamePhysicalContainer(Guid controller, Guid endpoint) =>
        controller != Guid.Empty && controller != HidHideDeviceIdentity.SystemContainerId && controller == endpoint;

    private sealed class LoopbackCapture : WasapiCapture
    {
        internal LoopbackCapture(MMDevice device) : base(device, true, 10) { }
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
            AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }
}
