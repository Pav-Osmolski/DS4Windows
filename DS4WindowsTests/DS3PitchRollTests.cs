using System.IO;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
public sealed class DS3PitchRollTests
{
    private const int Gravity = SixAxis.ACC_RES_PER_G;

    [TestMethod]
    public void OldProfileSettingRoundTripsAndRemainsIndependentAcrossSlots()
    {
        var serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
        using var reader = new StringReader(
            "<DS4Windows><UseDs3PitchRollSim>true</UseDs3PitchRollSim></DS4Windows>");
        var enabled = (ProfileDTO)serializer.Deserialize(reader);
        var store = new BackingStore();
        enabled.DeviceIndex = 0;
        enabled.MapTo(store);

        using var missingSetting = new StringReader("<DS4Windows />");
        var disabled = (ProfileDTO)serializer.Deserialize(missingSetting);
        disabled.DeviceIndex = 1;
        disabled.MapTo(store);
        Assert.IsTrue(store.useDs3PitchRollSim[0]);
        Assert.IsFalse(store.useDs3PitchRollSim[1]);

        var saved = new ProfileDTO { DeviceIndex = 0 };
        saved.MapFrom(store);
        using var writer = new StringWriter();
        serializer.Serialize(writer, saved);
        using var savedXml = new StringReader(writer.ToString());
        Assert.IsTrue(((ProfileDTO)serializer.Deserialize(savedXml)).UseDs3PitchRollSim);
    }

    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    [DataRow(false, -1)]
    [DataRow(true, -1)]
    public void SlowTiltProducesTheExpectedAxisAndSign(bool roll, int gravityDirection)
    {
        const double speed = 90;
        var estimator = new DS3PitchRollEstimator();
        ulong[] timestamps = [1000, 6000, 15000, 21000, 32000];
        (int Pitch, int Roll) result = default;
        foreach (ulong timestamp in timestamps)
        {
            double radians = speed * Math.PI / 180 * timestamp / 1_000_000;
            int y = (int)Math.Round(Math.Cos(radians) * Gravity) * gravityDirection;
            int tilt = (int)Math.Round(Math.Sin(radians) * Gravity);
            result = estimator.Update(roll ? tilt : 0, y, roll ? 0 : tilt, timestamp, 1);
        }

        int expected = (int)(speed * SixAxis.GYRO_RES_IN_DEG_SEC * gravityDirection);
        Assert.AreEqual(0, roll ? result.Pitch : result.Roll);
        Assert.AreEqual(roll ? expected : -expected, roll ? result.Roll : result.Pitch, 25);
    }

    [TestMethod]
    [DataRow(20_000, 1, 2_000, 0)] // Sudden acceleration near 1 g
    [DataRow(20_000, 1, 0, 2)] // Acceleration outside the gravity band
    [DataRow(16_000, 1, 50, 0)] // Duplicate timestamp
    [DataRow(12_000, 1, 50, 0)] // Backward timestamp
    [DataRow(80_000, 1, 50, 0)] // Report gap
    [DataRow(20_000, 2, 50, 0)] // New profile
    public void DiscontinuityDoesNotCarryAnEstimateIntoTheNextReport(
        int timestamp, int revision, int x, int extraGravity)
    {
        var estimator = new DS3PitchRollEstimator();
        for (ulong time = 4000; time <= 16000; time += 4000)
            Assert.AreEqual((0, 0), estimator.Update(0, Gravity, 0, time, 1));

        Assert.AreEqual((0, 0), estimator.Update(x, Gravity * (1 + extraGravity), 0,
            (ulong)timestamp, revision));
        Assert.AreEqual((0, 0), estimator.Update(x, Gravity, 0,
            (ulong)timestamp + 4000, revision));
    }

    [TestMethod]
    public void DisablingEstimationClearsTheSamplingWindow()
    {
        var estimator = new DS3PitchRollEstimator();
        for (ulong time = 4000; time <= 16000; time += 4000)
            estimator.Update((int)(time / 200), Gravity, 0, time, 1);

        estimator.Reset();
        Assert.AreEqual((0, 0), estimator.Update(120, Gravity, 0, 20_000, 1));
    }
}
