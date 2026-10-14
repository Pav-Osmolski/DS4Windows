using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DS4Windows.DS4Control;

namespace DS4WindowsTests;

// Isolated ephemeral loopback sockets only. No ControlService, HID device,
// controller driver, profile, actual mod port, or output backend is instantiated.
[TestClass]
public sealed class DSXUdpListenerTests
{
    private const string Rgb = "{\"type\":2,\"parameters\":[0,255,100,50,200]}";
    private const string Status = "{\"type\":0,\"parameters\":[]}";

    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("::1")]
    [Timeout(10000)]
    public async Task OversizedAndEmptyDatagramsCannotBreakTheCapturedReceiveSocket(string address)
    {
        IPAddress ip = IPAddress.Parse(address);
        using var server = new DSXUdpServer();
        Assert.IsTrue(server.StartForTesting(ip), server.LastError);
        using var client = new UdpClient(ip.AddressFamily);
        client.Client.Bind(new IPEndPoint(ip, 0));
        var endpoint = new IPEndPoint(ip, server.Port);
        byte[] oversized = new byte[60_000];
        await client.SendAsync(oversized, oversized.Length, endpoint);
        await client.SendAsync(Array.Empty<byte>(), 0, endpoint);
        AssertNoResponse(client);
        byte[] request = DSXUdpServerTests.Packet(Status);
        await client.SendAsync(request, request.Length, endpoint);
        using var response = await Receive(client);
        Assert.IsTrue(server.IsRunning);
        Assert.AreEqual(string.Empty, server.LastError);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task MutationPacketGetsOneTruthfulResponseWithoutAnExplicitStatusInstruction()
    {
        using var server = Start();
        using var client = Client();
        var events = new List<string>();
        using var completed = new ManualResetEventSlim();
        server.PacketStarting = () => events.Add("begin");
        server.OnRGBUpdate += (index, r, g, b, brightness) => events.Add($"rgb:{index}:{r}:{g}:{b}:{brightness}");
        server.PacketCompleted = () => { events.Add("complete"); completed.Set(); };
        await Send(client, server, Rgb);
        using JsonDocument response = await Receive(client);
        Assert.IsTrue(completed.Wait(3000));
        Assert.IsFalse(response.RootElement.GetProperty("isControllerConnected").GetBoolean());
        Assert.AreEqual(0, response.RootElement.GetProperty("BatteryLevel").GetInt32());
        CollectionAssert.AreEqual(new[] { "begin", "rgb:0:255:100:50:200", "complete" }, events);
        AssertNoResponse(client);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task OfficialV2RgbAndCustomPulsePacketWithNullPaddingGetsOneReply()
    {
        // Official Paliverse/DualSenseX v2 example: RGB without brightness,
        // CustomTriggerValue PulseAB + seven bytes, and default array entries.
        using var server = Start();
        using var client = Client();
        byte[] rgb = null, trigger = null;
        server.OnRGBUpdate += (_, r, g, b, brightness) => rgb = new[] { r, g, b, brightness };
        server.OnTriggerUpdate += (_, _, data) => trigger = data;
        await Send(client, server, "{\"type\":2,\"parameters\":[0,255,255,255]}",
            "{\"type\":1,\"parameters\":[0,1,12,8,0,101,255,255,0,0,0]}",
            "{\"type\":0,\"parameters\":null}", "{\"type\":0,\"parameters\":null}");
        using var response = await Receive(client);
        CollectionAssert.AreEqual(new byte[] { 255, 255, 255, 255 }, rgb);
        CollectionAssert.AreEqual(new byte[] { 0x26, 0, 101, 255, 255, 0, 0, 0, 0, 0, 0 }, trigger);
        AssertNoResponse(client);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task RepeatedStatusInstructionsAreCoalescedAndZeroThresholdDoesNotAlterTheMapper()
    {
        using var server = Start();
        using var client = Client();
        int calls = 0;
        server.GetStatus = () => { Interlocked.Increment(ref calls); return null; };
        await Send(client, server, Status, Status, "{\"type\":4,\"parameters\":[0,1,0]}",
            "{\"type\":4,\"parameters\":[0,2,0]}");
        using var response = await Receive(client);
        Assert.AreEqual(1, calls);
        Assert.IsFalse(response.RootElement.GetProperty("isControllerConnected").GetBoolean());
        AssertNoResponse(client);
    }

    [TestMethod]
    [DataRow("{\"type\":1,\"parameters\":[0,1,8,10]}")]
    [DataRow("{\"type\":1,\"parameters\":[0,1,12,16,0,101,255,255,0,0,0]}")]
    [DataRow("{\"type\":4,\"parameters\":[0,1,100]}")]
    [Timeout(10000)]
    public async Task KnownUnsupportedInstructionsReturnAnExplicitStatusAndNeverPartiallyApply(string unsupported)
    {
        using var server = Start();
        using var client = Client();
        int mutations = 0;
        server.OnRGBUpdate += (_, _, _, _, _) => Interlocked.Increment(ref mutations);
        server.OnTriggerUpdate += (_, _, _) => Interlocked.Increment(ref mutations);
        await Send(client, server, Rgb, unsupported);
        using var response = await Receive(client);
        StringAssert.StartsWith(response.RootElement.GetProperty("Status").GetString(), "Unsupported DSX");
        Assert.AreEqual(0, mutations);
        AssertNoResponse(client);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task MalformedPacketCannotApplyItsValidPrefixOrGenerateStatusAmplification()
    {
        using var server = Start();
        using var client = Client();
        int mutations = 0, captures = 0;
        server.OnRGBUpdate += (_, _, _, _, _) => Interlocked.Increment(ref mutations);
        server.PacketStarting = () => Interlocked.Increment(ref captures);
        await Send(client, server, Rgb, "{\"type\":7,\"parameters\":[-1]}");
        AssertNoResponse(client);
        Assert.AreEqual(0, mutations);
        Assert.AreEqual(0, captures);
        await Send(client, server, Status);
        using var valid = await Receive(client);
        Assert.AreEqual(1, captures);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task CallbackFailureCompletesThePacketAndTheListenerRemainsUsable()
    {
        using var server = Start();
        using var client = Client();
        using var complete = new ManualResetEventSlim();
        server.OnRGBUpdate += (_, _, _, _, _) => throw new InvalidOperationException("Synthetic handler failure");
        server.PacketCompleted = () => complete.Set();
        await Send(client, server, Rgb);
        Assert.IsTrue(complete.Wait(3000));
        AssertNoResponse(client);
        await Send(client, server, Status);
        using var response = await Receive(client);
        Assert.IsTrue(server.IsRunning);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task StopWaitsForTheAdmittedCallbackAndSuppressesTheRemainderOfItsBatch()
    {
        using var server = Start();
        using var client = Client();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var stopping = new ManualResetEventSlim();
        int calls = 0, completions = 0;
        server.PacketCompleted = () => Interlocked.Increment(ref completions);
        server.OnRGBUpdate += (_, _, _, _, _) =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(5000);
        };
        try
        {
            await Send(client, server, Rgb, Rgb);
            Assert.IsTrue(entered.Wait(3000));
            Task stop = Task.Run(() => { stopping.Set(); server.Stop(); });
            Assert.IsTrue(stopping.Wait(3000));
            Assert.IsFalse(stop.Wait(50), "Stop returned with an admitted callback still running.");
            release.Set();
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, completions);
            Assert.IsFalse(server.IsRunning);
            AssertNoResponse(client);
        }
        finally { release.Set(); }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task StopInsideReceiveCallbackDoesNotDeadlockOrRunRemainingInstructions()
    {
        using var server = Start();
        using var client = Client();
        using var complete = new ManualResetEventSlim();
        int calls = 0;
        server.OnRGBUpdate += (_, _, _, _, _) => { Interlocked.Increment(ref calls); server.Stop(); };
        server.PacketCompleted = () => complete.Set();
        await Send(client, server, Rgb, Rgb);
        Assert.IsTrue(complete.Wait(3000));
        Assert.AreEqual(1, calls);
        Assert.IsFalse(server.IsRunning);
        AssertNoResponse(client);
    }

    [TestMethod]
    [Timeout(10000)]
    public void StopInsideDirectIngressCallbackAlsoDoesNotJoinItsOwnDeliveryBarrier()
    {
        using var server = Start();
        using var client = Client();
        int calls = 0;
        server.OnRGBUpdate += (_, _, _, _, _) => { calls++; server.Stop(); };
        Assert.IsFalse(server.ProcessIncomingPacket(DSXUdpServerTests.Packet(Rgb, Rgb),
            (IPEndPoint)client.Client.LocalEndPoint));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task RestartInsideCallbackCannotRetargetItsRemainingInstructionsToANewSession()
    {
        using var server = Start();
        using var client = Client();
        using var restarted = new ManualResetEventSlim();
        int calls = 0;
        server.OnRGBUpdate += (_, _, _, _, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            { Assert.IsTrue(server.StartForTesting()); restarted.Set(); }
        };
        await Send(client, server, Rgb, Rgb);
        Assert.IsTrue(restarted.Wait(3000));
        await Send(client, server, Rgb);
        using var response = await Receive(client);
        Assert.AreEqual(2, calls);
        Assert.IsTrue(server.IsRunning);
        AssertNoResponse(client);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ConsecutiveRestartsKeepOneSocketGenerationAndReleaseEachOldPort()
    {
        using var server = Start();
        using var client = Client();
        int calls = 0;
        server.OnRGBUpdate += (_, _, _, _, _) => Interlocked.Increment(ref calls);
        for (int i = 0; i < 15; i++)
        {
            int previousPort = server.Port;
            server.Stop();
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, previousPort))) { }
            Assert.IsTrue(server.StartForTesting());
            await Send(client, server, Rgb);
            using var response = await Receive(client);
            Assert.AreEqual(i + 1, calls);
        }
        AssertNoResponse(client);
    }

    [TestMethod]
    [Timeout(10000)]
    public void FailedBindReleasesResourcesAndCanRetryAfterThePortIsAvailable()
    {
        using var occupied = Client();
        int port = ((IPEndPoint)occupied.Client.LocalEndPoint).Port;
        using var server = new DSXUdpServer();
        Assert.IsFalse(server.Start(port));
        Assert.IsFalse(server.IsRunning);
        StringAssert.Contains(server.LastError, "already in use");
        occupied.Dispose();
        Assert.IsTrue(server.Start(port));
        Assert.IsTrue(server.IsRunning);
        Assert.AreEqual(port, server.Port);
        Assert.AreEqual(string.Empty, server.LastError);
        Assert.IsFalse(server.Start(port, "0.0.0.0"));
        Assert.IsTrue(server.IsRunning); // Invalid edits cannot disrupt the valid endpoint.
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ConcurrentStartStopAndDisposeFinishWithoutLockInversion()
    {
        using var server = new DSXUdpServer();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 15; i++) { server.StartForTesting(); server.Stop(); }
        }))).WaitAsync(TimeSpan.FromSeconds(10));
        server.Dispose();
        Assert.IsFalse(server.IsRunning);
        Assert.IsFalse(server.StartForTesting());
        StringAssert.Contains(server.LastError, "disposed");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task UnexpectedExitNotifiesOutsideLocksAndCannotStopASuccessorCreatedByTheCallback()
    {
        using var server = Start();
        using var client = Client();
        using var notified = new ManualResetEventSlim();
        int notifications = 0;
        string failure = null;
        server.UnexpectedStopped = error =>
        {
            failure = error;
            Interlocked.Increment(ref notifications);
            Assert.IsFalse(server.IsRunning);
            Assert.IsTrue(server.StartForTesting());
            notified.Set();
        };
        server.FailSocketForTesting();
        Assert.IsTrue(notified.Wait(3000));
        StringAssert.Contains(failure, "unexpectedly");
        await Send(client, server, Status);
        using var response = await Receive(client);
        Assert.IsTrue(server.IsRunning);
        Assert.AreEqual(1, notifications);
        server.Stop();
        Assert.AreEqual(1, notifications); // Explicit stop does not signal failure.
    }

    [TestMethod]
    public void PublicIngressCannotBypassAStoppedServerOrAcceptRemoteSources()
    {
        using var server = new DSXUdpServer();
        int calls = 0;
        server.OnRGBUpdate += (_, _, _, _, _) => calls++;
        byte[] packet = DSXUdpServerTests.Packet(Rgb);
        Assert.IsFalse(server.ProcessIncomingPacket(packet, new IPEndPoint(IPAddress.Loopback, 12345)));
        Assert.IsTrue(server.StartForTesting());
        Assert.IsFalse(server.ProcessIncomingPacket(packet, new IPEndPoint(IPAddress.Parse("192.168.1.2"), 12345)));
        server.Stop();
        Assert.IsFalse(server.ProcessIncomingPacket(packet, new IPEndPoint(IPAddress.Loopback, 12345)));
        Assert.AreEqual(0, calls);
    }

    private static DSXUdpServer Start()
    {
        var server = new DSXUdpServer();
        Assert.IsTrue(server.StartForTesting(), server.LastError);
        Assert.AreEqual("127.0.0.1", server.ListenAddress);
        return server;
    }

    private static UdpClient Client()
    {
        var client = new UdpClient(AddressFamily.InterNetwork);
        client.ExclusiveAddressUse = true;
        client.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return client;
    }

    private static Task<int> Send(UdpClient client, DSXUdpServer server, params string[] instructions)
    {
        byte[] data = DSXUdpServerTests.Packet(instructions);
        return client.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Loopback, server.Port));
    }

    private static async Task<JsonDocument> Receive(UdpClient client)
    {
        var response = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(IPAddress.IsLoopback(response.RemoteEndPoint.Address));
        return JsonDocument.Parse(response.Buffer);
    }

    private static void AssertNoResponse(UdpClient client) =>
        Assert.IsFalse(client.Client.Poll(150000, SelectMode.SelectRead), "Unexpected extra UDP response.");
}
