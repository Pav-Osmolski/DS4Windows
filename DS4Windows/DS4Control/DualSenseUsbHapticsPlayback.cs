using System;
using System.Buffers.Binary;
using NAudio.Dsp;
using NAudio.Wave;

namespace DS4Windows;

// Native USB waveforms are the two rear channels of the virtual controller's
// four-channel Windows endpoint. They are not speaker audio or Audio Haptics.
internal sealed class DualSenseUsbHapticsPlayback
{
    private const int BlockFrames = 256;
    private readonly BufferedWaveProvider provider;
    private readonly WaveFormat source;
    private readonly WaveFormat target;
    private readonly WdlResampler resampler;
    private readonly float[] converted;
    private readonly byte[] output;
    private long received;
    private long produced;
    internal long SourceFrames => received;
    internal long NonzeroRearBlocks { get; private set; }

    internal DualSenseUsbHapticsPlayback(BufferedWaveProvider provider, WaveFormat captureFormat)
    {
        this.provider = provider;
        source = Standard(captureFormat);
        target = Standard(provider.WaveFormat);
        Validate(source);
        Validate(target);
        int capacity = checked((int)Math.Ceiling(
            (BlockFrames + 2.0) * target.SampleRate / source.SampleRate + 2));
        converted = new float[capacity * 2];
        output = new byte[capacity * target.BlockAlign];
        if (source.SampleRate != target.SampleRate)
        {
            resampler = new WdlResampler();
            resampler.SetMode(true, 2, false);
            resampler.SetFeedMode(true);
            resampler.SetRates(source.SampleRate, target.SampleRate);
        }
    }

    private static WaveFormat Standard(WaveFormat format) =>
        format is WaveFormatExtensible extensible ? extensible.AsStandardWaveFormat() : format;

    private static void Validate(WaveFormat format)
    {
        if (format.Channels != 4 || format.SampleRate < 8000 || format.SampleRate > 192000 ||
            !((format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32) ||
              (format.Encoding == WaveFormatEncoding.Pcm &&
               (format.BitsPerSample == 16 || format.BitsPerSample == 24 || format.BitsPerSample == 32))))
            throw new InvalidOperationException(
                "Native USB haptics requires four-channel PCM16/24/32 or float32 controller audio.");
    }

    internal void Write(byte[] input, int length)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (length < 0 || length > input.Length || length % source.BlockAlign != 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        int frames = length / source.BlockAlign;
        int bytesPerSample = source.BitsPerSample / 8;
        for (int offset = 0; offset < frames;)
        {
            int count = Math.Min(BlockFrames, frames - offset);
            float[] samples = converted;
            int sampleOffset = 0;
            if (resampler != null &&
                resampler.ResamplePrepare(count, 2, out samples, out sampleOffset) != count)
                throw new InvalidOperationException("The USB haptics resampler rejected its input block.");
            for (int frame = 0; frame < count; frame++)
            {
                int inputOffset = (offset + frame) * source.BlockAlign + 2 * bytesPerSample;
                samples[sampleOffset + frame * 2] = Read(input, inputOffset);
                samples[sampleOffset + frame * 2 + 1] = Read(input, inputOffset + bytesPerSample);
            }
            bool nonzero = false;
            for (int sample = 0; sample < count * 2; sample++)
                nonzero |= samples[sampleOffset + sample] != 0;
            if (nonzero) NonzeroRearBlocks++;
            received += count;
            int outputFrames = count;
            if (resampler != null)
            {
                long due = received * target.SampleRate / source.SampleRate;
                outputFrames = resampler.ResampleOut(converted, 0, count,
                    (int)Math.Min(converted.Length / 2, Math.Max(0, due - produced)), 2);
            }
            produced += outputFrames;
            int outputBytes = outputFrames * target.BlockAlign;
            Array.Clear(output, 0, outputBytes);
            for (int frame = 0; frame < outputFrames; frame++)
            {
                int outputOffset = frame * target.BlockAlign + target.BitsPerSample / 8 * 2;
                Store(outputOffset, converted[frame * 2]);
                Store(outputOffset + target.BitsPerSample / 8, converted[frame * 2 + 1]);
            }
            // A stalled render client must not accumulate seconds of old effects.
            // Clear whole interleaved frames before admitting the current block.
            if (provider.BufferedBytes + outputBytes > provider.BufferLength)
                provider.ClearBuffer();
            provider.AddSamples(output, 0, outputBytes);
            offset += count;
        }
    }

    private float Read(byte[] input, int offset)
    {
        float value = source.Encoding == WaveFormatEncoding.IeeeFloat
            ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(offset)))
            : source.BitsPerSample switch
            {
                16 => BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(offset)) / 32768f,
                24 => ((input[offset] | input[offset + 1] << 8 | input[offset + 2] << 16) << 8 >> 8) / 8388608f,
                _ => BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(offset)) / 2147483648f,
            };
        return float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
    }

    private void Store(int offset, float value)
    {
        value = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
        if (target.Encoding == WaveFormatEncoding.IeeeFloat)
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), BitConverter.SingleToInt32Bits(value));
        else if (target.BitsPerSample == 16)
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset), (short)Math.Clamp(value * 32768f, -32768, 32767));
        else if (target.BitsPerSample == 24)
        {
            int sample = (int)Math.Clamp(value * 8388608f, -8388608, 8388607);
            output[offset] = (byte)sample;
            output[offset + 1] = (byte)(sample >> 8);
            output[offset + 2] = (byte)(sample >> 16);
        }
        else
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset),
                (int)Math.Clamp((double)value * 2147483648, int.MinValue, int.MaxValue));
    }
}
