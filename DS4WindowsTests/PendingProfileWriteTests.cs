using DS4Windows;
namespace DS4WindowsTests;
[TestClass]
[DoNotParallelize]
public class PendingProfileWriteTests
{
    [TestMethod]
    public void RetryPreservesSnapshotAndOriginalProfileAfterSelectionChanges()
    {
        string oldRoot = Global.appdatapath, oldProfile = Global.ProfilePath[0];
        string root = Directory.CreateTempSubdirectory("ds4w-retry-").FullName;
        try
        {
            Global.appdatapath = root;
            Directory.CreateDirectory(Path.Combine(root, "Profiles"));
            string original = Path.Combine(root, "Profiles", "Original.xml");
            string other = Path.Combine(root, "Profiles", "Other.xml");
            File.WriteAllText(original, "old"); File.WriteAllText(other, "other");
            var pending = new PendingProfileWrite(0, "Original", "captured settings");
            using (var held = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.IsFalse(pending.TrySave());
            Global.ProfilePath[0] = "Other";
            Assert.IsTrue(pending.TrySave());
            Assert.AreEqual("captured settings", File.ReadAllText(original));
            Assert.AreEqual("other", File.ReadAllText(other));
            Assert.AreEqual("Other", Global.ProfilePath[0]);
        }
        finally { Global.appdatapath = oldRoot; Global.ProfilePath[0] = oldProfile; Directory.Delete(root, true); }
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RetryCannotOverwriteAnEditedOrDeletedProfile(bool delete)
    {
        string oldRoot = Global.appdatapath;
        string root = Directory.CreateTempSubdirectory("ds4w-retry-").FullName;
        try
        {
            Global.appdatapath = root; Directory.CreateDirectory(Path.Combine(root, "Profiles"));
            string path = Path.Combine(root, "Profiles", "Original.xml"); File.WriteAllText(path, "old");
            var pending = new PendingProfileWrite(0, "Original", "stale snapshot");
            if (delete) File.Delete(path); else File.WriteAllText(path, "new edit");
            Assert.IsFalse(pending.TrySave());
            if (delete) Assert.IsFalse(File.Exists(path)); else Assert.AreEqual("new edit", File.ReadAllText(path));
        }
        finally { Global.appdatapath = oldRoot; Directory.Delete(root, true); }
    }
}
