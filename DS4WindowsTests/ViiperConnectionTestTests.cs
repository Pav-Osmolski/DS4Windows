using System.Text.Json;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class ViiperConnectionTestTests
{
    private static string Result(string phase="ReadReply", int count=20, bool setupFailed=false) => JsonSerializer.Serialize(new
    {
        implementation="go-independent", attempted=20, setupFailed,
        attempts=Enumerable.Range(0,count).Select(i=>new {phase,success=i%2==0,localPort=12000+i,socketError=i%2==0?0:10054,elapsedMs=20})
    });

    [TestMethod]
    public void MixedOutcomesAreRetainedWithoutRetriesOrDriverOperations()
    {
        int called=0;
        Assert.AreEqual(10,ViiperConnectionTest.RunClient(()=>++called%2==0,CancellationToken.None));
        Assert.AreEqual(20,called);
        var rows=ViiperConnectionTest.ParseProbeResult(Result());
        Assert.AreEqual(20,rows.Count);
        Assert.AreEqual(10,rows.Count(x=>x.Success));
        Assert.AreEqual(10054,rows[1].SocketError);
    }

    [TestMethod]
    public void CancellationPreventsFurtherConnections()
    {
        using var cts=new CancellationTokenSource();
        int called=0;
        Assert.ThrowsExactly<OperationCanceledException>(()=>ViiperConnectionTest.RunClient(()=>
        {called++;cts.Cancel();return true;},cts.Token));
        Assert.AreEqual(1,called);
    }

    [TestMethod]
    public void MalformedPartialAndArbitraryTextAreNeverAcceptedAsProbeMetadata()
    {
        foreach (string text in new[]{Result("SECRET-PAYLOAD"),Result(count:19),Result(setupFailed:true),new string('x',16385)})
            Assert.ThrowsExactly<IOException>(()=>ViiperConnectionTest.ParseProbeResult(text));
    }
}
