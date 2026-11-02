using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DS4Windows.InputDevices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DS4Windows;

// Explicitly opt-in diagnostic companion. Never runs during an ordinary launch.
internal sealed class Issue98Diagnostics : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button run = new() { Content = "Run controlled haptics test" };
    private readonly Button saveGame = new() { Content = "Save game-test logs" };
    private readonly CancellationTokenSource closing = new();
    private readonly List<object> observations = new();
    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "Diagnostics",
        "Issue98-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
    private bool busy;

    internal Issue98Diagnostics(Window owner)
    {
        Owner = owner;
        Title = "Testing only — Issue #98 haptics diagnostics";
        Width = 700; Height = 470;
        Background = owner.Background; Foreground = owner.Foreground;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Issue #98 — virtual DualSense feedback test", FontSize = 22 });
        panel.Children.Add(new TextBlock { Margin = new Thickness(0, 16, 0, 16), TextWrapping = TextWrapping.Wrap,
            Text = "Close the game for the controlled test. Connect only the affected DualSense over Bluetooth and choose DualSense emulation. Enable Verbose logging and VIIPER debug logging, then restart this preview if needed.\n\nThe test sends two short, low-level waveforms to the matching virtual controller's rear audio channels. It does not record microphone or desktop audio, change sound defaults, volume, drivers or power settings. Logs and device information stay local." });
        run.Margin = saveGame.Margin = new Thickness(0, 0, 0, 12);
        run.Click += async (_, _) => await RunAsync();
        saveGame.Click += async (_, _) => await SaveGameAsync();
        panel.Children.Add(run); panel.Children.Add(saveGame); panel.Children.Add(status);
        Content = panel;
        Closing += (_, e) => { if (busy) { e.Cancel = true; closing.Cancel(); } };
        Closed += (_, _) => closing.Dispose();
        status.Text = "First run the controlled test. Afterwards, test the game for 90 seconds and use Save game-test logs. ZIPs are saved next to this preview in Diagnostics.";
    }

    private static (int Slot, DualSenseDevice Device, ViiperOutDevice Output) Target()
    {
        var service = DS4WinWPF.App.rootHub ?? throw new InvalidOperationException("Start DS4Windows first.");
        var slots = Enumerable.Range(0, service.DS4Controllers.Length)
            .Where(i => service.DS4Controllers[i] != null).ToArray();
        if (slots.Length != 1 || service.DS4Controllers[slots[0]] is not DualSenseDevice device ||
            device.ConnectionType != ConnectionType.BT ||
            service.outputDevices[slots[0]] is not ViiperOutDevice output || !output.IsRuntimeConnected ||
            output.OutputType is not (OutContType.ViiperDualSense or OutContType.ViiperDualSenseEdge))
            throw new InvalidOperationException("Connect only one physical DualSense/DualSense Edge over Bluetooth, with a ready virtual DualSense output.");
        return (slots[0], device, output);
    }

    private async Task RunAsync()
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            status.Text = "Checking audio endpoints…";
            await Task.Run(CaptureEndpoints, closing.Token);
            var target = Target();
            if (!Global.VerboseStartupLogging || !Global.ViiperDebugLogging)
                throw new InvalidOperationException("Enable Verbose logging and VIIPER debug logging on the Log page, fully exit and restart this preview, then retry.");
            long generation = target.Output.RuntimeFeedbackGeneration;
            int port = target.Output.DirectSpeakerUsbipPort;
            Record("controlled-before", Snapshot(target.Device));
            status.Text = "Checking the matching virtual audio endpoint, then sending left and right pulses…";
            await Task.Run(async () =>
            {
                using var enumerator = new MMDeviceEnumerator();
                var matches = new List<MMDevice>();
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    if (port >= 0 && DualSenseAudioPassthrough.IsDualSenseEndpoint(endpoint) &&
                        DualSenseAudioPassthrough.TryGetEndpointUsbipPort(endpoint, out int endpointPort) && endpointPort == port)
                        matches.Add(endpoint);
                    else endpoint.Dispose();
                }
                try
                {
                    if (matches.Count != 1) throw new InvalidOperationException("No unique active four-channel virtual DualSense endpoint matches this output. No sound was sent.");
                    var endpoint = matches[0];
                    var format = endpoint.AudioClient.MixFormat;
                    Record("selected-endpoint", new { endpoint.ID, endpoint.FriendlyName, format.SampleRate, format.Channels,
                        muted = endpoint.AudioEndpointVolume.Mute, volume = endpoint.AudioEndpointVolume.MasterVolumeLevelScalar, port });
                    if (format.Channels != 4) throw new InvalidOperationException("The matching virtual endpoint does not expose four audio channels. No sound was sent.");
                    if (!Current()) throw new InvalidOperationException("Controller output changed before the test. Please retry.");
                    using var output = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 30);
                    var signal = new Issue98RearPulseProvider();
                    var stopped = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                    output.PlaybackStopped += (_, e) => stopped.TrySetResult(e.Exception);
                    output.Init(new SampleToWaveProvider(signal));
                    DS4WinWPF.App.rootHub.LogDebug("[Issue98] Controlled rear-channel PCM test starting; virtual USB/IP port=" + port);
                    Record("pulse-start", new { port, generation });
                    output.Play();
                    try
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(4);
                        while (output.PlaybackState == PlaybackState.Playing && DateTime.UtcNow < deadline)
                        {
                            closing.Token.ThrowIfCancellationRequested();
                            if (!Current()) throw new InvalidOperationException("Controller output changed during the test.");
                            await Task.Delay(50, closing.Token);
                        }
                        if (output.PlaybackState == PlaybackState.Playing)
                            throw new TimeoutException("The bounded audio test did not finish within four seconds.");
                        if (stopped.Task.IsCompleted && stopped.Task.Result is Exception failure)
                            throw new InvalidOperationException("Windows audio playback failed.", failure);
                        if (signal.TotalSamplesRead != Issue98RearPulseProvider.Frames * 4)
                            throw new InvalidOperationException("Windows audio playback stopped before consuming the complete test signal.");
                    }
                    finally { output.Stop(); Record("pulse-stop", new { port }); }
                    bool Current() => target.Output.IsRuntimeConnected &&
                        target.Output.RuntimeFeedbackGeneration == generation && target.Output.DirectSpeakerUsbipPort == port &&
                        ReferenceEquals(DS4WinWPF.App.rootHub.DS4Controllers[target.Slot], target.Device) &&
                        ReferenceEquals(DS4WinWPF.App.rootHub.outputDevices[target.Slot], target.Output);
                }
                finally { foreach (var endpoint in matches) endpoint.Dispose(); }
            }, closing.Token);
            await Task.Delay(6000, closing.Token);
            Record("controlled-after", Snapshot(target.Device));
            var answer = MessageBox.Show(this, "Did you feel BOTH pulses?\n\nYes = both; No = one or neither; Cancel = unsure.",
                "Physical result", MessageBoxButton.YesNoCancel);
            Record("physical-observation", answer.ToString());
            await SaveAsync("controlled");
        }
        catch (Exception ex) { Record("test-error", ex.ToString()); await SaveAsync("controlled-error"); }
        finally { SetBusy(false); }
    }

    private async Task SaveGameAsync()
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            await Task.Run(CaptureEndpoints, closing.Token);
            var target = Target();
            Record("game-capture", Snapshot(target.Device));
            await SaveAsync("game");
        }
        catch (Exception ex) { Record("capture-error", ex.ToString()); await SaveAsync("game-error"); }
        finally { SetBusy(false); }
    }

    private static object Snapshot(DualSenseDevice device) => new
    {
        version = typeof(Issue98Diagnostics).Assembly.GetCustomAttributes(false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
        verbose = Global.VerboseStartupLogging,
        viiperDebug = Global.ViiperDebugLogging,
        device.BluetoothAudioPacerStartupCompletedWrites,
        device.BluetoothAudioPacerStartupMaximumCompletionMilliseconds,
        device.BluetoothAudioPacerStreamingCompletedWrites,
        device.BluetoothAudioPacerStreamingMaximumCompletionMilliseconds,
        device.BluetoothAudioPacerRealtimeHapticsPresentedCount,
    };

    private void CaptureEndpoints()
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (Role role in new[] { Role.Console, Role.Multimedia, Role.Communications })
        {
            try
            {
                using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                Record("default-render-endpoint", new { role = role.ToString(), endpoint.ID, endpoint.FriendlyName });
            }
            catch (Exception ex) { Record("default-endpoint-error", ex.Message); }
        }
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
        using (endpoint)
        {
            try
            {
                bool virtualEndpoint = DualSenseAudioPassthrough.TryGetEndpointUsbipPort(endpoint, out int port);
                Record("render-endpoint", new { endpoint.ID, endpoint.FriendlyName, state = endpoint.State.ToString(),
                    virtualEndpoint, port, mixFormat = endpoint.State == DeviceState.Active ? endpoint.AudioClient.MixFormat.ToString() : null,
                    muted = endpoint.State == DeviceState.Active ? (bool?)endpoint.AudioEndpointVolume.Mute : null,
                    volume = endpoint.State == DeviceState.Active ? (float?)endpoint.AudioEndpointVolume.MasterVolumeLevelScalar : null });
                if (endpoint.State == DeviceState.Active)
                {
                    var sessions = endpoint.AudioSessionManager.Sessions;
                    for (int index = 0; index < sessions.Count; index++)
                    using (var session = sessions[index])
                        Record("render-session", new { endpointId = endpoint.ID, processId = session.GetProcessID,
                            session.DisplayName, state = session.State.ToString() });
                }
            }
            catch (Exception ex) { Record("endpoint-error", new { endpoint.ID, error = ex.ToString() }); }
        }
    }

    private void Record(string kind, object data)
    {
        lock (observations) observations.Add(new { utc = DateTime.UtcNow, kind, data });
    }

    private async Task SaveAsync(string kind)
    {
        try { await SaveCoreAsync(kind); }
        catch (Exception ex) { status.Text = "Could not save the diagnostic ZIP: " + ex.Message + "\nExtract the preview into a writable folder and retry."; }
    }

    private async Task SaveCoreAsync(string kind)
    {
        string zip = await Task.Run(() =>
        {
            Directory.CreateDirectory(directory);
            string capture = Path.Combine(directory, kind + "-" + DateTime.Now.ToString("HHmmss-fff"));
            Directory.CreateDirectory(capture);
            NLog.LogManager.Flush();
            var errors = new List<string>();
            try
            {
                var target = Target();
                string name = Global.ProfilePath[target.Slot];
                // Resolve only a simple profile name under the active profile directory.
                if (!string.IsNullOrEmpty(name) && Path.GetFileName(name) == name)
                    File.Copy(Path.Combine(Global.appdatapath, "Profiles", name + ".xml"), Path.Combine(capture, "active-profile.xml"));
            }
            catch (Exception ex) { errors.Add("active profile: " + ex.Message); }
            foreach (string name in new[] { "ds4windows_log.txt", "viiper-debug.log" })
            {
                try { File.Copy(Path.Combine(DS4WinWPF.LoggerHolder.ResolveLogDirectory(), name), Path.Combine(capture, name)); }
                catch (Exception ex) { errors.Add(name + ": " + ex.Message); }
            }
            string adapterInfo = Path.Combine(AppContext.BaseDirectory, "Diagnostics", "issue98-adapter-info.txt");
            if (File.Exists(adapterInfo)) File.Copy(adapterInfo, Path.Combine(capture, "adapter-info.txt"));
            lock (observations) File.WriteAllText(Path.Combine(capture, "observations.json"),
                JsonSerializer.Serialize(new { observations, copyErrors = errors }, new JsonSerializerOptions { WriteIndented = true }));
            string result = capture + ".zip";
            ZipFile.CreateFromDirectory(capture, result);
            return result;
        });
        status.Text = "Saved: " + zip + "\nKeep this ZIP even if the test works. Review it before sharing; nothing was uploaded.";
    }

    private void SetBusy(bool value)
    {
        busy = value; run.IsEnabled = saveGame.IsEnabled = !value;
        if (!value && closing.IsCancellationRequested) Close();
    }
}

internal sealed class Issue98RearPulseProvider : ISampleProvider
{
    internal const int Frames = 48000 * 2;
    private int sample;
    internal int TotalSamplesRead => sample;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 4);
    public int Read(float[] buffer, int offset, int count)
    {
        int read = Math.Min(count, Frames * 4 - sample);
        for (int i = 0; i < read; i++, sample++)
        {
            int frame = sample / 4, channel = sample % 4;
            double time = frame / 48000.0;
            double start = channel == 2 ? 0.2 : 1.1;
            double phase = time - start;
            double envelope = phase >= 0 && phase < 0.45
                ? Math.Min(1, Math.Min(phase / 0.02, (0.45 - phase) / 0.02)) : 0;
            buffer[offset + i] = channel < 2 ? 0 :
                (float)(0.15 * envelope * Math.Sin(2 * Math.PI * 120 * phase));
        }
        return read;
    }
}
