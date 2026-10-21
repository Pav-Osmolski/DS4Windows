using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Media.SpeechSynthesis;

namespace DS4Windows;

internal readonly record struct BatteryAlertChoice(string Id, string Name);
internal static class BatteryAlertAudio
{
    internal const int SampleRate = 48000;
    internal const int MaximumSeconds = 30;
    internal const int MaximumFileBytes = 64 * 1024 * 1024;
    internal static string SoundDirectory => Path.Combine(Global.appdatapath, "BatteryAlertSounds");

    internal static IReadOnlyList<BatteryAlertChoice> Voices() =>
        new[] { new BatteryAlertChoice("", "System default") }.Concat(
            SpeechSynthesizer.AllVoices.OrderBy(v => v.Language).ThenBy(v => v.DisplayName)
                .Select(v => new BatteryAlertChoice(v.Id, $"{v.DisplayName} ({v.Language})"))).ToArray();

    internal static IReadOnlyList<BatteryAlertChoice> Outputs()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<BatteryAlertChoice> { new("", "Windows default audio device"),
            new("controller", "Controller speaker (enabled native audio route)") };
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in devices)
        {
            using (device) result.Add(new(device.ID, device.FriendlyName));
        }
        return result;
    }

    internal static async Task<float[]> SpeakAsync(string text, BatteryAlertSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var speech = new SpeechSynthesizer();
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == settings.Voice);
        if (voice != null) speech.Voice = voice;
        speech.Options.SpeakingRate = settings.Rate;
        using var stream = await speech.SynthesizeTextToStreamAsync(text).AsTask(token).ConfigureAwait(false);
        using var reader = new WaveFileReader(stream.AsStreamForRead());
        return Decode(reader.ToSampleProvider(), token);
    }

    internal static float[] Decode(ISampleProvider source, CancellationToken token)
    {
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        if (source.WaveFormat.Channels != 1) throw new InvalidDataException("Choose a mono or stereo sound file.");
        if (source.WaveFormat.SampleRate != SampleRate) source = new WdlResamplingSampleProvider(source, SampleRate);
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (samples.Count + read > MaximumSeconds * SampleRate)
                throw new InvalidDataException($"Choose an alert sound no longer than {MaximumSeconds} seconds.");
            for (int i = 0; i < read; i++)
            {
                if (!float.IsFinite(buffer[i])) throw new InvalidDataException("The sound contains invalid audio samples.");
                samples.Add(Math.Clamp(buffer[i], -1, 1));
            }
        }
        if (samples.Count == 0) throw new InvalidDataException("The sound file contains no audio.");
        return samples.ToArray();
    }

    internal static async Task<string> ImportAsync(string filename, string directory, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            string extension = Path.GetExtension(filename).ToLowerInvariant();
            if (extension is not (".wav" or ".mp3" or ".aif" or ".aiff"))
                throw new InvalidDataException("Choose a WAV, MP3 or AIFF sound.");
            if (new FileInfo(filename).Length > MaximumFileBytes) throw new InvalidDataException("Choose a sound smaller than 64 MB.");
            using var reader = new AudioFileReader(filename);
            float[] samples = Decode(reader, token);
            using var encoded = new MemoryStream();
            using (var writer = new WaveFileWriter(new IgnoreDisposeStream(encoded), WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)))
                writer.WriteSamples(samples, 0, samples.Length);
            byte[] bytes = encoded.ToArray();
            string name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".wav";
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, name);
            if (!File.Exists(destination))
            {
                string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    token.ThrowIfCancellationRequested();
                    File.Move(temporary, destination, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return name;
        }, token).ConfigureAwait(false);
    }

    internal static string ResolveSound(string directory, string name)
    {
        if (name == null || name.Length != 68 || !name.EndsWith(".wav", StringComparison.Ordinal) ||
            name[..64].Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Import a sound file for this alert first.");
        return Path.Combine(directory, name);
    }

    internal static float[] ReadSound(string name, CancellationToken token)
    {
        string path = ResolveSound(SoundDirectory, name);
        if (!File.Exists(path)) throw new FileNotFoundException("The imported sound is missing. Import it again.");
        if (new FileInfo(path).Length > MaximumFileBytes) throw new InvalidDataException("The imported sound is too large.");
        using var reader = new WaveFileReader(path);
        return Decode(reader.ToSampleProvider(), token);
    }

    internal static async Task PlayAsync(float[] samples, string endpointId, Func<string> controllerEndpoint,
        int volume, CancellationToken token)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (endpointId == "controller") endpointId = controllerEndpoint();
        using var device = string.IsNullOrEmpty(endpointId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(endpointId);
        using var stream = new RawSourceWaveStream(FloatBytes(samples), WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1));
        ISampleProvider source = stream.ToSampleProvider();
        var mix = device.AudioClient.MixFormat;
        if (mix.SampleRate != SampleRate) source = new WdlResamplingSampleProvider(source, mix.SampleRate);
        source = new BatteryAlertChannelProvider(source, mix.Channels,
            DualSenseAudioPassthrough.IsControllerAudioEndpoint(device));
        var gain = new VolumeSampleProvider(source) { Volume = volume / 100f };
        using var player = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackStopped += (_, args) =>
        {
            if (args.Exception != null) finished.TrySetException(args.Exception); else finished.TrySetResult();
        };
        player.Init(gain);
        token.ThrowIfCancellationRequested();
        player.Play();
        try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(samples.Length / (double)SampleRate + 5), token).ConfigureAwait(false); }
        finally { player.Stop(); }
    }

    private static MemoryStream FloatBytes(float[] samples)
    {
        byte[] bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new MemoryStream(bytes, writable: false);
    }

    private sealed class IgnoreDisposeStream : Stream
    {
        private readonly Stream inner;
        internal IgnoreDisposeStream(Stream inner) => this.inner = inner;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Flush() => inner.Flush();
    }
}

/// <summary>Never send an announcement into a controller's haptics channels.</summary>
internal sealed class BatteryAlertChannelProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly bool controller;
    private readonly float[] mono = new float[4096];
    public WaveFormat WaveFormat { get; }
    internal BatteryAlertChannelProvider(ISampleProvider source, int channels, bool controller)
    {
        if (source.WaveFormat.Channels != 1 || channels < 1) throw new ArgumentException("A mono alert source is required.");
        this.source = source;
        this.controller = controller;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, channels);
    }
    public int Read(float[] buffer, int offset, int count)
    {
        int channels = WaveFormat.Channels;
        int frames = source.Read(mono, 0, Math.Min(count / channels, mono.Length));
        Array.Clear(buffer, offset, frames * channels);
        for (int i = 0; i < frames; i++)
        {
            if (controller) buffer[offset + i * channels + (channels >= 4 ? 1 : 0)] = mono[i];
            else
            {
                buffer[offset + i * channels] = mono[i];
                if (channels > 1) buffer[offset + i * channels + 1] = mono[i];
            }
        }
        return frames * channels;
    }
}
