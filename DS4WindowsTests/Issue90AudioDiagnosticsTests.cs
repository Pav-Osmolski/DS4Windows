using DS4Windows;
using NAudio.Wave;

namespace DS4WindowsTests;

[TestClass]
public class Issue90AudioDiagnosticsTests
{
    [TestMethod]
    public void KnownDelayedCopyIsDetectedWithGainAndOffset()
    {
        var random = new Random(90);
        float[] source = Enumerable.Range(0, 2000).Select(_ => (float)random.NextDouble()).ToArray();
        float[] target = new float[source.Length + 37];
        for (int i = 0; i < source.Length; i++) target[i + 37] = source[i] * .3f + .02f;
        var result = Issue90AudioDiagnostics.Correlate(source, target);
        Assert.IsTrue(result.Available);
        Assert.AreEqual(370, result.TargetLagMs);
        Assert.IsTrue(result.Coefficient > .999);
    }
    [TestMethod]
    public void SilenceMissingShortAndNonFiniteDataCannotLookLikeCleanAudio()
    {
        foreach (float[] invalid in new[] { Array.Empty<float>(), new float[50], new float[300], new[] { float.NaN }, new[] { float.PositiveInfinity } })
            Assert.IsFalse(Issue90AudioDiagnostics.Correlate(invalid, invalid).Available);
        Assert.IsFalse(Issue90AudioDiagnostics.Correlate(null, null).Available);
    }
    [TestMethod]
    public void IndependentSignalsDoNotProduceStrongCorrelation()
    {
        var random = new Random(123);
        float[] x = Enumerable.Range(0, 3000).Select(_ => (float)random.NextDouble()).ToArray();
        float[] y = Enumerable.Range(0, 3000).Select(_ => (float)random.NextDouble()).ToArray();
        Assert.IsTrue(Issue90AudioDiagnostics.Correlate(x, y).Coefficient < .15);
    }
    [TestMethod]
    public void CaptureHasHardByteAndTimelineLimitsAndCopiesBorrowedMemory()
    {
        var lane = new Issue90AudioDiagnostics.Lane("test", capacity:8, chunkCapacity:2);
        byte[] input = { 1, 0, 2, 0 };
        lane.Record(input, new WaveFormat(16000, 16, 1), 100);
        input[0] = 9;
        lane.Record(input, new WaveFormat(16000, 16, 1), 200);
        lane.Record(input, new WaveFormat(16000, 16, 1), 300);
        Assert.AreEqual(8, lane.Count); Assert.AreEqual(1, lane.Drops);
        string folder = Path.Combine(Path.GetTempPath(), "DS4W-Issue90-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            lane.Save(folder);
            using (var reader = new WaveFileReader(Path.Combine(folder,"test.wav")))
            {
                byte[] data=new byte[8]; Assert.AreEqual(8,reader.Read(data,0,8)); Assert.AreEqual((byte)1,data[0]); Assert.AreEqual((byte)9,data[4]);
            }
            string chunks = File.ReadAllText(Path.Combine(folder,"test-chunks.json"));
            StringAssert.Contains(chunks, "100"); StringAssert.Contains(chunks,"200");
            lane.Record(input, new WaveFormat(16000,16,1),400); Assert.AreEqual(8,lane.Count);
        }
        finally { Directory.Delete(folder,true); }
    }
    [TestMethod]
    public void DiagnosticHookDoesNotWaitForBusyCaptureBuffer()
    {
        var lane = new Issue90AudioDiagnostics.Lane("test",capacity:8,chunkCapacity:2);
        using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Task owner = Task.Run(() => { lock(lane) { held.Set(); release.Wait(); } });
        Assert.IsTrue(held.Wait(2000));
        try { lane.Record(new byte[]{1},null,1); Assert.AreEqual(0,lane.Count); Assert.AreEqual(1,lane.Drops); }
        finally { release.Set(); owner.GetAwaiter().GetResult(); }
    }
    [TestMethod]
    public void DisabledHooksAllocateNothingAfterInitialization()
    {
        byte[] data = new byte[16]; short[] samples = new short[8];
        Issue90AudioDiagnostics.Pcm(0,0,data,32000,2); Issue90AudioDiagnostics.Mono(0,2,samples,8); Issue90AudioDiagnostics.Compressed(0,data,1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) { Issue90AudioDiagnostics.Pcm(0,0,data,32000,2); Issue90AudioDiagnostics.Mono(0,2,samples,8); Issue90AudioDiagnostics.Compressed(0,data,i); }
        Assert.AreEqual(0L,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
