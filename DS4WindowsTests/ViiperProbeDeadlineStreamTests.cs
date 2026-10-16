namespace DS4Windows.Tests;

[TestClass]
public class ViiperProbeDeadlineStreamTests
{
    [TestMethod]
    public void TrickledReadsUseOneBudgetWithoutWaitingForTimerCallbacks()
    {
        ManualClock clock = new();
        using TrickleStream wire = new(clock);
        using ViiperProbeDeadlineStream stream = new(wire, clock.GetTimestamp(), 500, clock);
        byte[] reply = new byte[35];

        Assert.Throws<IOException>(() => stream.ReadExactly(reply));
        CollectionAssert.AreEqual(new[] { 500, 425, 350, 275, 200, 125, 50 }, wire.ReadBudgets);
        Assert.AreEqual(7, wire.ReadCalls);
    }

    [TestMethod]
    public void EncryptedRecordReadsCannotRestartThePingBudget()
    {
        ManualClock clock = new();
        using TrickleStream wire = new(clock);
        using ViiperProbeDeadlineStream stream = new(wire, clock.GetTimestamp(), 3000, clock);
        clock.Advance(250);
        stream.LimitRemainingTime(1000);
        byte[] reply = new byte[64];

        Assert.Throws<IOException>(() => stream.ReadExactly(reply));
        Assert.AreEqual(1000, wire.ReadBudgets[0]);
        Assert.IsLessThan(100, wire.ReadBudgets[^1]);
        Assert.AreEqual(14, wire.ReadCalls);
    }

    [TestMethod]
    public void ConnectionTimeCountsAndExpiredProbeDoesNotWrite()
    {
        ManualClock clock = new();
        long started = clock.GetTimestamp();
        clock.Advance(500);
        using TrickleStream wire = new(clock);
        using ViiperProbeDeadlineStream stream = new(wire, started, 500, clock);

        Assert.Throws<IOException>(() => stream.Write(new byte[5]));
        Assert.AreEqual(0, wire.WriteCalls);
    }

    [TestMethod]
    public void LastReadArrivingAfterDeadlineCannotReportSuccess()
    {
        ManualClock clock = new();
        using TrickleStream wire = new(clock);
        using ViiperProbeDeadlineStream stream = new(wire, clock.GetTimestamp(), 50, clock);
        Assert.Throws<IOException>(() => stream.Read(new byte[1], 0, 1));
        Assert.AreEqual(1, wire.ReadCalls);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        internal void Advance(int amount) => milliseconds += amount;
    }

    // No timers run in this fixture. Each successful byte advances monotonic
    // time, reproducing progress while a cancellation callback cannot execute.
    private sealed class TrickleStream(ManualClock clock) : Stream
    {
        internal List<int> ReadBudgets { get; } = new();
        internal int ReadCalls { get; private set; }
        internal int WriteCalls { get; private set; }
        public override int ReadTimeout { get; set; }
        public override int WriteTimeout { get; set; }
        public override int Read(Span<byte> buffer)
        {
            ReadCalls++;
            ReadBudgets.Add(ReadTimeout);
            clock.Advance(75);
            buffer[0] = 1;
            return 1;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override void Write(byte[] buffer, int offset, int count) => WriteCalls++;
        public override void Write(ReadOnlySpan<byte> buffer) => WriteCalls++;
        public override void Flush() { }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
