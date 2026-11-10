using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DS4Windows;

// Diagnostic preview only. Copy a small value record into a bounded queue so
// transport threads never format strings or call the UI logger.
internal sealed class Issue123UsbTriggerTrace
{
    private static readonly Lazy<Channel<TraceEvent>> events = new(CreateChannel);
    private static long nextId;
    private static int dropped;

    private TriggerUpdate previousIngress;
    private long previousStream;
    private long previousFullId;
    private long previousFailedWriteId;
    private long lastCompletedId;
    private TriggerUpdate previousLocalWrite;

    internal long Received(ReadOnlySpan<byte> report, long stream, int slot)
    {
        var update = TriggerUpdate.Read(report);
        if (update.Flags == 0 || (previousStream == stream && update == previousIngress))
            return 0;

        previousIngress = update;
        previousStream = stream;
        long id = Interlocked.Increment(ref nextId);
        Record("VIIPER received", id, 0, report, stream, slot);
        return id;
    }

    internal void QueueFull(long id)
    {
        if (id == 0 || Interlocked.Exchange(ref previousFullId, id) == id) return;
        Record("physical queue full", id, 0);
    }

    internal void UsbWrite(long id, long revision, ReadOnlySpan<byte> report,
        long queuedAt, long writeStarted, bool written)
    {
        if (!written && previousFailedWriteId == id) return;
        previousFailedWriteId = written ? 0 : id;
        if (written)
        {
            lastCompletedId = id;
            previousLocalWrite = default;
        }
        Record(written ? "USB write completed" : "USB write failed", id, revision,
            report, queueTicks: writeStarted - queuedAt,
            writeTicks: Stopwatch.GetTimestamp() - writeStarted);
    }

    internal void LocalWrite(ReadOnlySpan<byte> report)
    {
        if (lastCompletedId == 0 || !Global.VerboseStartupLogging) return;
        var update = TriggerUpdate.Read(report);
        if (update.Flags == 0 || update == previousLocalWrite) return;
        previousLocalWrite = update;
        Record("local USB trigger write after native command", lastCompletedId, 0, report);
    }

    internal static void Record(string stage, long id, long revision,
        ReadOnlySpan<byte> report = default, long stream = 0, int slot = -1,
        int depth = -1, long queueTicks = 0, long writeTicks = 0, byte profileMask = 0)
    {
        if (id == 0 || !Global.VerboseStartupLogging) return;
        var item = new TraceEvent(stage, id, revision, Stopwatch.GetTimestamp(), stream, slot,
            depth, queueTicks, writeTicks, profileMask, TriggerUpdate.Read(report));
        if (!events.Value.Writer.TryWrite(item)) Interlocked.Increment(ref dropped);
    }

    private static Channel<TraceEvent> CreateChannel()
    {
        var channel = Channel.CreateBounded<TraceEvent>(
            new BoundedChannelOptions(512) { SingleReader = true });
        _ = Task.Run(() => DrainAsync(channel.Reader));
        return channel;
    }

    private static async Task DrainAsync(ChannelReader<TraceEvent> reader)
    {
        await foreach (var item in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                int lost = Interlocked.Exchange(ref dropped, 0);
                if (lost != 0)
                    AppLogger.LogToGui($"[Issue123 USB triggers] dropped={lost} (trace buffer full)", false);
                AppLogger.LogToGui(item.Format(), false);
            }
            catch (Exception ex) { Trace.WriteLine(ex.Message); }
        }
    }

    internal readonly record struct Effect(ulong FirstEightBytes, uint LastThreeBytes)
    {
        internal static Effect Read(ReadOnlySpan<byte> bytes) => new(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            (uint)(bytes[8] | bytes[9] << 8 | bytes[10] << 16));

        public override string ToString() => $"{(byte)FirstEightBytes:X2}/{FirstEightBytes >> 8:X14}/{LastThreeBytes:X6}";
    }

    internal readonly record struct TriggerUpdate(byte Flags, Effect Right, Effect Left)
    {
        internal static TriggerUpdate Read(ReadOnlySpan<byte> report)
        {
            if (report.Length < 48 || report[0] != 0x02) return default;

            byte flags = (byte)(report[1] & 0x0C);
            return new(flags,
                (flags & 0x04) != 0 ? Effect.Read(report.Slice(11, 11)) : default,
                (flags & 0x08) != 0 ? Effect.Read(report.Slice(22, 11)) : default);
        }

        public override string ToString() =>
            $"triggerFlags=0x{Flags:X2} R2={((Flags & 0x04) != 0 ? Right.ToString() : "-")} L2={((Flags & 0x08) != 0 ? Left.ToString() : "-")}";
    }

    private readonly record struct TraceEvent(string Stage, long Id, long Revision, long Qpc,
        long Stream, int Slot, int Depth, long QueueTicks, long WriteTicks, byte ProfileMask,
        TriggerUpdate Update)
    {
        internal string Format() => FormattableString.Invariant(
            $"[Issue123 USB triggers] {Stage} id={Id} revision={Revision} qpc={Qpc} qpcHz={Stopwatch.Frequency} stream={Stream} controller={Slot + 1} depth={Depth} queueMs={Ms(QueueTicks):F2} writeMs={Ms(WriteTicks):F2} profileMask=0x{ProfileMask:X2} {Update}");

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}