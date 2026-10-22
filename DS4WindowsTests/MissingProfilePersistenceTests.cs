using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class MissingProfilePersistenceTests
{
    [TestMethod]
    public void EmptySelectionAndSubsequentChosenProfileSurviveSettingsRoundTrip()
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        var initial = (AppSettingsDTO)serializer.Deserialize(new StringReader(
            "<Profile><Controller1 /></Profile>"))!;
        var store = new BackingStore();
        initial.MapTo(store);
        Assert.AreEqual("", store.profilePath[0]);
        Assert.AreEqual("", store.olderProfilePath[0]);
        bool linked = Global.linkedProfileCheck[0];
        try
        {
            Global.linkedProfileCheck[0] = false;
            foreach (string selected in new[] { "", "My chosen profile" })
            {
                // Regular selection updates both paths in ChangeSelectedProfile.
                store.profilePath[0] = store.olderProfilePath[0] = selected;
                var saved = new AppSettingsDTO();
                saved.MapFrom(store);
                using var writer = new StringWriter();
                serializer.Serialize(writer, saved);
                var reopened = (AppSettingsDTO)serializer.Deserialize(new StringReader(writer.ToString()))!;
                var restarted = new BackingStore();
                reopened.MapTo(restarted);
                Assert.AreEqual(selected, restarted.profilePath[0]);
                Assert.AreEqual(selected, restarted.olderProfilePath[0]);
            }
        }
        finally { Global.linkedProfileCheck[0] = linked; }
    }
}
