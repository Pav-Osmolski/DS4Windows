using System.Net;
using System.Net.Sockets;
using System.Text;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class ViiperReadinessProbeRegressionTests
{
    [TestMethod]
    public async Task RepeatedReadinessChecksDoNotDisposeSharedConnectCompletion()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var peer = await listener.AcceptTcpClientAsync(stop.Token);
                using var stream = peer.GetStream();
                byte[] command = new byte[5];
                try
                {
                    await stream.ReadExactlyAsync(command, stop.Token);
                    CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("ping\0"), command);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("VIIPER readiness fixture\0"), stop.Token);
                }
                catch (EndOfStreamException) { /* A failed probe closed before ping. */ }
            }
        }, stop.Token);
        try
        {
            for (int attempt = 0; attempt < 256; attempt++)
                Assert.IsTrue(ViiperSetupManager.ProbeServer("127.0.0.1", port,
                    authenticated: false, out string failure), $"Probe {attempt}: {failure}");
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            try { await server.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        }
    }
}
