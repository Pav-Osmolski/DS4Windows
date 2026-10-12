using DS4Windows;
using DS4WinWPF;
namespace DS4WindowsTests;
[TestClass]
[DoNotParallelize]
public class ProfileRenameTests
{
    private string root, oldRoot;
    [TestInitialize] public void Setup() { oldRoot = Global.appdatapath; root = Directory.CreateTempSubdirectory("ds4w-rename-").FullName; Global.appdatapath = root; Directory.CreateDirectory(Path.Combine(root, "Profiles")); }
    [TestCleanup] public void Cleanup() { Global.appdatapath = oldRoot; Directory.Delete(root, true); }
    private string PathFor(string name) => Path.Combine(root, "Profiles", name + ".xml");
    [TestMethod] public void CollisionPreservesBothFilesAndNameWithActionableError()
    {
        File.WriteAllText(PathFor("Old"), "old"); File.WriteAllText(PathFor("Other"), "other");
        var entity = new ProfileEntity { Name = "Old" }; int events = 0; entity.NameChanged += (_, _) => events++;
        Assert.IsFalse(entity.RenameProfile("Other.xml", out string error)); Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        Assert.AreEqual("Old", entity.Name); Assert.AreEqual(0, events);
        Assert.AreEqual("old", File.ReadAllText(PathFor("Old"))); Assert.AreEqual("other", File.ReadAllText(PathFor("Other")));
    }
    [TestMethod] public void LockedRenameCanRetryAndPreservesExactSerializedBytes()
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF, 60, 120, 47, 62 }; File.WriteAllBytes(PathFor("Old"), bytes);
        var entity = new ProfileEntity { Name = "Old" };
        using (var held = new FileStream(PathFor("Old"), FileMode.Open, FileAccess.Read, FileShare.Read))
        { Assert.IsFalse(entity.RenameProfile("New.xml", out string error)); Assert.IsFalse(string.IsNullOrWhiteSpace(error)); Assert.AreEqual("Old", entity.Name); }
        Assert.IsTrue(entity.RenameProfile("New.xml", out _)); Assert.AreEqual("New", entity.Name);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(PathFor("New"))); Assert.IsFalse(File.Exists(PathFor("Old")));
    }
    [TestMethod] public void CaseOnlyAndUnchangedNamesSucceed()
    {
        File.WriteAllText(PathFor("Old"), "old"); var entity = new ProfileEntity { Name = "Old" };
        Assert.IsTrue(entity.RenameProfile("Old", out _)); Assert.IsTrue(entity.RenameProfile("old", out _));
        Assert.AreEqual("old", entity.Name); Assert.AreEqual(1, Directory.GetFiles(Path.Combine(root, "Profiles")).Length);
    }
    [TestMethod] public void MissingSourceReportsFailureWithoutChangingName()
    { var entity = new ProfileEntity { Name = "Missing" }; Assert.IsFalse(entity.RenameProfile("New", out string error)); Assert.IsFalse(string.IsNullOrWhiteSpace(error)); Assert.AreEqual("Missing", entity.Name); }
}
