using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class ViiperManagementTraceTests
{
    private static string Request(ViiperClient client, string path, string payload = null)
    {
        try
        {
            return (string)typeof(ViiperClient).GetMethod("SendRequestRaw",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(client, [path, payload, 2000]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompleteReplyRecordsBothWireAndPlaintextWithoutContents(bool encrypted)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var lines = new List<string>();
        byte[] key = new byte[32];
        var client = new ViiperClient("127.0.0.1", port,
            stream => encrypted ? new ViiperEncryptedStream(stream, (byte[])key.Clone(), ViiperConnectionRole.Client) : stream,
            lines.Add);
        Task<string> query = Task.Run(() => Request(client, "bus/create", "SECRET-PAYLOAD"));
        using TcpClient peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        using Stream stream = encrypted ? new ViiperEncryptedStream(peer.GetStream(), key, ViiperConnectionRole.Server) : peer.GetStream();
        byte[] reply = Encoding.UTF8.GetBytes("{\"busId\":42}\n");
        await Task.Run(() =>
        {
            var one = new byte[1];
            do { stream.ReadExactly(one); } while (one[0] != 0);
            stream.Write(reply);
        }).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual("{\"busId\":42}", await query.WaitAsync(TimeSpan.FromSeconds(3)));
        string line = lines.Single();
        StringAssert.Contains(line, "operation=bus-create phase=Close success=True");
        StringAssert.Contains(line, $"localPort={((IPEndPoint)peer.Client.RemoteEndPoint).Port}");
        StringAssert.Contains(line, $"remotePort={port}");
        StringAssert.Contains(line, $"wireReceived={reply.Length + (encrypted ? 32 : 0)} ");
        StringAssert.Contains(line, $"plaintextReceived={reply.Length} replyComplete=True");
        Assert.IsFalse(line.Contains("SECRET"));
        Assert.IsFalse(line.Contains("busId"));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(7)]
    public void ResetDuringEncryptedReplyReportsPartialWireButNoPlaintext(int prefixLength)
    {
        var lines = new List<string>();
        var trace = new ViiperManagementTrace("bus/create", lines.Add) { Phase = "ReadReply" };
        using Stream observed = trace.Observe(new PrefixThenReset(prefixLength));
        using var stream = new ViiperEncryptedStream(observed, new byte[32], ViiperConnectionRole.Client);
        var error = Assert.ThrowsExactly<IOException>(() => stream.Read(new byte[20]));
        trace.Finish(false, error);
        string line = lines.Single();
        StringAssert.Contains(line, "phase=ReadReply success=False");
        StringAssert.Contains(line, "plaintextReceived=0 replyComplete=False");
        StringAssert.Contains(line, $"wireReceived={prefixLength} ");
        StringAssert.Contains(line, "socketError=ConnectionReset");
    }

    [TestMethod]
    public void AuthenticationFailureAndBrokenLoggerPreserveOriginalErrorAndHideSecrets()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var expected = new IOException("SECRET", new SocketException((int)SocketError.ConnectionReset));
        var lines = new List<string>();
        var client = new ViiperClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            _ => throw expected, line => { lines.Add(line); throw new InvalidOperationException("sink"); });
        Assert.AreSame(expected, Assert.ThrowsExactly<IOException>(() => Request(client,
            "bus/2/add-authorized-xboxone", "SECRET-CAPABILITY")));
        string recorded = lines.Single();
        StringAssert.Contains(recorded, "operation=device-add-authorized phase=Authenticate success=False");
        StringAssert.Contains(recorded, "socketError=ConnectionReset nativeError=10054");
        Assert.IsFalse(recorded.Contains("SECRET"));
        Assert.IsFalse(recorded.Contains("bus/2"));
    }

    [TestMethod]
    public async Task SuccessfulBusReplyAndNextConnectionFailureHaveDistinctTraceIds()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int calls = 0;
        var lines = new List<string>();
        var expected = new IOException("second connection failed");
        var client = new ViiperClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            stream => ++calls == 1 ? stream : throw expected, lines.Add);
        Task<string> first = Task.Run(() => Request(client, "bus/create"));
        using TcpClient peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        peer.ReceiveTimeout = 2000;
        await Task.Run(() =>
        {
            var one = new byte[1];
            do { peer.GetStream().ReadExactly(one); } while (one[0] != 0);
            peer.GetStream().Write(Encoding.UTF8.GetBytes("{\"busId\":1}\n"));
        }).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual("{\"busId\":1}", await first.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.AreSame(expected, Assert.ThrowsExactly<IOException>(() => Request(client, "bus/1/add", "{}")));
        Assert.AreEqual(2, lines.Count);
        StringAssert.Contains(lines[0], "operation=bus-create phase=Close success=True");
        StringAssert.Contains(lines[1], "operation=device-add phase=Authenticate success=False");
        Assert.AreNotEqual(lines[0].Split(' ')[2], lines[1].Split(' ')[2]);
        Assert.AreEqual(2, calls, "Diagnostics must not retry bus/device creation.");
    }

    [TestMethod]
    public async Task ProbeFailureKeepsExistingFailureDescriptionAndAddsSafeSocketDetail()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var lines = new List<string>();
        string failure = null;
        var query = Task.Run(() => ViiperSetupManager.ProbeServer("127.0.0.1", port, false,
            out failure, managementDiagnostic: lines.Add));
        using TcpClient peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        peer.ReceiveTimeout = 2000;
        await Task.Run(() => peer.GetStream().ReadExactly(new byte[5])).WaitAsync(TimeSpan.FromSeconds(3));
        peer.Client.LingerState = new LingerOption(true, 0);
        peer.Close();
        Assert.IsFalse(await query.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.IsTrue(failure is "ReadPing: IOException" or "ReadPing: no valid VIIPER response", failure);
        StringAssert.Contains(lines.Single(), "operation=ping phase=ReadPing success=False");
        StringAssert.Contains(lines.Single(), "wireSent=5 wireReceived=0 plaintextReceived=0");
    }

    [TestMethod]
    public async Task RapidReadinessConnectionsDoNotLoseTheirCompletionHandle()
    {
        const int count = 256;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stopped = new CancellationTokenSource();
        Task server = Task.Factory.StartNew(() =>
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    using TcpClient peer = listener.AcceptTcpClient();
                    peer.ReceiveTimeout = 3000;
                    peer.GetStream().ReadExactly(new byte[5]);
                    peer.GetStream().Write("VIIPER rapid connection fixture\n"u8);
                }
            }
            catch (SocketException) when (stopped.IsCancellationRequested) { }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            for (int i = 0; i < count; i++)
                Assert.IsTrue(ViiperSetupManager.ProbeServer("127.0.0.1", port, false,
                    out string failure), $"Readiness connection {i + 1}: {failure}");
        }
        finally
        {
            stopped.Cancel();
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class PrefixThenReset(int length) : Stream
    {
        private readonly MemoryStream input = new(new byte[] { 0, 0, 0, 40, 0, 0, 0 }, 0, length);
        public override int Read(Span<byte> buffer)
        {
            int read = input.Read(buffer);
            if (read == 0) throw new IOException("SECRET", new SocketException((int)SocketError.ConnectionReset));
            return read;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) input.Dispose();
            base.Dispose(disposing);
        }
    }
}
