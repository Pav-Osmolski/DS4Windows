using System.Net;
using System.Net.Sockets;
using System.Text;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public class ViiperManagementResponseTests
{
    [TestMethod]
    [DataRow(false, 4096)]
    [DataRow(true, 4096)]
    [DataRow(true, 1)]
    public void CompleteReplyDoesNotRequireCleanConnectionClose(bool encrypted, int chunk)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        byte[] key = new byte[32];
        byte[] response = Encoding.UTF8.GetBytes("{\"active\":true}\n");
        if (encrypted)
        {
            using var wire = new MemoryStream();
            using var writer = new ViiperEncryptedStream(wire, (byte[])key.Clone(), ViiperConnectionRole.Server);
            writer.Write(response);
            response = wire.ToArray();
        }
        using var transport = new ResetAfterReplyStream(response, chunk);
        var client = new ViiperClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            _ => encrypted ? new ViiperEncryptedStream(transport, key, ViiperConnectionRole.Client) : transport);
        Assert.IsTrue(client.GetNarrowMicrophoneInterfaceStatus(2, "1").IsActive);
        Assert.AreEqual(0, transport.ReadsPastReply,
            "A complete protocol reply must not depend on a subsequent FIN instead of RST.");
    }

    [TestMethod]
    public void ResetBeforeReplyTerminatorStillFails()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var transport = new ResetAfterReplyStream(Encoding.UTF8.GetBytes("{\"active\":true}"), 1);
        var client = new ViiperClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, _ => transport);
        Assert.ThrowsExactly<IOException>(() => client.GetNarrowMicrophoneInterfaceStatus(2, "1"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewlineCannotBypassEncryptedRecordValidation(bool truncate)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        byte[] key = new byte[32];
        using var wire = new MemoryStream();
        using (var writer = new ViiperEncryptedStream(wire, (byte[])key.Clone(), ViiperConnectionRole.Server))
        {
            writer.Write(Encoding.UTF8.GetBytes("{\"active\":true}\n"));
            byte[] record = wire.ToArray();
            if (truncate) Array.Resize(ref record, record.Length - 1);
            else record[^1] ^= 1;
            using var transport = new ResetAfterReplyStream(record, 1);
            var client = new ViiperClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
                _ => new ViiperEncryptedStream(transport, key, ViiperConnectionRole.Client));
            if (truncate)
                Assert.ThrowsExactly<IOException>(() => client.GetNarrowMicrophoneInterfaceStatus(2, "1"));
            else
                Assert.ThrowsExactly<System.Security.Cryptography.AuthenticationTagMismatchException>(
                    () => client.GetNarrowMicrophoneInterfaceStatus(2, "1"));
        }
    }

    [TestMethod]
    public void CompleteReplyStillRequiresValidJsonAndSuccessfulApiStatus()
    {
        foreach (string reply in new[] { "not json\n", "{\"status\":409,\"title\":\"Conflict\"}\n" })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var transport = new ResetAfterReplyStream(Encoding.UTF8.GetBytes(reply), 1);
            var client = new ViiperClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, _ => transport);
            if (reply.StartsWith("not"))
                Assert.ThrowsExactly<System.Text.Json.JsonException>(() => client.GetNarrowMicrophoneInterfaceStatus(2, "1"));
            else
                Assert.ThrowsExactly<ViiperApiException>(() => client.GetNarrowMicrophoneInterfaceStatus(2, "1"));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoopbackReplyCompletesWhileServerKeepsConnectionOpen(bool encrypted)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        byte[] key = new byte[32];
        var client = new ViiperClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            stream => encrypted ? new ViiperEncryptedStream(stream, (byte[])key.Clone(), ViiperConnectionRole.Client) : stream);
        Task<ViiperMicrophoneInterfaceStatus> query = Task.Run(() => client.GetNarrowMicrophoneInterfaceStatus(2, "1"));
        using TcpClient accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
        accepted.ReceiveTimeout = accepted.SendTimeout = 2000;
        using Stream server = encrypted ? new ViiperEncryptedStream(accepted.GetStream(), key, ViiperConnectionRole.Server) : accepted.GetStream();
        // Consume the complete request without touching any installed VIIPER or USB/IP device.
        await Task.Run(() =>
        {
            var one = new byte[1];
            do { server.ReadExactly(one); } while (one[0] != 0);
            server.Write(Encoding.UTF8.GetBytes("{\"active\":true}\n"));
        }).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue((await query.WaitAsync(TimeSpan.FromSeconds(2))).IsActive,
            "The server has sent its complete reply but has not closed this connection.");
    }

    private sealed class ResetAfterReplyStream(byte[] reply, int chunk) : Stream
    {
        private int offset;
        public int ReadsPastReply { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));
        public override int Read(Span<byte> buffer)
        {
            if (offset == reply.Length)
            {
                ReadsPastReply++;
                throw new IOException("Connection reset after reply", new SocketException((int)SocketError.ConnectionReset));
            }
            int count = Math.Min(Math.Min(chunk, buffer.Length), reply.Length - offset);
            reply.AsSpan(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }
        public override void Write(byte[] buffer, int offset, int count) { }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
