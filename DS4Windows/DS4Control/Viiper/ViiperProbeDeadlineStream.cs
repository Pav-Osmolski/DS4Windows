using System;
using System.IO;

namespace DS4Windows
{
    // Only readiness probes use this synchronous stream. Apply the remaining
    // monotonic budget beneath authentication and encrypted-record framing:
    // their ReadExactly loops must not restart a timeout for every peer byte.
    internal sealed class ViiperProbeDeadlineStream : Stream
    {
        private readonly Stream inner;
        private readonly TimeProvider clock;
        private readonly long started;
        private double deadlineMilliseconds;

        internal ViiperProbeDeadlineStream(Stream inner, long started,
            int timeoutMilliseconds, TimeProvider clock = null)
        {
            this.inner = inner;
            this.clock = clock ?? TimeProvider.System;
            this.started = started;
            deadlineMilliseconds = timeoutMilliseconds;
        }

        internal void LimitRemainingTime(int milliseconds)
            => deadlineMilliseconds = Math.Min(deadlineMilliseconds,
                ElapsedMilliseconds + milliseconds);

        private double ElapsedMilliseconds => clock.GetElapsedTime(started).TotalMilliseconds;

        private int RemainingMilliseconds()
        {
            double remaining = deadlineMilliseconds - ElapsedMilliseconds;
            if (remaining <= 0)
                throw new IOException("VIIPER readiness probe deadline expired.");
            return (int)Math.Ceiling(remaining);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            inner.ReadTimeout = Math.Min(1000, RemainingMilliseconds());
            int read = inner.Read(buffer);
            RemainingMilliseconds(); // Reject data returned after the deadline.
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.WriteTimeout = Math.Min(500, RemainingMilliseconds());
            inner.Write(buffer);
            RemainingMilliseconds();
        }

        public override void Flush()
        {
            RemainingMilliseconds();
            inner.Flush();
            RemainingMilliseconds();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
