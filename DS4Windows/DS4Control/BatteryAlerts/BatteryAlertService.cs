using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DS4Windows.InputDevices;
using DS4Windows.Switch2;
using NAudio.CoreAudioApi;

namespace DS4Windows;

internal sealed class BatteryAlertService
{
    private readonly ControlService hub;
    private readonly object gate = new();
    private readonly Dictionary<string, BatteryAlertPolicy> policies = new(StringComparer.OrdinalIgnoreCase);
    private readonly long[] manualTimes = new long[ControlService.MAX_DS4_CONTROLLER_COUNT];
    private CancellationTokenSource runCancellation, playbackCancellation;
    private Channel<AlertRequest> requests;
    private readonly SemaphoreSlim playbackGate = new(1);
    private DS4Device playingDevice;
    private int playingSlot = -1;
    private long playbackEpoch;
    internal event Action<string> StatusChanged;
    private sealed record AlertRequest(int Slot, DS4Device Device, BatteryAlertKind Kind, bool Preview, long Epoch);

    internal BatteryAlertService(ControlService hub) => this.hub = hub;

    internal void Start()
    {
        lock (gate)
        {
            if (runCancellation != null) return;
            runCancellation = new();
            requests = Channel.CreateBounded<AlertRequest>(new BoundedChannelOptions(16)
                { SingleReader = false, FullMode = BoundedChannelFullMode.Wait });
            var channel = requests;
            var token = runCancellation.Token;
            var cancellation = runCancellation;
            _ = Task.WhenAll(Task.Run(() => ProcessAsync(channel, token)), Task.Run(() => MonitorAsync(channel, token)))
                .ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        }
    }

    internal void Stop()
    {
        lock (gate)
        {
            Interlocked.Increment(ref playbackEpoch);
            runCancellation?.Cancel();
            runCancellation = null;
            requests?.Writer.TryComplete();
            requests = null;
            playbackCancellation?.Cancel();
            lock (policies) foreach (var policy in policies.Values) policy.Pause();
        }
    }

    internal void StopPlayback()
    {
        lock (gate)
        {
            Interlocked.Increment(ref playbackEpoch);
            playbackCancellation?.Cancel();
            if (requests != null) while (requests.Reader.TryRead(out _)) { }
        }
    }

    // Called on the input thread: only a bounded enqueue, never speech, file or audio I/O.
    internal void RequestCheck(int slot)
    {
        if ((uint)slot >= (uint)hub.DS4Controllers.Length) return;
        long now = Stopwatch.GetTimestamp();
        long previous = Interlocked.Read(ref manualTimes[slot]);
        if (previous != 0 && Stopwatch.GetElapsedTime(previous, now).TotalSeconds < 3) return;
        if (Interlocked.CompareExchange(ref manualTimes[slot], now, previous) != previous) return;
        var device = hub.DS4Controllers[slot];
        if (device != null) requests?.Writer.TryWrite(new(slot, device, BatteryAlertKind.Check, false, Interlocked.Read(ref playbackEpoch)));
    }

    internal async Task PreviewAsync(int slot, BatteryAlertKind kind, CancellationToken token = default)
    {
        StopPlayback();
        DS4Device device = (uint)slot < (uint)hub.DS4Controllers.Length ? hub.DS4Controllers[slot] : null;
        if (!Current(slot, device)) device = null;
        await DeliverAsync(new(slot, device, kind, true, Interlocked.Read(ref playbackEpoch)), token).ConfigureAwait(false);
    }

    private bool Current(int slot, DS4Device device) => device != null &&
        (uint)slot < (uint)hub.DS4Controllers.Length && ReferenceEquals(hub.DS4Controllers[slot], device) &&
        !device.IsDisconnecting && !device.IsRemoving && device.Synced;

    internal static BatteryAlertReading Read(DS4Device device)
    {
        if (device == null) return default;
        if (device is Switch2RuntimeInputDevice switch2)
        {
            var status = switch2.Switch2BatteryStatus;
            return new(status.CompatibilityPercentage, false, false, status.IsValid, status.Band.ToString().ToLowerInvariant());
        }
        bool reliable = device.BatteryReadingAvailable && (device.FeatureSet & VidPidFeatureSet.NoBatteryReading) == 0;
        string level = device.DeviceType is InputDeviceType.JoyConL or InputDeviceType.JoyConR or InputDeviceType.SwitchPro or InputDeviceType.DS3
            ? (device.Battery <= 25 ? "low" : device.Battery <= 50 ? "medium" : "high") : null;
        return new(Math.Clamp(device.Battery, 0, 100), device.Charging,
            reliable && device.BatteryChargeComplete, reliable, level);
    }

