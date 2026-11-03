using System.IO;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
public class GameBarBluetoothRecoverySettingsTests
{
    [TestMethod]
    [DataRow("<Profile />", true)]
    [DataRow("<Profile><GameBarBluetoothInputRecovery>True</GameBarBluetoothInputRecovery></Profile>", true)]
    [DataRow("<Profile><GameBarBluetoothInputRecovery>False</GameBarBluetoothInputRecovery></Profile>", false)]
    [DataRow("<Profile><GameBarBluetoothInputRecovery>invalid</GameBarBluetoothInputRecovery></Profile>", true)]
    public void MissingAndInvalidSettingsDefaultOnAndExplicitOffSurvivesRoundTrip(string xml, bool expected)
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var reader = new StringReader(xml);
        var dto = (AppSettingsDTO)serializer.Deserialize(reader);
        var store = new BackingStore();
        dto.MapTo(store);
        Assert.AreEqual(expected, store.gameBarBluetoothInputRecovery);
        var saved = new AppSettingsDTO();
        saved.MapFrom(store);
        using var writer = new StringWriter();
        serializer.Serialize(writer, saved);
        using var savedReader = new StringReader(writer.ToString());
        var restored = (AppSettingsDTO)serializer.Deserialize(savedReader);
        var restoredStore = new BackingStore();
        restored.MapTo(restoredStore);
        Assert.AreEqual(expected, restoredStore.gameBarBluetoothInputRecovery);
    }
}
