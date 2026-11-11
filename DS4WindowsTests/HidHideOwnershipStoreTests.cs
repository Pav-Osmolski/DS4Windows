using DS4Windows;
using System.Text.Json;

namespace DS4WindowsTests;

[TestClass]
public sealed class HidHideOwnershipStoreTests
{
    private string directory;
    private const string Controller = @"HID\VID_054C&PID_0CE6\PHYSICAL";
    private const string External = @"HID\OTHER_MAPPER";

    [TestInitialize]
    public void Initialize() => directory = Path.Combine(Path.GetTempPath(), "DS4W-HidHide-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [TestMethod]
    public void ConfirmedCrashOrphanIsRecoveredByNextProcess()
    {
        using (var first = new HidHideOwnershipStore(directory))
        using (var tx = first.Open())
        {
            CollectionAssert.AreEqual(new[] { Controller }, tx.Claim(new[] { Controller }, Array.Empty<string>()));
            tx.ConfirmAdded(new[] { Controller });
        }
        using var successor = new HidHideOwnershipStore(directory);
        using var recovery = successor.Open();
        CollectionAssert.AreEqual(new[] { Controller }, recovery.StaleIds);
        CollectionAssert.AreEqual(new[] { Controller }, recovery.Release(recovery.StaleIds));
        recovery.ConfirmAbsent(new[] { External });
        Assert.AreEqual(0, recovery.StaleIds.Length);
    }

    [TestMethod]
    public void LiveOwnerCannotBeRecoveredAndLastBorrowerOwnsRelease()
    {
        using var first = new HidHideOwnershipStore(directory);
        using var second = new HidHideOwnershipStore(directory);
        using (var tx = first.Open())
        {
            tx.Claim(new[] { Controller }, Array.Empty<string>());
            tx.ConfirmAdded(new[] { Controller });
        }
        using (var tx = second.Open())
        {
            Assert.AreEqual(0, tx.StaleIds.Length);
            Assert.IsTrue(tx.IsProtected(Controller));
            CollectionAssert.AreEqual(new[] { Controller }, tx.Claim(new[] { Controller }, new[] { Controller }));
        }
        using (var tx = first.Open())
        {
            Assert.AreEqual(0, tx.Release(new[] { Controller }).Length,
                "One copy stopping must not remove another copy's live hiding rule.");
            Assert.AreEqual(0, tx.CurrentOwnedIds.Length);
        }
        using (var tx = second.Open())
        {
            CollectionAssert.AreEqual(new[] { Controller }, tx.Release(new[] { Controller }));
            tx.ConfirmAbsent(Array.Empty<string>());
        }
    }

    [TestMethod]
    public void ExistingUntrackedRulesAreNeverAutomaticallyAdopted()
    {
        using var owner = new HidHideOwnershipStore(directory);
        using var tx = owner.Open();
        Assert.AreEqual(0, tx.Claim(new[] { Controller, External }, new[] { Controller, External }).Length);
        Assert.AreEqual(0, tx.Release(new[] { Controller, External }).Length);
        Assert.AreEqual(0, tx.StaleIds.Length);
    }

    [TestMethod]
    public void CrashBeforeConfirmationRequiresExplicitRecovery()
    {
        using (var first = new HidHideOwnershipStore(directory))
        using (var tx = first.Open()) tx.Claim(new[] { Controller }, Array.Empty<string>());
        using var successor = new HidHideOwnershipStore(directory);
        using var recovery = successor.Open();
        Assert.AreEqual(0, recovery.StaleIds.Length);
        Assert.AreEqual(0, recovery.Claim(new[] { Controller }, new[] { Controller }).Length);
        Assert.AreEqual(0, recovery.Release(new[] { Controller }).Length);
    }

    [TestMethod]
    public void CrashAfterReleaseIntentRetainsProofUntilDriverReadback()
    {
        using (var first = new HidHideOwnershipStore(directory))
        using (var tx = first.Open())
        {
            tx.Claim(new[] { Controller }, Array.Empty<string>());
            tx.ConfirmAdded(new[] { Controller });
            tx.Release(new[] { Controller }); // Simulate failed removal IOCTL.
        }
        using var successor = new HidHideOwnershipStore(directory);
        using var recovery = successor.Open();
        CollectionAssert.AreEqual(new[] { Controller }, recovery.StaleIds);
        recovery.ConfirmAbsent(new[] { Controller });
        CollectionAssert.AreEqual(new[] { Controller }, recovery.StaleIds);
        recovery.ConfirmAbsent(Array.Empty<string>());
        Assert.AreEqual(0, recovery.StaleIds.Length);
    }

    [TestMethod]
    public void ReacquisitionBeforeReleaseConfirmationProtectsTheRule()
    {
        using var owner = new HidHideOwnershipStore(directory);
        using var tx = owner.Open();
        tx.Claim(new[] { Controller }, Array.Empty<string>());
        tx.ConfirmAdded(new[] { Controller });
        tx.Release(new[] { Controller });
        tx.Claim(new[] { Controller }, Array.Empty<string>());
        tx.ConfirmAdded(new[] { Controller });
        tx.ConfirmAbsent(new[] { Controller });
        Assert.IsTrue(tx.IsProtected(Controller));
        Assert.AreEqual(0, tx.StaleIds.Length);
    }

    [TestMethod]
    public void CorruptJournalFailsClosedAndReleasesFileLock()
    {
        using var owner = new HidHideOwnershipStore(directory);
        string path = Path.Combine(directory, "ownership.json");
        File.WriteAllText(path, "broken");
        Assert.ThrowsExactly<JsonException>(() => owner.Open());
        Assert.AreEqual("broken", File.ReadAllText(path));
        File.WriteAllText(path, "{\"Version\":1,\"Rules\":[]}");
        using var tx = owner.Open();
        Assert.AreEqual(0, tx.StaleIds.Length);
    }

    [TestMethod]
    public void MissingLeaseAfterCrashDoesNotBlockConfirmedRecovery()
    {
        string token;
        using (var first = new HidHideOwnershipStore(directory))
        {
            token = first.Owner;
            using var tx = first.Open();
            tx.Claim(new[] { Controller }, Array.Empty<string>());
            tx.ConfirmAdded(new[] { Controller });
        }
        File.Delete(Path.Combine(directory, token + ".lease"));
        using var successor = new HidHideOwnershipStore(directory);
        using var recovery = successor.Open();
        CollectionAssert.AreEqual(new[] { Controller }, recovery.StaleIds);
    }

    [TestMethod]
    public void UncertainOwnerIsNotReclaimed()
    {
        using (var first = new HidHideOwnershipStore(directory))
        using (var tx = first.Open())
        {
            tx.Claim(new[] { Controller }, Array.Empty<string>());
            tx.ConfirmAdded(new[] { Controller });
        }
        using var uncertain = new HidHideOwnershipStore(directory, _ => true);
        using var query = uncertain.Open();
        Assert.AreEqual(0, query.StaleIds.Length);
        Assert.IsTrue(query.IsProtected(Controller));
    }
}
