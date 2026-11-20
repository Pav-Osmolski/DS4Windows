using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public sealed class BluetoothInputCadenceTests
{
    [TestMethod]
    public void AQueuedReportBurstCannotBecomeItsOwnRateSample()
    {
        var cadence = new BluetoothInputCadence();
        const long frequency = 1_000_000;
        Assert.IsFalse(cadence.Observe(100, frequency, out _));
        for (int report = 1; report < 64; report++)
            Assert.IsFalse(cadence.Observe(100 + report * 3, frequency, out _));

        Assert.IsTrue(cadence.Observe(250_100, frequency, out double interval));
        Assert.AreEqual(250.0 / 64, interval, 0.0001);
        cadence.Reset();
        Assert.IsFalse(cadence.Observe(10_000_000, frequency, out _));
    }
}
