using System.Diagnostics;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
public class ViiperDiagnosticsTests
{
    [TestMethod]
    public void OldSettingsDefaultOffAndDiagnosticPreferenceRoundTrips()
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var old = new StringReader("<Profile />");
        var store = new BackingStore();
        ((AppSettingsDTO)serializer.Deserialize(old)).MapTo(store);
        Assert.IsFalse(store.viiperDebugLogging);
        foreach (bool enabled in new[] { true, false })
        {
            store.viiperDebugLogging = enabled;
            var dto = new AppSettingsDTO();
            dto.MapFrom(store);
            using var writer = new StringWriter();
            serializer.Serialize(writer, dto);
            using var reader = new StringReader(writer.ToString());
            ((AppSettingsDTO)serializer.Deserialize(reader)).MapTo(store);
            Assert.AreEqual(enabled, store.viiperDebugLogging);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DiagnosticsUseOneLiteralPathAndNeverEnableRawCapture(bool enabled)
    {
        var start = new ProcessStartInfo("viiper.exe");
        string path = Path.Combine(Path.GetTempPath(), "diagnostics with spaces", "viiper-debug.log");
        ViiperDiagnostics.AddArguments(start, enabled, path);
        CollectionAssert.AreEqual(new[] {
            enabled ? "--log.level=debug" : "--log.level=warn",
            enabled ? "--log.file=" + path : "--log.file=", "--log.raw-file="
        }, start.ArgumentList.ToArray());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void InstalledCaptureBypassesTaskWithoutChangingAuthorityOrElevation(bool administrator)
    {
        string image = @"C:\Program Files\DS4Windows\VIIPER\viiper.exe";
        string path = @"C:\Users\Test User\Logs\viiper-debug.log";
        var start = ViiperSetupManager.CreateViiperServerStartInfo(image, true,
            administrator, @"C:\Windows\System32", debugLogging: true, logPath: path);
        Assert.AreEqual(image, start.FileName);
        Assert.AreEqual(administrator ? "" : "runas", start.Verb);
        Assert.IsTrue(start.UseShellExecute);
        Assert.AreEqual(ProcessWindowStyle.Hidden, start.WindowStyle);
        CollectionAssert.Contains(start.ArgumentList.ToArray(), "--log.file=" + path);
        CollectionAssert.Contains(start.ArgumentList.ToArray(), "--log.raw-file=");
        CollectionAssert.Contains(start.ArgumentList.ToArray(), "--usb.retained-import-authority-id=4923336367393615921");
        var normal = ViiperSetupManager.CreateViiperServerStartInfo(image, true,
            administrator, @"C:\Windows\System32", debugLogging: false);
        Assert.AreEqual(@"C:\Windows\System32\schtasks.exe", normal.FileName);
    }

    [TestMethod]
    public void NewCapturePreservesOnlyPreviousAndCleanupKeepsActiveCapture()
    {
        string root = Path.Combine(Path.GetTempPath(), "viiper-diagnostics-" + Guid.NewGuid().ToString("N"));
        string active = Path.Combine(root, ViiperDiagnostics.FileName);
        string previous = Path.Combine(root, ViiperDiagnostics.PreviousFileName);
        try
        {
            ViiperDiagnostics.PrepareLogFile(active);
            Assert.IsFalse(File.Exists(active));
            File.WriteAllText(active, "first capture");
            ViiperDiagnostics.PrepareLogFile(active);
            Assert.AreEqual("first capture", File.ReadAllText(previous));
            File.WriteAllText(active, "second capture");
            ViiperDiagnostics.PrepareLogFile(active);
            Assert.AreEqual("second capture", File.ReadAllText(previous));
            File.WriteAllText(active, "current capture");
            File.WriteAllText(Path.Combine(root, "unrelated.log"), "keep");
            var result = StoredLogManager.Cleanup(root, 30, DateTime.UtcNow, clearAll: true);
            Assert.AreEqual(1, result.Deleted);
            Assert.AreEqual("current capture", File.ReadAllText(active));
            Assert.IsTrue(File.Exists(Path.Combine(root, "unrelated.log")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
