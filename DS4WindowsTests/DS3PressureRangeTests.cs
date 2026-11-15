using System.Reflection;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
public sealed class DS3PressureRangeTests
{
    [TestMethod]
    [DataRow(0, 100)]
    [DataRow(10, 80)]
    [DataRow(99, 100)]
    [DataRow(40, 41)]
    public void FullByteRangeIsLinearBoundedAndMonotonic(int start, int full)
    {
        var range = new DS3ButtonPressureSettings(); range.SetRange(start, full);
        byte previous = 0;
        for (int raw = 0; raw <= 255; raw++)
        {
            byte actual = range.Map((byte)raw);
            int expected = (int)Math.Clamp(Math.Round((raw * 100d - start * 255d) / (full - start), MidpointRounding.AwayFromZero), 0, 255);
            Assert.AreEqual(expected, (int)actual, $"raw={raw}");
            Assert.IsTrue(actual >= previous); previous = actual;
        }
        Assert.AreEqual((byte)0, range.Map(0)); Assert.AreEqual((byte)255, range.Map(255));
        range.Reset();
        for (int raw = 0; raw <= 255; raw++) Assert.AreEqual((byte)raw, range.Map((byte)raw), "Default settings must be byte-exact.");
    }

    [TestMethod]
    [DataRow(-100, 300, 0, 100)]
    [DataRow(100, 0, 99, 100)]
    [DataRow(80, 20, 80, 81)]
    [DataRow(10, 10, 10, 11)]
    public void InvalidRangesStayUsable(int start, int full, int expectedStart, int expectedFull)
    {
        var range = new DS3ButtonPressureSettings(); range.SetRange(start, full);
        Assert.AreEqual(expectedStart, range.StartPercent); Assert.AreEqual(expectedFull, range.FullPercent);
        Assert.AreEqual((byte)0, range.Map(0)); Assert.AreEqual((byte)255, range.Map(255));
    }

    [TestMethod]
    public void ProfileRoundTripRetainsRangeAndOldProfilesResetItToDefaults()
    {
        var source = new BackingStore(); source.ds3PressureSettings[0].SetRange(10, 80);
        Assert.AreEqual(0, source.ds3PressureSettings[1].StartPercent, "Slots must not share mutable settings.");
        var dto = new ProfileDTO { DeviceIndex = 0 }; dto.MapFrom(source);
        using var text = new StringWriter(); ProfileDTO.Serializer.Serialize(text, dto);
        using var reader = new StringReader(text.ToString());
        var copy = (ProfileDTO)ProfileDTO.Serializer.Deserialize(reader); copy.DeviceIndex = 0;
        var destination = new BackingStore(); copy.MapTo(destination);
        Assert.AreEqual(10, destination.ds3PressureSettings[0].StartPercent);
        Assert.AreEqual(80, destination.ds3PressureSettings[0].FullPercent);
        // Loading an old file into an already-used slot must not inherit the last profile's range.
        var xml = System.Xml.Linq.XDocument.Parse(text.ToString());
        xml.Root!.Element("DS3PressureStartPercent")!.Remove(); xml.Root.Element("DS3PressureFullPercent")!.Remove();
        using var oldReader = new StringReader(xml.ToString());
        var old = (ProfileDTO)ProfileDTO.Serializer.Deserialize(oldReader); old.DeviceIndex = 0; old.MapTo(destination);
        Assert.AreEqual(0, destination.ds3PressureSettings[0].StartPercent);
        Assert.AreEqual(100, destination.ds3PressureSettings[0].FullPercent);
        destination.ds3PressureSettings[0].SetRange(10, 80);
        typeof(BackingStore).GetMethod("ResetProfile", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(destination, new object[] { 0 });
        Assert.AreEqual(0, destination.ds3PressureSettings[0].StartPercent);
        Assert.AreEqual(100, destination.ds3PressureSettings[0].FullPercent);
    }

    [TestMethod]
    public void InvalidProfileValuesCannotCreateAnEmptyRange()
    {
        var dto = new ProfileDTO { DeviceIndex = 0, DS3PressureStartPercent = 1000, DS3PressureFullPercent = -1 };
        var store = new BackingStore(); dto.MapTo(store);
        Assert.AreEqual(99, store.ds3PressureSettings[0].StartPercent);
        Assert.AreEqual(100, store.ds3PressureSettings[0].FullPercent);
    }
}
