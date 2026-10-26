using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

[TestClass]
public sealed class DisclosurePreferenceWriterTests
{
    [TestMethod]
    public async Task ReturningToSavedStateDuringDebounceDoesNotWrite()
    {
        int writes = 0;
        var writer = new DisclosurePreferenceWriter(_ => Interlocked.Increment(ref writes), error => Assert.Fail(error.ToString()));
        writer.Initialize(false);
        writer.Queue(true);
        writer.Queue(false);
        await writer.Saved.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, writes);
        writer.Queue(true);
        await writer.Saved.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, writes);
        writer.Queue(true);
        await writer.Saved.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, writes, "Repeated notifications of the same state must not write again.");
    }

    [TestMethod]
    public async Task BlockedWriterDoesNotBlockNewChoicesAndOnlyLatestPendingChoiceIsSaved()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var written = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        int failures = 0;
        var writer = new DisclosurePreferenceWriter(value =>
        {
            entered.Set();
            if (!release.Wait(5000)) throw new TimeoutException();
            written.Enqueue(value);
        }, _ => Interlocked.Increment(ref failures));
        writer.Initialize(false);
        writer.Queue(true);
        try
        {
            Assert.IsTrue(entered.Wait(2000));
            for (int i = 0; i < 100; i++) writer.Queue(i % 2 == 0);
            Assert.IsFalse(writer.Saved.IsCompleted);
        }
        finally { release.Set(); await writer.Saved.WaitAsync(TimeSpan.FromSeconds(5)); }
        CollectionAssert.AreEqual(new[] { true, false }, written.ToArray());
        Assert.AreEqual(0, failures);
    }

    [TestMethod]
    public async Task FailedSaveCanRetryWithoutTreatingUnsavedChoiceAsPersisted()
    {
        int attempts = 0, failures = 0;
        var writer = new DisclosurePreferenceWriter(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Synthetic unavailable disk");
        }, _ => Interlocked.Increment(ref failures));
        writer.Initialize(false);
        writer.Queue(true);
        await writer.Saved.WaitAsync(TimeSpan.FromSeconds(5));
        writer.Queue(true);
        await writer.Saved.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, failures);
    }
}
