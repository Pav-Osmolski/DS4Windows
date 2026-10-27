using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public class UsbHapticsReadinessPolicyTests
{
    [TestMethod]
    public void NormalEnumerationWaitDoesNotWarnButPersistentWaitEscalatesOnce()
    {
        var policy = new UsbHapticsReadinessPolicy();
        var pending = new AudioEndpointPendingException("The matching VIIPER four-channel audio endpoint is not ready.");
        Assert.IsFalse(policy.Observe(pending, TimeSpan.Zero).Value.Warning);
        Assert.IsNull(policy.Observe(pending, TimeSpan.FromSeconds(1)));
        Assert.IsNull(policy.Observe(pending, TimeSpan.FromSeconds(14.99)));
        Assert.IsTrue(policy.Observe(pending, TimeSpan.FromSeconds(15)).Value.Warning);
        Assert.IsNull(policy.Observe(pending, TimeSpan.FromSeconds(16)));
    }

    [TestMethod]
    public void ChangingPrerequisiteDoesNotRestartGraceAndReadyResetsNextSession()
    {
        var policy = new UsbHapticsReadinessPolicy();
        Assert.IsFalse(policy.Observe(new AudioEndpointPendingException("Virtual endpoint"), TimeSpan.FromSeconds(50)).Value.Warning);
        Assert.IsFalse(policy.Observe(new AudioEndpointPendingException("Physical endpoint"), TimeSpan.FromSeconds(64)).Value.Warning);
        Assert.IsTrue(policy.Observe(new AudioEndpointPendingException("Virtual endpoint"), TimeSpan.FromSeconds(65)).Value.Warning);
        policy.Ready();
        Assert.IsFalse(policy.Observe(new AudioEndpointPendingException("Virtual endpoint"), TimeSpan.FromSeconds(90)).Value.Warning);
    }

    [TestMethod]
    public void UnexpectedPlaybackErrorsWarnImmediatelyAndRemainWarningsUntilRecovery()
    {
        var policy = new UsbHapticsReadinessPolicy();
        Assert.IsTrue(policy.Observe(new InvalidOperationException("The USB audio render stream stopped."), TimeSpan.Zero).Value.Warning);
        Assert.IsTrue(policy.Observe(new AudioEndpointPendingException("Virtual endpoint"), TimeSpan.FromSeconds(1)).Value.Warning);
        policy.Ready();
        Assert.IsFalse(policy.Observe(new AudioEndpointPendingException("Virtual endpoint"), TimeSpan.FromSeconds(2)).Value.Warning);
    }
}