    internal static string Message(int slot, BatteryAlertKind kind, BatteryAlertReading reading)
    {
        string prefix = $"Controller {slot + 1}";
        if (!reading.Reliable) return prefix + (reading.Charging ? " charging. Battery level unavailable." : ": battery information unavailable.");
        if (kind == BatteryAlertKind.Full && reading.Full) return prefix + " fully charged.";
        if (kind == BatteryAlertKind.Low) return prefix + " battery low." + (reading.Level == null ? $" {reading.Percentage} percent." : "");
        string status = reading.Level != null ? $"battery level: {reading.Level}" : $"battery: {reading.Percentage} percent";
        return prefix + " " + status + "." + (reading.Charging ? " Charging." : "");
    }

    private async Task MonitorAsync(Channel<AlertRequest> channel, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var settings = Global.BatteryAlerts;
                lock (gate)
                {
                    if (playingDevice != null && !Current(playingSlot, playingDevice)) playbackCancellation?.Cancel();
                }
                if (settings.LowEnabled || settings.FullEnabled)
                {
                    var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < hub.DS4Controllers.Length; i++)
                    {
                        var device = hub.DS4Controllers[i];
                        if (!Current(i, device)) continue;
                        string identity = device.ProfileLinkId;
                        if (string.IsNullOrWhiteSpace(identity)) identity = "session:" + device.GetHashCode();
                        observed.Add(identity);
                        var reading = Read(device);
                        lock (gate)
                        {
                            if (token.IsCancellationRequested || !ReferenceEquals(requests, channel)) return;
                            lock (policies)
                            {
                                if (!policies.TryGetValue(identity, out var policy))
                                {
                                    if (policies.Count >= 256) policies.Remove(policies.Keys.First());
                                    policies[identity] = policy = new();
                                }
                                var kind = policy.Observe(reading, settings, Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                                if (kind.HasValue) channel.Writer.TryWrite(new(i, device, kind.Value, false, Interlocked.Read(ref playbackEpoch)));
                            }
                        }
                    }
                    lock (gate)
                    {
                        if (token.IsCancellationRequested || !ReferenceEquals(requests, channel)) return;
                        lock (policies)
                            foreach (var entry in policies) if (!observed.Contains(entry.Key)) entry.Value.Pause();
                    }
                }
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report("Battery monitoring failed: " + ex.Message); }
    }

    private async Task ProcessAsync(Channel<AlertRequest> channel, CancellationToken token)
    {
        try
        {
            await foreach (var request in channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                if (Current(request.Slot, request.Device)) await DeliverAsync(request, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DeliverAsync(AlertRequest request, CancellationToken serviceToken)
    {
        await playbackGate.WaitAsync(serviceToken).ConfigureAwait(false);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
        lock (gate)
        {
            if (request.Epoch != Interlocked.Read(ref playbackEpoch)) { playbackGate.Release(); return; }
            playbackCancellation = cancellation;
            playingDevice = request.Device;
            playingSlot = request.Slot;
        }
        try
        {
            if (request.Device != null && !Current(request.Slot, request.Device)) return;
            var settings = Global.BatteryAlerts.Clone();
            if (!request.Preview && request.Kind == BatteryAlertKind.Low && !settings.LowEnabled ||
                !request.Preview && request.Kind == BatteryAlertKind.Full && !settings.FullEnabled) return;
            var rule = settings.Rule(request.Kind);
            if (rule.Mode == BatteryAlertMode.Off && !rule.Vibrate)
                throw new InvalidOperationException("Choose speech, a custom sound or vibration for this alert.");
            if (rule.Mode == BatteryAlertMode.Off && request.Device == null)
                throw new InvalidOperationException("Connect the selected controller to preview vibration.");
            var reading = request.Preview ? new BatteryAlertReading(50, false, request.Kind == BatteryAlertKind.Full, true) : Read(request.Device);
            if (!request.Preview && request.Kind == BatteryAlertKind.Low &&
                (!reading.Reliable || reading.Charging || reading.Percentage > settings.LowThreshold)) return;
            if (!request.Preview && request.Kind == BatteryAlertKind.Full && !reading.Full) return;
            var token = cancellation.Token;
            Report("Preparing battery alert...");
            var clips = new List<float[]>();
            if (settings.Volume > 0 && (rule.Mode is BatteryAlertMode.Sound or BatteryAlertMode.Both))
                clips.Add(await Task.Run(() => BatteryAlertAudio.ReadSound(rule.SoundFile, token), token).ConfigureAwait(false));
            if (settings.Volume > 0 && (rule.Mode is BatteryAlertMode.Speech or BatteryAlertMode.Both))
                clips.Add(await BatteryAlertAudio.SpeakAsync(Message(request.Slot, request.Kind, reading), settings, token).ConfigureAwait(false));
            token.ThrowIfCancellationRequested();
            if (request.Device != null && !Current(request.Slot, request.Device)) return;
            Task vibration = rule.Vibrate && request.Device != null
                ? PlayVibrationAsync(request.Device, rule, token) : Task.CompletedTask;
            try
            {
                Report("Playing battery alert...");
                foreach (var clip in clips)
                    await BatteryAlertAudio.PlayAsync(clip, settings.Endpoint,
                        () => ControllerEndpoint(request.Slot, request.Device), settings.Volume, token).ConfigureAwait(false);
                await vibration.ConfigureAwait(false);
            }
            finally { cancellation.Cancel(); await vibration.ConfigureAwait(false); }
            Report("Battery alert finished.");
        }
        catch (OperationCanceledException) { Report("Battery alert stopped."); }
        catch (Exception ex)
        {
            Report("Battery alert: " + ex.Message);
            if (!request.Preview) AppLogger.LogToTray("Battery alert could not play: " + ex.Message, true);
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(playbackCancellation, cancellation)) { playbackCancellation = null; playingDevice = null; }
            }
            playbackGate.Release();
        }
    }

    private string ControllerEndpoint(int slot, DS4Device device)
    {
        if (!Current(slot, device)) throw new InvalidOperationException("Connect the controller before previewing its speaker.");
        if (device.DeviceType is not (InputDeviceType.DS4 or InputDeviceType.DualSense))
            throw new InvalidOperationException("This controller does not have a supported built-in speaker route. Select a headset or Windows audio device.");
        if (!Global.DualSenseEnableSpeakerOutput[slot] || Global.DualSenseHeadsetOnlyAudio[slot])
            throw new InvalidOperationException("Enable the controller speaker in Profile Editor > Controller audio first.");
        var output = hub.GetPlayStationFeatureOutput(slot);
        int port = output?.DirectSpeakerUsbipPort ?? -1;
        if (port < 0) throw new InvalidOperationException("This controller has no active native audio route.");
        if (DualSenseAudioPassthrough.EvaluateDirectSpeakerRoute(Global.DualSenseAudioCaptureEndpointId[slot],
                DualSenseAudioPassthrough.GetEndpointKind(output.OutputType), output) != DirectSpeakerRouteDecision.Direct)
            throw new InvalidOperationException("Choose native game audio as the controller speaker source, or select a headset for battery alerts.");
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var endpoint in devices)
        {
            using (endpoint)
                if (DualSenseAudioPassthrough.TryGetEndpointUsbipPort(endpoint, out int candidate) && candidate == port)
                    return endpoint.ID;
        }
        throw new InvalidOperationException("The matching controller audio device is unavailable.");
    }

    private static async Task PlayVibrationAsync(DS4Device device, BatteryAlertRule rule, CancellationToken token)
    {
        for (int i = 0; i < rule.Pulses; i++)
        {
            token.ThrowIfCancellationRequested();
            using (var lease = device.TryBeginBatteryAlertRumble((byte)(rule.Strength * 255 / 100)))
            {
                if (lease == null) return;
                await Task.Delay(rule.Duration, token).ConfigureAwait(false);
            }
            if (i + 1 < rule.Pulses) await Task.Delay(150, token).ConfigureAwait(false);
        }
    }

    private void Report(string status)
    {
        if (StatusChanged != null)
            foreach (Action<string> handler in StatusChanged.GetInvocationList())
                try { handler(status); } catch (InvalidOperationException) { } catch (OperationCanceledException) { }
        if (status.StartsWith("Battery alert:", StringComparison.Ordinal)) AppLogger.LogToGui(status, true);
    }
}
