using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class ViiperStartupProbeTests
{
    private static ViiperPrerequisiteStatus Status(bool timeout = false) => new()
    {
        UsbipInstalled = true, UsbipExecutableSafe = true, UsbipDriverFilesSafe = true,
        UsbipRuntimeReady = !timeout, UsbipProbeTimedOut = timeout,
        UsbipVersion = "0.9.8.1", UsbipProbeMessage = timeout ? "Synthetic probe deadline" : null,
        ViiperInstalled = true, ViiperPackageCurrent = true, ServerRunning = true
    };

    [TestMethod]
    public void TimeoutSnapshotIsRecordedBeforeDelayAndSuccessfulRetry()
    {
        int attempts = 0, delays = 0;
        var lines = new List<string>();
        var recovered = Status();
        var result = ViiperStartupProbe.Check(() => ++attempts == 1 ? Status(true) : recovered,
            milliseconds =>
            {
                Assert.AreEqual(500, milliseconds);
                Assert.IsTrue(lines.Any(line => line.Contains("Synthetic probe deadline")));
                Assert.IsTrue(lines.Any(line => line.Contains("elapsed=")));
                delays++;
            }, lines.Add);
        Assert.AreSame(recovered, result);
        Assert.IsTrue(result.Ready);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, delays);
        Assert.IsTrue(lines.Any(line => line.Contains("check 2: VIIPER virtual-controller backend ready")));
    }

    [TestMethod]
    public void RepeatedTimeoutStopsAfterOneRetryAndKeepsRepairGateClosed()
    {
        int attempts = 0, delays = 0;
        var result = ViiperStartupProbe.Check(() => { attempts++; return Status(true); },
            _ => delays++, _ => { });
        Assert.IsFalse(result.Ready);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, delays);
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("executable")]
    [DataRow("driver")]
    [DataRow("citrix")]
    [DataRow("ownership")]
    [DataRow("abi")]
    [DataRow("healthy")]
    public void PermanentFailuresAndHealthyStartsAreNeverRetried(string scenario)
    {
        var status = Status(scenario != "healthy");
        switch (scenario)
        {
            case "version": status.UsbipInstalled = false; break;
            case "executable": status.UsbipExecutableSafe = false; break;
            case "driver": status.UsbipDriverFilesSafe = false; break;
            case "citrix": status.CitrixUsbMonitorConflict = true; break;
            case "ownership": status.ViiperProcessConflict = true; break;
            case "abi":
                status.UsbipProbeTimedOut = false;
                status.UsbipProbeMessage = "ABI mismatch (diagnostic text may even say timed out)";
                break;
        }
        int attempts = 0;
        var result = ViiperStartupProbe.Check(() => { attempts++; return status; },
            _ => Assert.Fail("Unexpected retry"), _ => { });
        Assert.AreSame(status, result);
        Assert.AreEqual(1, attempts);
        Assert.AreEqual(scenario == "healthy", result.Ready);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OutputReadFailureIsNotMistakenForADeadline(bool timeoutException)
    {
        Assert.IsFalse(ViiperSetupManager.TryCompleteUsbipProbeOutput(
            Task.FromException<string>(timeoutException ? new TimeoutException("Reader fault") : new IOException("timed out is arbitrary error text")),
            Task.FromResult(""), 250, out _, out _, out bool timedOut));
        Assert.IsFalse(timedOut);
        Assert.IsFalse(ViiperSetupManager.TryCompleteUsbipProbeOutput(
            Task.FromResult(""), Task.FromResult(""), 0, out _, out _, out timedOut));
        Assert.IsTrue(timedOut);
    }
}
