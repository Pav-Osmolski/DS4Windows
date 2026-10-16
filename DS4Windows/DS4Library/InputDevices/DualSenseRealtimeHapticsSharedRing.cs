using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace DS4Windows.InputDevices
{
    /// <summary>
    /// One single-producer/single-consumer ring shared by the main
    /// process and the isolated physical HID writer. A slot remains owned by
    /// the consumer until the corresponding HID write is accepted, so retrying
    /// a briefly saturated write cannot duplicate or reorder tactile media.
    /// Sustained overload retires obsolete media, never controller commands.
    /// </summary>
    internal sealed class DualSenseRealtimeHapticsSharedRing : IDisposable
    {
        internal const int Version = 2;
        internal const int DefaultCapacity = 64;
        internal const int PayloadLength = 64;
        internal const int MaximumLatencyMilliseconds = 250;
        internal const int RecoveryTargetMilliseconds = 50;
        private static readonly long MaximumLatencyTicks =
            Stopwatch.Frequency * MaximumLatencyMilliseconds / 1000;
        private static readonly long RecoveryTargetTicks =
            Stopwatch.Frequency * RecoveryTargetMilliseconds / 1000;

        private const int Magic = 0x48523444; // "D4RH"
        private const long MagicOffset = 0;
        private const long VersionOffset = 4;
        private const long CapacityOffset = 8;
        private const long PayloadLengthOffset = 12;
        private const long WriteSequenceOffset = 16;
        private const long ReadSequenceOffset = 24;
        // Previously reserved header space; the slot layout and protocol stay
        // compatible. Each counter has exactly one process writing it.
        private const long RetiredBlocksOffset = 32;
        private const long RecoveriesOffset = 40;
        private const long RejectedOldInputOffset = 48;
        private const long ProducerWaitMaximumTicksOffset = 56;
        private const int HeaderLength = 64;

        private const int SlotPublishedSequenceOffset = 0;
        private const int SlotEnqueuedQpcOffset = 8;
        private const int SlotExpiryQpcOffset = 16;
        private const int SlotGenerationOffset = 24;
        private const int SlotPayloadOffset = 32;
        private const int SlotStride = SlotPayloadOffset + PayloadLength;

        private readonly MemoryMappedFile map;
        private readonly MemoryMappedViewAccessor view;
        private readonly EventWaitHandle spaceAvailable;
        private readonly EventWaitHandle dataAvailable;
        private readonly EventWaitHandle stopRequested;
        private readonly WaitHandle[] producerWaitHandles;
        private readonly object producerLock = new object();
        private readonly object consumerLock = new object();
        private readonly byte[] preparedPayload = new byte[PayloadLength];
        private readonly int capacity;
        private bool prepared;
        private long preparedSequence;
        private long preparedEnqueuedQpc;
        private long preparedExpiryQpc;
        private bool recovering;
        private long committedSequence;
        private long committedEnqueuedQpc;
        private bool hasReceivedGeneration;
        private int acceptedGeneration = 1;
        private int maximumQueueDepth;
        private long maximumQueueAgeTicks;
        private long presentedCount;
        private int disposed;

        private DualSenseRealtimeHapticsSharedRing(string mapName,
            string spaceAvailableName, string stopRequestedName,
            bool create, int capacity)
        {
            if (string.IsNullOrWhiteSpace(mapName))
            {
                throw new ArgumentException("A shared-ring map name is required.",
                    nameof(mapName));
            }
            if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    "Shared-ring capacity must be a power of two.");
            }

            MapName = mapName;
            SpaceAvailableName = spaceAvailableName;
            StopRequestedName = stopRequestedName;
            this.capacity = capacity;
            long mapLength = HeaderLength + (long)capacity * SlotStride;

            if (create)
            {
                map = MemoryMappedFile.CreateNew(mapName, mapLength,
                    MemoryMappedFileAccess.ReadWrite);
                spaceAvailable = new EventWaitHandle(false,
                    EventResetMode.AutoReset, spaceAvailableName);
                dataAvailable = new EventWaitHandle(false,
                    EventResetMode.AutoReset, mapName + ".Data");
                stopRequested = new EventWaitHandle(false,
                    EventResetMode.ManualReset, stopRequestedName);
            }
            else
            {
                map = MemoryMappedFile.OpenExisting(mapName,
                    MemoryMappedFileRights.ReadWrite);
                spaceAvailable = EventWaitHandle.OpenExisting(
                    spaceAvailableName);
                dataAvailable = EventWaitHandle.OpenExisting(mapName + ".Data");
                stopRequested = EventWaitHandle.OpenExisting(
                    stopRequestedName);
            }

            view = map.CreateViewAccessor(0, mapLength,
                MemoryMappedFileAccess.ReadWrite);
            producerWaitHandles = new WaitHandle[]
            {
                spaceAvailable,
                stopRequested,
            };

            if (create)
            {
                view.Write(MagicOffset, Magic);
                view.Write(VersionOffset, Version);
                view.Write(CapacityOffset, capacity);
                view.Write(PayloadLengthOffset, PayloadLength);
                view.Write(WriteSequenceOffset, 0L);
                view.Write(ReadSequenceOffset, 0L);
                view.Flush();
            }
            else
            {
                ValidateHeader(capacity);
            }
        }

        internal string MapName { get; }
        internal string SpaceAvailableName { get; }
        internal string StopRequestedName { get; }
        internal int Capacity => capacity;
        internal WaitHandle DataAvailableSignal => dataAvailable;
        internal WaitHandle StopRequestedSignal => stopRequested;
        internal int Count
        {
            get
            {
                long depth = Math.Max(0,
                    view.ReadInt64(WriteSequenceOffset) -
                    view.ReadInt64(ReadSequenceOffset));
                return (int)Math.Min(capacity, depth);
            }
        }
        internal bool HasPreparedGeneration => prepared;
        // One-based publication identities, not payload/nonzero heuristics.
        // A prepared slot has already passed the accepted-generation check.
        internal long PublishedSequence
        {
            get
            {
                long sequence = view.ReadInt64(WriteSequenceOffset);
                Thread.MemoryBarrier();
                return sequence;
            }
        }
        internal long PreparedSequence
        {
            get { lock (consumerLock) return prepared ? preparedSequence + 1 : 0; }
        }
        internal int MaximumQueueDepth => maximumQueueDepth;
        internal long MaximumQueueAgeTicks => maximumQueueAgeTicks;
        internal long RetiredBlocks => ReadDiagnosticCounter(RetiredBlocksOffset) +
            ReadDiagnosticCounter(RejectedOldInputOffset);
        internal long RecoveryCount => ReadDiagnosticCounter(RecoveriesOffset);
        internal long ProducerWaitMaximumTicks =>
            ReadDiagnosticCounter(ProducerWaitMaximumTicksOffset);
        internal long PresentedCount => presentedCount;

        internal static DualSenseRealtimeHapticsSharedRing CreateOwner(
            string namePrefix, int capacity = DefaultCapacity)
        {
            return new DualSenseRealtimeHapticsSharedRing(
                namePrefix + ".RealtimeHaptics",
                namePrefix + ".RealtimeHaptics.Space",
                namePrefix + ".RealtimeHaptics.Stop",
                create: true, capacity);
        }

        internal static DualSenseRealtimeHapticsSharedRing OpenConsumer(
            string mapName, string spaceAvailableName,
            string stopRequestedName, int capacity)
        {
            return new DualSenseRealtimeHapticsSharedRing(mapName,
                spaceAvailableName, stopRequestedName, create: false,
                capacity);
        }

        internal bool Publish(byte[] source, int offset, int generation,
            long expiryQpc, long enqueuedAtQpc)
        {
            if (source == null || offset < 0 ||
                offset + PayloadLength > source.Length)
            {
                throw new ArgumentException(
                    "Realtime haptics source does not contain one complete generation.",
                    nameof(source));
            }

            long waitStarted = 0;
            while (Volatile.Read(ref disposed) == 0 &&
                !stopRequested.WaitOne(0))
            {
                lock (producerLock)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (waitStarted != 0)
                        view.Write(ProducerWaitMaximumTicksOffset, Math.Max(
                            view.ReadInt64(ProducerWaitMaximumTicksOffset),
                            now - waitStarted));
                    if (waitStarted != 0 && now - waitStarted > MaximumLatencyTicks)
                    {
                        view.Write(RejectedOldInputOffset,
                            view.ReadInt64(RejectedOldInputOffset) + 1);
                        // Accepted disposition: retrying these obsolete bytes
                        // would move their timestamp forward and defeat recovery.
                        return true;
                    }
                    long writeSequence = view.ReadInt64(WriteSequenceOffset);
                    long readSequence = view.ReadInt64(ReadSequenceOffset);
                    if (writeSequence - readSequence < capacity)
                    {
                        int slotIndex = (int)(writeSequence & (capacity - 1));
                        long slotOffset = HeaderLength +
                            (long)slotIndex * SlotStride;
                        view.Write(slotOffset + SlotEnqueuedQpcOffset,
                            enqueuedAtQpc);
                        view.Write(slotOffset + SlotExpiryQpcOffset,
                            expiryQpc);
                        view.Write(slotOffset + SlotGenerationOffset,
                            generation);
                        view.WriteArray(slotOffset + SlotPayloadOffset,
                            source, offset, PayloadLength);
                        Thread.MemoryBarrier();
                        view.Write(slotOffset + SlotPublishedSequenceOffset,
                            writeSequence + 1);
                        Thread.MemoryBarrier();
                        view.Write(WriteSequenceOffset, writeSequence + 1);
                        // This is a distinct consumer notification. Sharing the
                        // producer's space event would steal its wake/credit.
                        dataAvailable.Set();
                        return true;
                    }
                }

                if (waitStarted == 0) waitStarted = Stopwatch.GetTimestamp();
                // Keep short interruptions lossless, but do not block a
                // source indefinitely behind media which is already too old.
                if (WaitHandle.WaitAny(producerWaitHandles, 20) == 1)
                {
                    return false;
                }
            }

            return false;
        }

        internal bool PrepareForPresentation(byte[] report, long nowQpc)
        {
            if (report == null ||
                DualSenseBluetoothAudioPacer.RealtimeHapticsDataOffset +
                    PayloadLength > report.Length)
            {
                throw new ArgumentException(
                    "Physical report cannot carry one realtime haptics generation.",
                    nameof(report));
            }

            lock (consumerLock)
            {
                if (TryPrepareLocked(nowQpc, out bool empty))
                {
                    ApplyPrepared(report);
                    return true;
                }

                if ((empty || recovering) && hasReceivedGeneration)
                    Silence(report);
                return false;
            }
        }

        // Bind without consuming a ring slot or touching a front-audio report.
        // The presenter can then defer an empty rear lane before claiming any
        // speaker packet or ordered controller-state command.
        internal bool TryPrepareCurrentGeneration(long nowQpc, out bool empty,
            out long lastCommittedSequence, out long lastCommittedEnqueuedQpc)
        {
            lock (consumerLock)
            {
                bool ready = TryPrepareLocked(nowQpc, out empty);
                lastCommittedSequence = committedSequence;
                lastCommittedEnqueuedQpc = committedEnqueuedQpc;
                return ready;
            }
        }

        private bool TryPrepareLocked(long nowQpc, out bool empty)
        {
            empty = false;
            if (prepared)
            {
                // Prebinding may precede HID credit. Include that wait
                // when the final presentation reapplies this same block.
                if (preparedEnqueuedQpc > 0)
                    maximumQueueAgeTicks = Math.Max(maximumQueueAgeTicks,
                        Math.Max(0, nowQpc - preparedEnqueuedQpc));
                if (!IsTooOld(nowQpc, preparedEnqueuedQpc, MaximumLatencyTicks) &&
                    !IsExpired(nowQpc, preparedExpiryQpc))
                    return true;
                BeginRecovery();
                Retire(preparedSequence);
                prepared = false;
                Array.Clear(preparedPayload, 0, preparedPayload.Length);
            }

            // A continuously publishing peer cannot monopolize this thread.
            for (int scanned = 0; scanned <= capacity; scanned++)
            {
                long readSequence = view.ReadInt64(ReadSequenceOffset);
                long writeSequence = view.ReadInt64(WriteSequenceOffset);
                int depth = (int)Math.Min(capacity,
                    Math.Max(0, writeSequence - readSequence));
                if (depth > maximumQueueDepth)
                {
                    maximumQueueDepth = depth;
                }
                if (readSequence >= writeSequence)
                {
                    recovering = false;
                    empty = true;
                    return false;
                }

                int slotIndex = (int)(readSequence & (capacity - 1));
                long slotOffset = HeaderLength +
                    (long)slotIndex * SlotStride;
                Thread.MemoryBarrier();
                if (view.ReadInt64(slotOffset +
                        SlotPublishedSequenceOffset) != readSequence + 1)
                {
                    return false;
                }

                int generation = view.ReadInt32(slotOffset +
                    SlotGenerationOffset);
                int generationDelta = unchecked(generation -
                    acceptedGeneration);
                if (generationDelta > 0)
                {
                    // A lifecycle command for this generation is already
                    // in the ordered pipe but has not been applied yet.
                    return false;
                }
                if (generationDelta < 0)
                {
                    AdvanceRead(readSequence);
                    continue;
                }

                long queuedAt = view.ReadInt64(slotOffset +
                    SlotEnqueuedQpcOffset);
                long expiry = view.ReadInt64(slotOffset + SlotExpiryQpcOffset);
                if (queuedAt > 0)
                {
                    maximumQueueAgeTicks = Math.Max(
                        maximumQueueAgeTicks,
                        Math.Max(0, nowQpc - queuedAt));
                }
                if (IsTooOld(nowQpc, queuedAt, MaximumLatencyTicks) ||
                    IsExpired(nowQpc, expiry))
                    BeginRecovery();
                if (recovering && (IsTooOld(nowQpc, queuedAt, RecoveryTargetTicks) ||
                    IsExpired(nowQpc, expiry)))
                {
                    Retire(readSequence);
                    continue;
                }
                recovering = false;
                view.ReadArray(slotOffset + SlotPayloadOffset,
                    preparedPayload, 0, PayloadLength);
                preparedSequence = readSequence;
                preparedEnqueuedQpc = queuedAt;
                preparedExpiryQpc = expiry;
                prepared = true;
                hasReceivedGeneration = true;
                return true;
            }
            empty = true;
            return false;
        }

        private long ReadDiagnosticCounter(long offset)
        {
            if (Volatile.Read(ref disposed) != 0) return 0;
            try { return view.ReadInt64(offset); }
            catch (ObjectDisposedException) { return 0; }
        }

        private static bool IsTooOld(long now, long queuedAt, long budget) =>
            queuedAt > 0 && now >= queuedAt && now - queuedAt > budget;

        private static bool IsExpired(long now, long expiry) =>
            expiry > 0 && expiry != long.MaxValue && now > expiry;

        private void BeginRecovery()
        {
            if (!recovering)
                view.Write(RecoveriesOffset, view.ReadInt64(RecoveriesOffset) + 1);
            recovering = true;
            hasReceivedGeneration = true;
        }

        private void Retire(long sequence)
        {
            view.Write(RetiredBlocksOffset, view.ReadInt64(RetiredBlocksOffset) + 1);
            AdvanceRead(sequence);
        }

        internal void CommitPrepared()
        {
            lock (consumerLock)
            {
                if (!prepared)
                {
                    return;
                }

                AdvanceRead(preparedSequence);
                committedSequence = preparedSequence + 1;
                committedEnqueuedQpc = preparedEnqueuedQpc;
                Array.Clear(preparedPayload, 0, preparedPayload.Length);
                prepared = false;
                presentedCount++;
            }
        }

        internal void AcceptGeneration(int generation,
            bool silenceFutureReports)
        {
            lock (consumerLock)
            {
                acceptedGeneration = generation;
                prepared = false;
                preparedEnqueuedQpc = 0;
                preparedExpiryQpc = 0;
                recovering = false;
                committedSequence = 0;
                committedEnqueuedQpc = 0;
                Array.Clear(preparedPayload, 0, preparedPayload.Length);
                hasReceivedGeneration = silenceFutureReports;
            }
        }

        internal void RequestStop()
        {
            try { stopRequested.Set(); } catch { }
        }

        private void ApplyPrepared(byte[] report)
        {
            int offset = DualSenseBluetoothAudioPacer.
                RealtimeHapticsDataOffset;
            report[offset - 2] = 0x92;
            report[offset - 1] = PayloadLength;
            Buffer.BlockCopy(preparedPayload, 0, report, offset,
                PayloadLength);
        }

        private static void Silence(byte[] report)
        {
            int offset = DualSenseBluetoothAudioPacer.
                RealtimeHapticsDataOffset;
            report[offset - 2] = 0x92;
            report[offset - 1] = PayloadLength;
            Array.Clear(report, offset, PayloadLength);
        }

        private void AdvanceRead(long readSequence)
        {
            Thread.MemoryBarrier();
            view.Write(ReadSequenceOffset, readSequence + 1);
            Thread.MemoryBarrier();
            spaceAvailable.Set();
        }

        private void ValidateHeader(int expectedCapacity)
        {
            if (view.ReadInt32(MagicOffset) != Magic ||
                view.ReadInt32(VersionOffset) != Version ||
                view.ReadInt32(CapacityOffset) != expectedCapacity ||
                view.ReadInt32(PayloadLengthOffset) != PayloadLength)
            {
                throw new InvalidDataException(
                    "The realtime haptics shared-ring header is invalid.");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            RequestStop();
            view.Dispose();
            map.Dispose();
            spaceAvailable.Dispose();
            dataAvailable.Dispose();
            stopRequested.Dispose();
        }
    }
}
