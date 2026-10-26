using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ProfileAppearanceTests
{
    private string root;
    [TestInitialize] public void Initialize() => root = Directory.CreateTempSubdirectory("ds4w-profile-icons-").FullName;
    [TestCleanup] public void Cleanup()
    {
        Assert.IsTrue(Path.GetFileName(Path.GetFullPath(root)).StartsWith("ds4w-profile-icons-"));
        Directory.Delete(root, true);
    }

    private byte[] Png(int width = 256, int height = 128)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 20; pixels[i + 1] = 50; pixels[i + 2] = 100; pixels[i + 3] = (byte)(i % 8 == 0 ? 0 : 128); }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }

    [TestMethod]
    public void InitialUsesTheFirstUnicodeTextElementAndTracksRename()
    {
        var profile = new ProfileEntity { Name = "Default" };
        Assert.AreEqual("D", profile.IconLetter);
        profile.Name = "MAME"; Assert.AreEqual("M", profile.IconLetter);
        Assert.AreEqual("É", ProfileAppearance.Initial("  e\u0301clair").Normalize());
        Assert.AreEqual("🎮", ProfileAppearance.Initial("🎮 Games"));
        Assert.AreEqual("P", ProfileAppearance.Initial(" "));
    }

    [TestMethod]
    public void PngIsDetachedResizedAndRetainsTransparencyWithoutAnExternalPath()
    {
        string input = Path.Combine(root, "image.png"); File.WriteAllBytes(input, Png());
        var appearance = ProfileAppearance.Import(input, "#7950b5"); File.Delete(input);
        var image = appearance.Image;
        Assert.IsTrue(image.IsFrozen); Assert.AreEqual(128, image.PixelWidth); Assert.AreEqual(64, image.PixelHeight);
        Assert.AreEqual("#7950B5", appearance.Fill);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        Assert.IsTrue(pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha < 255));
        Assert.IsTrue(Convert.FromBase64String(appearance.Png).Length <= ProfileAppearance.MaximumPngBytes);
    }

    [TestMethod]
    public void IcoIsAcceptedAndEmbeddedAsPng()
    {
        byte[] png = Png(32, 32);
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((byte)32); writer.Write((byte)32); writer.Write((byte)0); writer.Write((byte)0);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(png.Length); writer.Write(22); writer.Write(png);
        string input = Path.Combine(root, "image.ico"); File.WriteAllBytes(input, output.ToArray());
        var appearance = ProfileAppearance.Import(input, null);
        Assert.AreEqual(32, appearance.Image.PixelWidth);
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, Convert.FromBase64String(appearance.Png).Take(4).ToArray());
    }

    [TestMethod]
    public void CorruptUnsupportedAndOversizedImagesAreRejected()
    {
        string path = Path.Combine(root, "invalid.png"); File.WriteAllText(path, "<svg>not a PNG</svg>");
        Assert.ThrowsExactly<InvalidDataException>(() => ProfileAppearance.Import(path, null));
        string other = Path.Combine(root, "invalid.jpg"); File.WriteAllBytes(other, Png());
        Assert.ThrowsExactly<InvalidDataException>(() => ProfileAppearance.Import(other, null));
        File.WriteAllBytes(path, new byte[2 * 1024 * 1024 + 1]);
        Assert.ThrowsExactly<InvalidDataException>(() => ProfileAppearance.Import(path, null));
        File.WriteAllBytes(path, Png(2049, 1));
        Assert.ThrowsExactly<InvalidDataException>(() => ProfileAppearance.Import(path, null));
        var xml = new XmlDocument { XmlResolver = null };
        xml.LoadXml("<DS4Windows><ProfileAppearance><Fill>#xyz123</Fill><Png>broken</Png></ProfileAppearance></DS4Windows>");
        Assert.AreEqual(new ProfileAppearance(), ProfileAppearance.Read(xml.DocumentElement));
    }

    [TestMethod]
    public void AppearanceTravelsWithTheProfileAndSurvivesNormalSavesAndReset()
    {
        string original = Path.Combine(root, "Original.xml");
        File.WriteAllText(original, "<DS4Windows><OutputContDevice>ViiperDualSense</OutputContDevice><Color>1,2,3</Color></DS4Windows>");
        var appearance = new ProfileAppearance("#7950B5", Convert.ToBase64String(Png(32, 32)));
        ProfilePersistence.SaveAppearance(original, appearance);
        ProfilePersistence.Save(original, "<DS4Windows><OutputContDevice>ViiperDualSense</OutputContDevice><Color>4,5,6</Color></DS4Windows>");
        var saved = XDocument.Load(original);
        Assert.AreEqual("4,5,6", saved.Root.Element("Color").Value);
        Assert.AreEqual(appearance.Png, saved.Root.Element("ProfileAppearance").Element("Png").Value);
        string copy = Path.Combine(root, "Copy.xml"); File.Copy(original, copy);
        string renamed = Path.Combine(root, "Renamed.xml"); File.Move(copy, renamed);
        Assert.AreEqual(appearance.Png, XDocument.Load(renamed).Root.Element("ProfileAppearance").Element("Png").Value);
        ProfilePersistence.SaveAppearance(renamed, new());
        Assert.IsNull(XDocument.Load(renamed).Root.Element("ProfileAppearance"));
        Assert.IsNotNull(XDocument.Load(original).Root.Element("ProfileAppearance"), "Resetting one profile must not change another.");
    }

    [TestMethod]
    public void ViewChoiceIsRememberedSeparatelyAndDefaultsToTiles()
    {
        string program = Path.Combine(root, "Program"), appData = Path.Combine(root, "Roaming"), prefs = Path.Combine(root, "Preferences");
        var storage = new SettingsStorage(program, appData, prefs);
        Assert.AreEqual(ProfileViewChoice.Tiles, storage.ProfileView);
        storage.Remember(SettingsFolder.Program); storage.SetStorageExpanded(true);
        storage.SetProfileView(ProfileViewChoice.Details);
        var again = new SettingsStorage(program, appData, prefs);
        Assert.AreEqual(ProfileViewChoice.Details, again.ProfileView);
        Assert.AreEqual(SettingsFolder.Program, again.Selected); Assert.IsTrue(again.StorageExpanded);
        again.SetProfileView(ProfileViewChoice.List);
        Assert.AreEqual(ProfileViewChoice.List, storage.ProfileView);
    }
}
