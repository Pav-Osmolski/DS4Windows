using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using Microsoft.Win32;

namespace DS4WindowsTests;

[TestClass]
public class ViiperTrayPreferenceTests
{
    [TestMethod]
    public void MissingSettingKeepsTrayVisibleAndBothValuesRoundTrip()
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var legacyReader = new StringReader("<Profile />");
        var legacy = (AppSettingsDTO)serializer.Deserialize(legacyReader);
        var store = new BackingStore();
        legacy.MapTo(store);
        Assert.IsTrue(store.showViiperTrayIcon);
        foreach (bool visible in new[] { false, true })
        {
            store.showViiperTrayIcon = visible;
            var saved = new AppSettingsDTO();
            saved.MapFrom(store);
            using var writer = new StringWriter();
            serializer.Serialize(writer, saved);
            using var reader = new StringReader(writer.ToString());
            var restored = (AppSettingsDTO)serializer.Deserialize(reader);
            restored.MapTo(store);
            Assert.AreEqual(visible, store.showViiperTrayIcon);
        }
    }

    [TestMethod]
    public void PreferenceOnlySavesStateAndBuildsNextLaunchArgument()
    {
        var writes = new List<string>();
        ViiperTrayPreference.Apply(true, false,
            value => writes.Add("app:" + value), value => writes.Add("startup:" + value));
        CollectionAssert.AreEqual(new[] { "startup:False", "app:False" }, writes);
        foreach (bool visible in new[] { false, true })
        {
            var start = new ProcessStartInfo("viiper.exe");
            start.ArgumentList.Add("server");
            ViiperTrayPreference.AddArgument(start, visible);
            CollectionAssert.AreEqual(new[] { "server", visible ? "--no-tray=false" : "--no-tray=true" },
                new List<string>(start.ArgumentList));
        }
    }

    [TestMethod]
    public void FailedApplicationSaveRestoresInstalledPreference()
    {
        var writes = new List<bool>();
        Assert.ThrowsExactly<IOException>(() => ViiperTrayPreference.Apply(true, false,
            _ => throw new IOException("disk full"), value => writes.Add(value)));
        CollectionAssert.AreEqual(new[] { false, true }, writes);
        bool savedApplication = false;
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => ViiperTrayPreference.Apply(true, false,
            _ => savedApplication = true, _ => throw new UnauthorizedAccessException()));
        Assert.IsFalse(savedApplication);
    }

    [TestMethod]
    public void InstalledPreferenceIsAnExactPerUserDword()
    {
        string path = @"Software\DS4Windows-Tests\Tray-" + Guid.NewGuid().ToString("N");
        try
        {
            foreach (bool visible in new[] { false, true })
            {
                ViiperTrayPreference.WriteStartupPreference(visible, path);
                using var key = Registry.CurrentUser.OpenSubKey(path);
                Assert.AreEqual(RegistryValueKind.DWord, key.GetValueKind("ShowViiperTrayIcon"));
                Assert.AreEqual(visible ? 1 : 0, key.GetValue("ShowViiperTrayIcon"));
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }
}
