using System.IO;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
public class TouchpadSwipeProfileTests
{
    [TestMethod]
    [DataRow("", 300)]
    [DataRow("<TouchpadSwipeDistance>25</TouchpadSwipeDistance>", 50)]
    [DataRow("<TouchpadSwipeDistance>450</TouchpadSwipeDistance>", 450)]
    [DataRow("<TouchpadSwipeDistance>900</TouchpadSwipeDistance>", 600)]
    public void DistanceLoadsAndRoundTripsThroughProfileXml(string elements, int expected)
    {
        var serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
        using var reader = new StringReader("<DS4Windows>" + elements + "</DS4Windows>");
        var loaded = (ProfileDTO)serializer.Deserialize(reader);
        loaded.DeviceIndex = 0;
        var source = new BackingStore();
        loaded.MapTo(source);
        Assert.AreEqual(expected, source.touchpadSwipeDistance[0]);

        var dto = new ProfileDTO { DeviceIndex = 0 };
        dto.MapFrom(source);
        using var writer = new StringWriter();
        serializer.Serialize(writer, dto);
        using var saved = new StringReader(writer.ToString());
        var reloaded = (ProfileDTO)serializer.Deserialize(saved);
        Assert.AreEqual(expected, reloaded.TouchpadSwipeDistance);
    }
}
