using System.Text;
using System.Xml;
using System.Xml.Serialization;
using DS4Windows;
using DS4Windows.DS4Control;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

/// <summary>
/// The ProfileDTO serializer uses XmlAttributeOverrides, which .NET does not
/// cache. These tests check it is built once and that profile preparation does
/// not load a new assembly per call.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ProfileSerializerCacheTests
{
    private string folder;

    [TestInitialize]
    public void Setup()
    {
        folder = Path.Combine(Path.GetTempPath(), $"ds4w-serializer-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    [TestMethod]
    public void Serializer_ReturnsSameInstance()
    {
        Assert.IsNotNull(ProfileDTO.Serializer);
        Assert.AreSame(ProfileDTO.Serializer, ProfileDTO.Serializer);
    }

    [TestMethod]
    public void RepeatedTryPrepare_DoesNotGrowAssemblyCount()
    {
        string path = Path.Combine(folder, "Aim.xml");
        File.WriteAllText(path, CreateSavedProfileXml(
            "custom", "0.72, 0.26, 1.00, 1.00"), Encoding.UTF8);

        void Prepare()
        {
            bool ok = PreparedProfileLoad.TryPrepare(path, 0, out _, out _, out string error);
            Assert.IsTrue(ok, error);
        }

        for (int i = 0; i < 3; i++)
            Prepare();
        int before = AppDomain.CurrentDomain.GetAssemblies().Length;
        for (int i = 0; i < 20; i++)
            Prepare();
        int after = AppDomain.CurrentDomain.GetAssemblies().Length;

        Assert.AreEqual(before, after, "TryPrepare loaded new assemblies per call.");
    }

    // Builds a profile the way BackingStore.SaveProfileNew writes one.
    private static string CreateSavedProfileXml(string rsCurveMode, string rsCurveCustom)
    {
        var serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
        using var strWriter = new Utf8StringWriter();
        using (XmlWriter xmlWriter = XmlWriter.Create(strWriter,
            new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = true }))
        {
            xmlWriter.WriteComment(" DS4Windows Configuration Data. test ");
            xmlWriter.WriteWhitespace("\r\n\r\n");
            var dto = new ProfileDTO { DeviceIndex = 0 };
            dto.MapFrom(new BackingStore());
            dto.RSOutputCurveMode = rsCurveMode;
            dto.RSOutputCurveCustom = rsCurveCustom;
            serializer.Serialize(xmlWriter, dto,
                new XmlSerializerNamespaces(new[] { XmlQualifiedName.Empty }));
        }
        return strWriter.ToString();
    }
}
