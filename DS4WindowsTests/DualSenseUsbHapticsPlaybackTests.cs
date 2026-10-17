using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace DS4Windows.Tests;

[TestClass]
public sealed class DualSenseUsbHapticsPlaybackTests
{
    [TestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    [DataRow(0)]
    public void NativeUsbKeepsRearChannelsDistinctAndNeverPlaysThemThroughSpeaker(int bits)
    {
        var source = Format(bits);
        var target = WaveFormat.CreateIeeeFloatWaveFormat(48000, 4);
        var provider = Provider(target);
        var playback = new DualSenseUsbHapticsPlayback(provider, source);
        byte[] input = Samples(source, 480, (f, c) => c switch
        { 0 => .9f, 1 => -.8f, 2 => .25f, _ => -.5f });
        playback.Write(input, input.Length);
        byte[] result = Drain(provider);
        Assert.AreEqual(480 * 16, result.Length);
        for (int frame = 0; frame < 480; frame++)
        {
            Assert.AreEqual(0f, Float(result, frame, 0));
            Assert.AreEqual(0f, Float(result, frame, 1));
            Assert.AreEqual(.25f, Float(result, frame, 2), .00004f);
            Assert.AreEqual(-.5f, Float(result, frame, 3), .00004f);
        }
        Assert.AreEqual(480L, playback.SourceFrames);
        Assert.IsTrue(playback.NonzeroRearBlocks > 0);
    }

    [TestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    [DataRow(0)]
    public void OutputFormatsRetainPolarityAndAuthoritativeSilence(int bits)
    {
        var source = Format(0);
        var target = Format(bits);
        var provider = Provider(target);
        var playback = new DualSenseUsbHapticsPlayback(provider, source);
        byte[] impulse = Samples(source, 1, (_, c) => c == 2 ? -.5f : c == 3 ? .25f : 1f);
        playback.Write(impulse, impulse.Length);
        byte[] first = Drain(provider);
        Assert.IsTrue(first.Take(2 * target.BitsPerSample / 8).All(b => b == 0));
        int offset = 2 * target.BitsPerSample / 8;
        Assert.IsTrue((first[offset + target.BitsPerSample / 8 - 1] & 0x80) != 0,
            "The negative left waveform lost its sign.");
        byte[] silence = Samples(source, 480, (_, c) => c < 2 ? 1f : 0f);
        playback.Write(silence, silence.Length);
        Assert.IsTrue(Drain(provider).All(b => b == 0),
            "An authored stop must not hold the previous waveform or turn speaker sound into vibration.");
    }

    [TestMethod]
    [DataRow(32000)]
    [DataRow(44100)]
    [DataRow(96000)]
    public void ResamplingPreservesDurationAndPhaseAcrossCaptureCallbacks(int rate)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(rate, 4);
        byte[] input = Samples(format, rate + 31,
            (f, c) => c == 2 ? (float)Math.Sin(f * .025) * .4f :
                c == 3 ? (float)Math.Cos(f * .013) * .2f : 1);
        byte[] Whole(int[] chunks)
        {
            var p = Provider(Format(0), seconds: 3);
            var playback = new DualSenseUsbHapticsPlayback(p, format);
            int offset = 0, index = 0;
            while (offset < input.Length)
            {
                int length = Math.Min(input.Length - offset, chunks[index++ % chunks.Length] * format.BlockAlign);
                playback.Write(input.AsSpan(offset, length).ToArray(), length);
                offset += length;
            }
            return Drain(p);
        }
        byte[] whole = Whole(new[] { rate + 31 });
        byte[] split = Whole(new[] { 1, 7, 511, 19, 260, 13 });
        Assert.AreEqual(whole.Length, split.Length);
        long expected = (long)(rate + 31) * 48000 / rate;
        Assert.IsTrue(whole.Length / 16 >= expected - 2 && whole.Length / 16 <= expected);
        for (int i = 0; i < whole.Length; i += 4)
            Assert.AreEqual(BitConverter.ToSingle(whole, i), BitConverter.ToSingle(split, i), .00002f);
    }

    [TestMethod]
    public void AFullQueueDiscardsOldEffectsInsteadOfReplayingThemAfterAStop()
    {
        var format = Format(0);
        var p = Provider(format, .1);
        var playback = new DualSenseUsbHapticsPlayback(p, format);
        var effect = Samples(format, 4800, (_, c) => c >= 2 ? .5f : 0);
        playback.Write(effect, effect.Length);
        var stop = new byte[480 * format.BlockAlign];
        playback.Write(stop, stop.Length);
        Assert.IsTrue(p.BufferedBytes <= stop.Length);
        Assert.IsTrue(Drain(p).All(b => b == 0));
    }

    [TestMethod]
    public void StereoCaptureIsRejectedRatherThanMistakenForNativeHaptics()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new DualSenseUsbHapticsPlayback(Provider(Format(0)),
                WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new DualSenseUsbHapticsPlayback(Provider(
                WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)), Format(0)));
    }

    [TestMethod]
    public void NonfiniteSamplesCannotPoisonThePhysicalAudioStream()
    {
        var format = Format(0);
        var p = Provider(format);
        var playback = new DualSenseUsbHapticsPlayback(p, format);
        byte[] input = Samples(format, 2, (_, c) => c == 2 ? float.NaN : float.PositiveInfinity);
        playback.Write(input, input.Length);
        Assert.IsTrue(Drain(p).All(b => b == 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => playback.Write(input, input.Length - 1));
    }

    [TestMethod]
    public void AutomaticRoutingRequiresTheSameRealControllerContainer()
    {
        Guid one = Guid.NewGuid(), other = Guid.NewGuid();
        Assert.IsTrue(DualSenseUsbHapticsPassthrough.SamePhysicalContainer(one, one));
        Assert.IsFalse(DualSenseUsbHapticsPassthrough.SamePhysicalContainer(one, other));
        Assert.IsFalse(DualSenseUsbHapticsPassthrough.SamePhysicalContainer(Guid.Empty, Guid.Empty));
        Assert.IsFalse(DualSenseUsbHapticsPassthrough.SamePhysicalContainer(
            HidHideDeviceIdentity.SystemContainerId, HidHideDeviceIdentity.SystemContainerId));
    }

    private static WaveFormat Format(int bits) => bits == 0
        ? WaveFormat.CreateIeeeFloatWaveFormat(48000, 4) : new WaveFormat(48000, bits, 4);
    private static BufferedWaveProvider Provider(WaveFormat format, double seconds = 1) =>
        new(format) { BufferDuration = TimeSpan.FromSeconds(seconds), ReadFully = false };
    private static byte[] Drain(BufferedWaveProvider p)
    {
        var data = new byte[p.BufferedBytes];
        Assert.AreEqual(data.Length, p.Read(data, 0, data.Length));
        return data;
    }
    private static float Float(byte[] data, int frame, int channel) =>
        BitConverter.ToSingle(data, frame * 16 + channel * 4);
    private static byte[] Samples(WaveFormat format, int frames, Func<int, int, float> value)
    {
        byte[] data = new byte[frames * format.BlockAlign];
        int bytes = format.BitsPerSample / 8;
        for (int f = 0; f < frames; f++)
        for (int c = 0; c < 4; c++)
        {
            int offset = f * format.BlockAlign + c * bytes;
            float v = value(f, c);
            if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), BitConverter.SingleToInt32Bits(v));
            else if (bytes == 2)
                BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset), (short)(v * 32767));
            else
            {
                int sample = (int)((double)v * (bytes == 3 ? 8388607 : 2147483647));
                for (int b = 0; b < bytes; b++) data[offset + b] = (byte)(sample >> (8 * b));
            }
        }
        return data;
    }
}
