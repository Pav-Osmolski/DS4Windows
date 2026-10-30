using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DS4Windows;

// Preview-only opt-in recorder. No disk I/O, allocation or waiting in the audio hooks.
internal static class Issue90AudioDiagnostics
{
    private static Session active;
    private static readonly string Nonce = Environment.GetEnvironmentVariable("DS4WINDOWS_ISSUE90_NONCE");
    private static readonly WaveFormat Mono48 = new(48000, 16, 1);
    private static readonly WaveFormat Stereo48 = new(48000, 16, 2);
    private static readonly WaveFormat Stereo32 = new(32000, 16, 2);
    private static readonly WaveFormat Mono16 = new(16000, 16, 1);
    private static readonly WaveFormat Stereo16 = new(16000, 16, 2);
    private static int initialized;
    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0 ||
            Environment.GetEnvironmentVariable("DS4WINDOWS_ISSUE90_CAPTURE") != "1") return;
        _ = Task.Run(async () =>
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Diagnostics");
            try
            {
                Directory.CreateDirectory(root);
                string nonce = Nonce;
                if (!Guid.TryParseExact(nonce, "N", out _)) return;
                string request = Path.Combine(root, nonce + "-start.request");
                // The launcher writes the fresh request only after explicit RECORD consent.
                DateTime ready = DateTime.UtcNow;
                for (int i = 0; i < 1800; i++)
                {
                    if (File.Exists(request) && File.GetLastWriteTimeUtc(request) >= ready.AddSeconds(-2))
                    {
                        await Run(root, nonce);
                        return;
                    }
                    await Task.Delay(500);
                }
                File.WriteAllText(Path.Combine(root, (Nonce ?? "issue90") + "-error.txt"), "Capture request timed out. Restart using the diagnostic launcher.");
            }
            catch (Exception e)
            {
                try { File.WriteAllText(Path.Combine(root, (Nonce ?? "issue90") + "-error.txt"), e.ToString()); } catch { }
            }
        });
    }

    internal static void Pcm(int slot, int lane, ReadOnlySpan<byte> data, int rate, int channels)
    {
        Session session = Volatile.Read(ref active);
        if (session == null || slot != 0 || !session.IsRecording) return;
        WaveFormat format = (rate, channels) switch { (48000, 1) => Mono48, (48000, 2) => Stereo48,
            (32000, 2) => Stereo32, (16000, 1) => Mono16, (16000, 2) => Stereo16, _ => null };
        if (format != null) session.Lanes[lane].Record(data, format, Stopwatch.GetTimestamp());
    }
    internal static void Mono(int slot, int lane, short[] data, int frames)
    {
        if (Volatile.Read(ref active) == null || slot != 0) return;
        Pcm(slot, lane, MemoryMarshal.AsBytes(data.AsSpan(0, frames)), 48000, 1);
    }
    internal static void Compressed(int slot, byte[] data, int sequence)
    {
        Session session = Volatile.Read(ref active);
        if (session == null || slot != 0 || !session.IsRecording) return;
        session.Lanes[1].Record(data, null, Stopwatch.GetTimestamp(), sequence);
    }

    internal static void InputReport(DS4Device device, byte[] data, int length)
    {
        Session session = Volatile.Read(ref active);
        if (session == null || !session.IsRecording) return;
        var devices = Program.rootHub?.DS4Controllers;
        if (devices != null && devices.Length > 0 && ReferenceEquals(devices[0], device))
            session.Lanes[6].Record(data.AsSpan(0, length), null, Stopwatch.GetTimestamp());
    }

    private static async Task Run(string root, string nonce)
    {
        var session = new Session();
        string folder = Path.Combine(root, "Issue90-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(folder);
        var errors = new ConcurrentQueue<string>();
        using var enumerator = new MMDeviceEnumerator();
        Snapshot(folder, "before", enumerator, errors);
        CopyProfile(folder, "before", errors);
        MMDevice microphone = null;
        WasapiCapture capture = null;
        try
        {
            var candidates = new List<MMDevice>();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                if (endpoint.FriendlyName.StartsWith("DS4Windows Controller 1 Microphone", StringComparison.OrdinalIgnoreCase)) candidates.Add(endpoint);
                else endpoint.Dispose();
            var matches = candidates.ToArray();
            if (matches.Length != 1)
            {
                foreach (var endpoint in matches) endpoint.Dispose();
                errors.Enqueue($"Windows virtual microphone capture unavailable: expected one named slot-1 endpoint, found {matches.Length}. No default/physical mic fallback was used.");
            }
            else
            {
                microphone = matches[0];
                capture = new WasapiCapture(microphone, true, 20);
                WaveFormat format = capture.WaveFormat;
                capture.DataAvailable += (_, e) => { if (session.IsRecording) session.Lanes[5].Record(e.Buffer.AsSpan(0, e.BytesRecorded), format, Stopwatch.GetTimestamp()); };
                capture.RecordingStopped += (_, e) => { if (e.Exception != null) errors.Enqueue("Windows mic: " + e.Exception); };
            }
            session.ActiveStartedTick = Stopwatch.GetTimestamp();
            session.DeadlineTick = session.ActiveStartedTick + Stopwatch.Frequency * 30L;
            Volatile.Write(ref active, session);
            try { capture?.StartRecording(); } catch (Exception e) { errors.Enqueue("Windows virtual mic could not start: " + e.Message); }
            File.WriteAllText(Path.Combine(root, nonce + "-recording.txt"), folder);
            AppLogger.LogToGui("Issue #90 diagnostic capture started: 30 seconds, controller 1 only. Microphone and game audio are being recorded locally.", false);
            var runtime = new List<object>();
            for (int second = 0; second < 30; second++)
            {
                runtime.Add(new { tick = Stopwatch.GetTimestamp(), profile = Global.ProfilePath[0],
                    speakerEnabled = Global.DualSenseEnableSpeakerOutput[0], headsetVolume = Global.DualSenseHeadphoneVolume[0],
                    speakerVolume = Global.DualSenseSpeakerVolume[0], micEnabled = Global.DualSenseEnableMicrophonePassthrough[0],
                    micVolume = Global.DualSenseMicrophoneVolume[0], suppression = Global.DualSenseMicrophoneNoiseSuppression[0],
                    compression = Global.DualSenseSpeakerCompression[0], bassBoost = Global.DualSenseSpeakerBassBoost[0],
                    audioSource = Global.DualSenseAudioCaptureEndpointId[0], speakerEndpoint = Global.DualSenseAudioSpeakerEndpointId[0],
                    micCapture = Global.DualSenseMicrophoneCaptureEndpointId[0], micOutput = Global.DualSenseMicrophoneOutputEndpointId[0] });
                await Task.Delay(1000);
            }
            Snapshot(folder, "recording-ended", enumerator, errors);
            File.WriteAllText(Path.Combine(folder, "runtime-settings.json"), JsonSerializer.Serialize(runtime));
        }
        finally
        {
            Interlocked.CompareExchange(ref active, null, session);
            try { capture?.StopRecording(); } catch (Exception e) { errors.Enqueue(e.Message); }
            capture?.Dispose(); microphone?.Dispose();
        }
        Snapshot(folder, "after", enumerator, errors);
        CopyProfile(folder, "after", errors);
        var lanes = new List<object>();
        for (int i = 0; i < session.Lanes.Length; i++)
            lanes.Add(session.Lanes[i].Save(folder));
        File.WriteAllText(Path.Combine(folder, "capture.json"), JsonSerializer.Serialize(new
        {
            schema = 1, startedUtc = session.StartedUtc, stopwatchFrequency = Stopwatch.Frequency,
            sessionCreatedTick = session.StartedTick, captureStartedTick = session.ActiveStartedTick, captureDeadlineTick = session.DeadlineTick, durationSeconds = 30, slot = 1,
            appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
            lanes, errors,
            notes = new[] { "Contains game audio, speech and copied profile settings. Review before sharing.",
                "Opening the diagnostic Windows microphone reader adds a recording consumer; keep NVIDIA recording too.",
                "Callback timestamps are observations, not hardware presentation timestamps. WAV files concatenate observed chunks; use chunk timelines for gaps.",
                "Correlation is a clue, not proof of digital leakage. Headset acoustic/electrical cross-talk remains possible.",
                "No settings, defaults, routing, volumes or drivers were changed by the recorder. Only slot 1 is captured." }
        }, new JsonSerializerOptions { WriteIndented = true }));
        CopyLogs(folder, errors);
        var comparisons = new[] { Compare(folder, "game-source", "mic-decoded"), Compare(folder, "game-source", "mic-processed"),
            Compare(folder, "game-source", "windows-virtual-mic"), Compare(folder, "mic-processed", "windows-virtual-mic") };
        File.WriteAllText(Path.Combine(folder, "analysis.json"), JsonSerializer.Serialize(new { comparisons, errors }, new JsonSerializerOptions { WriteIndented = true }));
        string zip = folder + ".zip";
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, false);
        File.WriteAllText(Path.Combine(root, nonce + "-complete.txt"), zip);
        AppLogger.LogToGui("Issue #90 diagnostic ZIP saved: " + zip, false);
    }

    private static object Compare(string folder, string a, string b)
    {
        try
        {
            float[] x = ReadEnvelope(Path.Combine(folder, a + ".wav"));
            float[] y = ReadEnvelope(Path.Combine(folder, b + ".wav"));
            return new { source = a, target = b, result = Correlate(x, y), note = "10 ms RMS-envelope correlation, +/-2 seconds; inspect WAVs/timestamps before attributing a cause." };
        }
        catch (Exception e) { return new { source = a, target = b, unavailable = e.Message }; }
    }
    private static float[] ReadEnvelope(string path)
    {
        using var reader = new AudioFileReader(path);
        var bins = new List<float>();
        int count = reader.WaveFormat.SampleRate / 100 * reader.WaveFormat.Channels;
        float[] samples = new float[count]; int n;
        while ((n = reader.Read(samples, 0, samples.Length)) > 0)
        {
            double sum = 0; for (int i = 0; i < n; i++) sum += samples[i] * samples[i];
            bins.Add((float)Math.Sqrt(sum / n));
        }
        return bins.ToArray();
    }
    internal static Correlation Correlate(float[] x, float[] y)
    {
        if (x == null || y == null || x.Any(v => !float.IsFinite(v)) || y.Any(v => !float.IsFinite(v)))
            return new Correlation(false, null, null);
        double best = double.NegativeInfinity; int bestLag = 0, candidates = 0;
        for (int lag = -200; lag <= 200; lag++)
        {
            int start = Math.Max(0, -lag), end = Math.Min(x.Length, y.Length - lag), n = end - start;
            if (n < 100) continue;
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            for (int i = start; i < end; i++) { double a = x[i], b = y[i + lag]; sx += a; sy += b; sxx += a*a; syy += b*b; sxy += a*b; }
            double variance = (n * sxx - sx*sx) * (n * syy - sy*sy);
            if (variance <= 1e-12) continue;
            candidates++;
            double c = (n*sxy - sx*sy) / Math.Sqrt(variance);
            if (c > best) { best = c; bestLag = lag; }
        }
        return new Correlation(candidates > 0, candidates > 0 ? best : null, candidates > 0 ? bestLag * 10 : null);
    }
    internal record Correlation(bool Available, double? Coefficient, int? TargetLagMs);

    private static void Snapshot(string folder, string phase, MMDeviceEnumerator enumerator, ConcurrentQueue<string> errors)
    {
        try
        {
            var defaults = new List<object>();
            foreach (DataFlow flow in new[] { DataFlow.Render, DataFlow.Capture })
            foreach (Role role in new[] { Role.Console, Role.Multimedia, Role.Communications })
                try { using var d = enumerator.GetDefaultAudioEndpoint(flow, role); defaults.Add(new { flow = flow.ToString(), role = role.ToString(), id = d.ID }); } catch { }
            var endpoints = new List<object>();
            foreach (DataFlow flow in new[] { DataFlow.Render, DataFlow.Capture })
            foreach (var d in enumerator.EnumerateAudioEndPoints(flow, DeviceState.All))
            {
                using (d)
                {
                    try { endpoints.Add(new { id = d.ID, name = d.FriendlyName, flow = flow.ToString(), state = d.State.ToString(),
                        muted = d.AudioEndpointVolume.Mute, volume = d.AudioEndpointVolume.MasterVolumeLevelScalar,
                        format = d.AudioClient.MixFormat.ToString(), sessions = ReadSessions(d) }); }
                    catch (Exception e) { endpoints.Add(new { id = d.ID, name = d.FriendlyName, state = d.State.ToString(), error = e.Message }); }
                }
            }
            File.WriteAllText(Path.Combine(folder, "endpoints-" + phase + ".json"), JsonSerializer.Serialize(new { defaults, endpoints }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { errors.Enqueue("Endpoint snapshot: " + e.Message); }
    }
    private static object[] ReadSessions(MMDevice device)
    {
        var result = new List<object>();
        try
        {
            var manager = device.AudioSessionManager;
            try
            {
            var sessions = manager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                int pid = unchecked((int)session.GetProcessID);
                string name = null;
                try { using var process = Process.GetProcessById(pid); name = process.ProcessName; } catch { }
                result.Add(new { processId = pid, processName = name, state = session.State.ToString() });
            }
            }
            finally { manager.Dispose(); }
        }
        catch (Exception e) { result.Add(new { unavailable = e.Message }); }
        return result.ToArray();
    }

    private static void CopyProfile(string folder, string phase, ConcurrentQueue<string> errors)
    {
        try
        {
            string name = Path.GetFileName(Global.ProfilePath[0]);
            string source = Path.Combine(Global.appdatapath, "Profiles", name + ".xml");
            File.WriteAllText(Path.Combine(folder, "profile-" + phase + ".txt"), source);
            if (File.Exists(source) && new FileInfo(source).Length <= 2 * 1024 * 1024) File.Copy(source, Path.Combine(folder, "profile-" + phase + ".xml"));
        }
        catch (Exception e) { errors.Enqueue("Profile: " + e.Message); }
    }
    private static void CopyLogs(string folder, ConcurrentQueue<string> errors)
    {
        try
        {
            string root = DS4WinWPF.LoggerHolder.ResolveLogDirectory();
            foreach (string name in new[] { "ds4windows_log.txt", "viiper-debug.log" })
            {
                string path = Path.Combine(root, name); if (!File.Exists(path)) continue;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long count = Math.Min(stream.Length, 8 * 1024 * 1024); stream.Seek(-count, SeekOrigin.End);
                using var output = File.Create(Path.Combine(folder, name));
                byte[] buffer = new byte[8192];
                while (count > 0) { int read = stream.Read(buffer, 0, (int)Math.Min(count, buffer.Length)); if (read == 0) break; output.Write(buffer, 0, read); count -= read; }
            }
        }
        catch (Exception e) { errors.Enqueue("Logs: " + e.Message); }
    }
    private sealed class Session
    {
        internal readonly DateTime StartedUtc = DateTime.UtcNow;
        internal readonly long StartedTick = Stopwatch.GetTimestamp();
        internal long ActiveStartedTick, DeadlineTick;
        internal bool IsRecording => Stopwatch.GetTimestamp() <= DeadlineTick;
        internal readonly Lane[] Lanes = new[] { new Lane("game-source"), new Lane("physical-mic-sbc", 2*1024*1024), new Lane("mic-decoded", 4*1024*1024),
            new Lane("mic-processed", 4*1024*1024), new Lane("mic-transport", 4*1024*1024), new Lane("windows-virtual-mic"), new Lane("physical-bt-reports") };
    }
    internal sealed class Lane
    {
        private readonly string name;
        private readonly byte[] bytes;
        private readonly Chunk[] chunks;
        private int count, chunkCount, drops;
        private bool sealedForExport;
        private WaveFormat format;
        internal Lane(string name, int capacity = 16 * 1024 * 1024, int chunkCapacity = 8192)
        { this.name=name; bytes=new byte[capacity]; chunks=new Chunk[chunkCapacity]; }
        internal void Record(ReadOnlySpan<byte> data, WaveFormat wave, long tick, int sequence = -1)
        {
            if (!Monitor.TryEnter(this)) { Interlocked.Increment(ref drops); return; }
            try
            {
                if (sealedForExport || data.Length == 0) return;
                if (chunkCount >= chunks.Length || data.Length > bytes.Length-count ||
                    (format != null && wave != null && !format.Equals(wave))) { Interlocked.Increment(ref drops); return; }
                format ??= wave;
                data.CopyTo(bytes.AsSpan(count)); chunks[chunkCount++]=new Chunk(count,data.Length,tick,sequence); count+=data.Length;
            }
            finally { Monitor.Exit(this); }
        }
        internal object Save(string folder)
        {
            lock(this)
            {
                sealedForExport = true;
                if (format != null) { using var wav=new WaveFileWriter(Path.Combine(folder,name+".wav"),format); wav.Write(bytes,0,count); }
                else File.WriteAllBytes(Path.Combine(folder,name+".bin"),bytes.AsSpan(0,count).ToArray());
                File.WriteAllText(Path.Combine(folder,name+"-chunks.json"),JsonSerializer.Serialize(chunks.AsSpan(0,chunkCount).ToArray()));
                return new { name, bytes=count, chunks=chunkCount, droppedChunks=drops, format=format?.ToString() };
            }
        }
        internal int Count => count;
        internal int Drops => drops;
    }
    private readonly record struct Chunk(int Offset, int Length, long Tick, int Sequence);
}
