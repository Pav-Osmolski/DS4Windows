using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class DSXSettingsTests
{
    [TestMethod]
    public void EditingAnInvalidEndpointDoesNotChangeTheSavedPreference()
    {
        bool enabled = Global.IsUsingDSXUDPServer();
        string address = Global.GetDSXUDPServerListenAddress();
        int port = Global.GetDSXUDPServerPortNum();
        // Test the draft properties only, without invoking startup/registry UI initialization.
        var view = (SettingsViewModel)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(SettingsViewModel));
        view.UseDSXUDPServer = !enabled;
        view.DSXUdpIpAddress = "not-an-address";
        view.DSXUdpPort = 0;
        Assert.AreEqual(enabled, Global.IsUsingDSXUDPServer());
        Assert.AreEqual(address, Global.GetDSXUDPServerListenAddress());
        Assert.AreEqual(port, Global.GetDSXUDPServerPortNum());
    }

    [TestMethod]
    public void NewAndLegacySettingsDoNotEnableAModListener()
    {
        Assert.IsFalse(new BackingStore().useDSXUDPServ);
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var reader = new StringReader("<Profile />");
        var dto = (AppSettingsDTO)serializer.Deserialize(reader);
        Assert.IsFalse(dto.UseDSXUDPServer);
        var store = new BackingStore();
        dto.MapTo(store);
        Assert.IsFalse(store.useDSXUDPServ);
        Assert.AreEqual(6969, store.dsxUdpServPort);
        Assert.AreEqual("127.0.0.1", store.dsxUdpServListenAddress);
    }

    [TestMethod]
    [DataRow("127.0.0.1", 6970)]
    [DataRow("::1", 65535)]
    public void EnabledLoopbackEndpointRoundTrips(string address, int port)
    {
        var dto = new AppSettingsDTO { UseDSXUDPServer = true,
            DSXUDPServerListenAddress = address, DSXUDPServerPort = port };
        var store = new BackingStore();
        dto.MapTo(store);
        var outgoing = new AppSettingsDTO();
        outgoing.MapFrom(store);
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var writer = new StringWriter();
        serializer.Serialize(writer, outgoing);
        using var reader = new StringReader(writer.ToString());
        var restored = (AppSettingsDTO)serializer.Deserialize(reader);
        Assert.IsTrue(restored.UseDSXUDPServer);
        Assert.AreEqual(address, restored.DSXUDPServerListenAddress);
        Assert.AreEqual(port, restored.DSXUDPServerPort);
    }

    [TestMethod]
    [DataRow("0.0.0.0", 6969)]
    [DataRow("192.168.1.1", 6969)]
    [DataRow("::", 6969)]
    [DataRow("not-an-address", 6969)]
    [DataRow("127.0.0.1", 0)]
    [DataRow("127.0.0.1", 65536)]
    public void InvalidStoredEndpointIsDisabledWithoutBreakingOtherSettings(string address, int port)
    {
        var dto = new AppSettingsDTO { UseDSXUDPServer = true,
            DSXUDPServerListenAddress = address, DSXUDPServerPort = port };
        var store = new BackingStore();
        dto.MapTo(store);
        Assert.IsFalse(store.useDSXUDPServ);
        Assert.AreEqual("127.0.0.1", store.dsxUdpServListenAddress);
        Assert.AreEqual(6969, store.dsxUdpServPort);
    }

    [TestMethod]
    public void RequestedSettingIsNotPresentedAsAReadyListener()
    {
        StringAssert.Contains(DSXModStatusPresentation.Describe(true, false, false, "::1", 6969, ""), "press Start");
        StringAssert.Contains(DSXModStatusPresentation.Describe(true, true, false, "::1", 6969, ""), "Not listening");
        StringAssert.Contains(DSXModStatusPresentation.Describe(true, true, true, "::1", 6970, ""), "[::1]:6970");
        Assert.AreEqual("The port is busy.", DSXModStatusPresentation.Describe(true, true, false,
            "127.0.0.1", 6969, "The port is busy."));
        StringAssert.Contains(DSXModStatusPresentation.Describe(false, true, false, "127.0.0.1", 6969, ""), "Off");
        StringAssert.Contains(DSXModStatusPresentation.Describe(true, true, false, "127.0.0.1", 6969, "", true), "Applying");
    }
}
