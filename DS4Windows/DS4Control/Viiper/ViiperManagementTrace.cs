using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace DS4Windows
{
    // Cold-path transport metadata only. Never retain request/reply contents,
    // paths, exception messages, keys or authorized-device capabilities.
    internal sealed class ViiperManagementTrace
    {
        private static long nextId;
        private readonly long id = Interlocked.Increment(ref nextId);
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly string operation;
        private readonly Action<string> record;
        private int localPort, remotePort;
        internal string Phase { get; set; } = "Connect";
        internal long Sent, Received, PlaintextReceived;
        internal bool ReplyComplete;

        internal ViiperManagementTrace(string path, Action<string> record = null)
        {
            // Classify known operations; never log the caller's raw path.
            operation = path switch
            {
                "bus/create" => "bus-create",
                "bus/remove" => "bus-remove",
                "ping" => "ping",
                _ => ClassifyDeviceOperation(path),
            };
            this.record = record;
        }

        private static string ClassifyDeviceOperation(string path)
        {
            string[] parts = (path ?? "").Split('/');
            if (parts.Length != 3 || parts[0] != "bus" ||
                !int.TryParse(parts[1], out _)) return "other-management";
            return parts[2] switch
            {
                "add" => "device-add",
                "add-authorized-xboxone" => "device-add-authorized",
                "remove" => "device-remove",
                _ => "other-management",
            };
        }

        internal void Connected(TcpClient tcp)
        {
            if (tcp.Client.LocalEndPoint is IPEndPoint local) localPort = local.Port;
            if (tcp.Client.RemoteEndPoint is IPEndPoint remote) remotePort = remote.Port;
        }

        internal Stream Observe(Stream inner) => new CountingStream(inner, this);

        internal void Finish(bool success, Exception error = null)
        {
            // Diagnostics must not mask the original failure or affect startup.
            try
            {
                if (success && record == null && !Global.VerboseStartupLogging) return;
                SocketException socket = null;
                for (Exception inner = error; inner != null; inner = inner.InnerException)
                    if (inner is SocketException candidate) { socket = candidate; break; }
                string line = FormattableString.Invariant(
                    $"VIIPER management: id={id} operation={operation} phase={Phase} success={success} localPort={localPort} remotePort={remotePort} elapsedMs={elapsed.Elapsed.TotalMilliseconds:F2} wireSent={Sent} wireReceived={Received} plaintextReceived={PlaintextReceived} replyComplete={ReplyComplete} error={error?.GetType().Name ?? "none"} hresult=0x{(error?.HResult ?? 0):X8} socketError={socket?.SocketErrorCode.ToString() ?? "none"} nativeError={(socket?.NativeErrorCode ?? 0)}");
                if (record != null) record(line);
                else AppLogger.LogToGui(line, false);
            }
            catch { /* A diagnostic sink cannot change connection behaviour. */ }
        }

        private sealed class CountingStream(Stream inner, ViiperManagementTrace trace) : Stream
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = inner.Read(buffer, offset, count);
                trace.Received += read;
                return read;
            }
            public override int Read(Span<byte> buffer)
            {
                int read = inner.Read(buffer);
                trace.Received += read;
                return read;
            }
            public override void Write(byte[] buffer, int offset, int count)
            {
                inner.Write(buffer, offset, count);
                trace.Sent += count;
            }
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                inner.Write(buffer);
                trace.Sent += buffer.Length;
            }
            public override bool CanRead => inner.CanRead;
            public override bool CanWrite => inner.CanWrite;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanTimeout => inner.CanTimeout;
            public override int ReadTimeout { get => inner.ReadTimeout; set => inner.ReadTimeout = value; }
            public override int WriteTimeout { get => inner.WriteTimeout; set => inner.WriteTimeout = value; }
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override void Flush() => inner.Flush();
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => inner.SetLength(value);
            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
