using System.Text;
using System.Xml.Linq;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ProfilePersistenceTests
{
    private string directory;
    private const string Original = "<DS4Windows><Color>1,2,3</Color></DS4Windows>";
    private const string Updated = "<DS4Windows><Color>4,5,6</Color></DS4Windows>";

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateTempSubdirectory("ds4w-profile-save-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    public void CreationAndReplacementWriteCompleteUtf8XmlWithoutTemporaryFiles()
    {
        string path = Path.Combine(directory, "Profile.xml");
        ProfilePersistence.Save(path, Original);
        Assert.AreEqual(Original, File.ReadAllText(path));
        ProfilePersistence.Save(path, Updated);
        CollectionAssert.AreEqual(new UTF8Encoding(false).GetBytes(Updated), File.ReadAllBytes(path));
        Assert.AreEqual("4,5,6", XDocument.Load(path).Root.Element("Color").Value);
        Assert.AreEqual(1, Directory.GetFiles(directory).Length);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InterruptedWritePreservesPreviousProfileOrLeavesNewDestinationAbsent(bool existing)
    {
        string path = Path.Combine(directory, "Profile.xml");
        if (existing) File.WriteAllText(path, Original);
        var failure = new IOException("Synthetic interrupted write");
        IOException observed = Assert.ThrowsException<IOException>(() => ProfilePersistence.Save(path, Updated,
            writeTemporary: (temporary, xml) =>
            {
                File.WriteAllText(temporary, "<DS4Windows>");
                throw failure;
            }));
        Assert.AreSame(failure, observed);
        Assert.AreEqual(existing, File.Exists(path));
        if (existing) Assert.AreEqual(Original, File.ReadAllText(path));
        Assert.AreEqual(existing ? 1 : 0, Directory.GetFiles(directory).Length);
    }

    [TestMethod]
    public void LockedDestinationPreservesPreviousProfileAndRemovesTemporaryFile()
    {
        string path = Path.Combine(directory, "Profile.xml");
        File.WriteAllText(path, Original);
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.ThrowsException<IOException>(() => ProfilePersistence.Save(path, Updated));
        Assert.AreEqual(Original, File.ReadAllText(path));
        Assert.AreEqual(1, Directory.GetFiles(directory).Length);
    }

    [TestMethod]
    public void ReadOnlyDestinationPreservesPreviousProfileAndRemovesTemporaryFile()
    {
        string path = Path.Combine(directory, "Profile.xml");
        File.WriteAllText(path, Original);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.ThrowsException<UnauthorizedAccessException>(() => ProfilePersistence.Save(path, Updated));
        Assert.AreEqual(Original, File.ReadAllText(path));
        Assert.AreEqual(1, Directory.GetFiles(directory).Length);
    }

    [TestMethod]
    public void ProfileSaveUsesExistingSerializerAndPreservesReadOnlyDestination()
    {
        string previousRoot = Global.appdatapath;
        try
        {
            Global.appdatapath = directory;
            string profiles = Directory.CreateDirectory(Path.Combine(directory, "Profiles")).FullName;
            string path = Path.Combine(profiles, "Candidate.xml");
            var store = new BackingStore();
            store.rumble[0] = 77;
            Assert.IsTrue(store.SaveProfileNew(0, "Candidate"));
            Assert.AreEqual("77", XDocument.Load(path).Root.Element("RumbleBoost").Value);
            byte[] previous = File.ReadAllBytes(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            store.rumble[0] = 88;
            Assert.IsFalse(store.SaveProfileNew(0, "Candidate"));
            CollectionAssert.AreEqual(previous, File.ReadAllBytes(path));
            Assert.AreEqual(1, Directory.GetFiles(profiles).Length);
        }
        finally { Global.appdatapath = previousRoot; }
    }
}
