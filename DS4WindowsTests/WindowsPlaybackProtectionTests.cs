using DS4Windows;
using NAudio.CoreAudioApi;

namespace DS4WindowsTests;

[TestClass]
public class WindowsPlaybackProtectionTests
{
    private static PlaybackProtectionWindow Window(params string[] existing) => new(
        new() { [Role.Console] = "speakers", [Role.Multimedia] = "headphones" },
        existing.Prepend("speakers").Prepend("headphones"), 1000);

    [TestMethod]
    public void NewOwnedPlaybackGetsPreviousRoleSpecificDeviceOnce()
    {
        var window = Window();
        Assert.AreEqual("speakers", window.Consider(Role.Console, "virtual", "virtual", true, true, 1200, 1250, 1300));
        Assert.IsNull(window.Consider(Role.Console, "virtual", "virtual", true, true, 1200, 1400, 1450));
        Assert.AreEqual("headphones", window.Consider(Role.Multimedia, "virtual", "virtual", true, true, 1200, 1250, 1300));
    }

    [TestMethod]
    public void CommunicationsAndMissingDefaultsAreNeverChanged()
    {
        Assert.IsNull(Window().Consider(Role.Communications, "virtual", "virtual", true, true, 1200, 1250, 1300));
        var window = new PlaybackProtectionWindow(new(), Array.Empty<string>(), 1000);
        Assert.IsNull(window.Consider(Role.Console, "virtual", "virtual", true, true, 1200, 1250, 1300));
    }

    [TestMethod]
    public void ExistingVirtualEndpointCanBeChosenManually()
    {
        Assert.IsNull(Window("virtual").Consider(Role.Console, "virtual", "virtual", true, true, 1200, 1250, 1300));
    }

    [TestMethod]
    public void OtherControllersPhysicalDevicesAndNewUserChoicesCancelProtection()
    {
        var window = Window();
        Assert.IsNull(window.Consider(Role.Console, "usb-headset", "usb-headset", false, true, 1200, 1250, 1300));
        Assert.IsNull(window.Consider(Role.Console, "virtual", "virtual", true, true, 1400, 1450, 1500));
        Assert.IsNull(Window().Consider(Role.Console, "other-controller", "other-controller", false, true, 1200, 1250, 1300));
        Assert.IsNull(Window().Consider(Role.Console, "virtual", "user-choice", true, true, 1200, 1250, 1300));
    }

    [TestMethod]
    public void MissingPreviousDeviceOrStaleEventsCannotRestore()
    {
        Assert.IsNull(Window().Consider(Role.Console, "virtual", "virtual", true, false, 1200, 1250, 1300));
        Assert.IsNull(Window().Consider(Role.Console, "virtual", "virtual", true, true, 1200, 4000, 4050));
        Assert.IsNull(Window().Consider(Role.Console, "virtual", "virtual", true, true, 1200, 1250, 4000));
        Assert.IsNull(Window().Consider(Role.Console, "virtual", "virtual", true, true, 32000, 32100, 32200));
        Assert.IsNull(Window().Consider(Role.Console, "virtual", "virtual", true, true, 900, 1250, 1300));
    }

    [TestMethod]
    public void OldNotificationsDoNotCancelANewSession()
    {
        var window = Window();
        Assert.IsNull(window.Consider(Role.Console, "old", "old", false, true, 500, 600, 1300));
        Assert.AreEqual("speakers", window.Consider(Role.Console, "virtual", "virtual", true, true, 1200, 1250, 1300));
    }

    [TestMethod]
    public void DefaultNotificationCanArriveBeforeTheEndpointNotification()
    {
        var window = Window();
        Assert.IsNull(window.Consider(Role.Console, "virtual", "virtual", true, true, 0, 1250, 1300));
        Assert.AreEqual("speakers", window.Consider(Role.Console, "virtual", "virtual", true, true, 1300, 1250, 1350));
    }

    [TestMethod]
    public void PlaybackPreferenceDefaultsOnAndRoundTripsBothStates()
    {
        var dto = new DS4WinWPF.DS4Control.DTOXml.AppSettingsDTO();
        Assert.IsTrue(dto.PreserveWindowsAudioOutput);
        var serializer = new System.Xml.Serialization.XmlSerializer(dto.GetType());
        foreach (bool enabled in new[] { false, true })
        {
            dto.PreserveWindowsAudioOutput = enabled;
            using var text = new StringWriter();
            serializer.Serialize(text, dto);
            using var reader = new StringReader(text.ToString());
            Assert.AreEqual(enabled, ((DS4WinWPF.DS4Control.DTOXml.AppSettingsDTO)serializer.Deserialize(reader)).PreserveWindowsAudioOutput);
        }
    }
}
