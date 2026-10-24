using System.Net;
using System.Net.Sockets;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class ViiperReadinessConnectionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RapidReadinessConnectionsDoNotLoseTheirCompletionHandle(bool installer)
    {
        const int count = 256;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stopped = new CancellationTokenSource();
        Task server = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    using TcpClient peer = await listener.AcceptTcpClientAsync(stopped.Token);
                    await peer.GetStream().ReadExactlyAsync(new byte[5], stopped.Token);
                    await peer.GetStream().WriteAsync("VIIPER readiness fixture\n"u8.ToArray(), stopped.Token);
                }
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            catch (SocketException) when (stopped.IsCancellationRequested) { }
        });
        try
        {
            for (int i = 0; i < count; i++)
            {
                string failure = null;
                bool ready = installer
                    ? DS4Windows.Bootstrapper.InfrastructureProbe.ViiperApiReady("127.0.0.1", port)
                    : ViiperSetupManager.ProbeServer("127.0.0.1", port, false, out failure);
                Assert.IsTrue(ready, $"Readiness connection {i + 1} (installer={installer}): {failure}");
            }
        }
        finally
        {
            stopped.Cancel();
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
