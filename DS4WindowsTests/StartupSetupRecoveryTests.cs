using DS4Windows.Installation;

namespace DS4WindowsTests;

[TestClass]
public sealed class StartupSetupRecoveryTests
{
    private const string Id = "aaaabbbbccccddddeeeeffff11112222";
    private const string Sid = "S-1-5-21-100-200-300-1001";

    [TestMethod]
    public void LegacyResumeCleanupRequiresActualTargetArgumentsAndWorkingDirectory()
    {
        const string directory = @"C:\ProgramData\DS4Windows\Installer\resume";
        string executable = Path.Combine(directory, "DS4Windows_Setup_x64.exe");
        const string shortcut = @"C:\Users\Example\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\DS4Windows Setup Resume.lnk";
        Assert.IsTrue(StartupSetupRecovery.IsOwnedLegacyShortcut(shortcut, executable, "/repair", directory, executable));
        Assert.IsFalse(StartupSetupRecovery.IsOwnedLegacyShortcut(shortcut, @"C:\Other\app.exe", "/repair", directory, executable));
        Assert.IsFalse(StartupSetupRecovery.IsOwnedLegacyShortcut(shortcut, executable, "/uninstall", directory, executable));
        Assert.IsFalse(StartupSetupRecovery.IsOwnedLegacyShortcut(shortcut, executable, "/repair --other", directory, executable));
        Assert.IsFalse(StartupSetupRecovery.IsOwnedLegacyShortcut(shortcut, executable, "/repair", @"C:\Other", executable));
        Assert.IsFalse(StartupSetupRecovery.IsOwnedLegacyShortcut(shortcut.Replace("Startup", "Documents"), executable, "/repair", directory, executable));
        Assert.IsFalse(StartupSetupRecovery.IsOwnedLegacyShortcut(shortcut + ".other", executable, "/repair", directory, executable));
        Assert.IsFalse(StartupSetupRecovery.IsOwnedLegacyShortcut("DS4Windows Setup Resume.lnk", executable, "/repair", directory, executable));
    }

    [TestMethod]
    [DataRow("boot-a", null, false)]
    [DataRow("boot-b", null, true)]
    [DataRow("boot-b", Id, false)]
    [DataRow("", null, false)]
    public void ResumeRequiresAnotherBootAndOneAttempt(string boot, string attempted, bool expected)
    {
        var pending = Pending();
        Assert.AreEqual(expected, StartupSetupRecovery.IsEligible(pending, Id, Sid, boot, attempted));
        Assert.IsFalse(StartupSetupRecovery.IsEligible(pending, Id, Sid + "0", boot, attempted));
        Assert.IsFalse(StartupSetupRecovery.IsEligible(pending, "bad", Sid, boot, attempted));
    }

    [TestMethod]
    public void BundleContinuationRejectsAnotherBundleAndDoesNotDependOnPersistedOriginalSource()
    {
        var pending = Pending();
        pending.Kind = "bundle";
        pending.BundleId = "{11111111-2222-3333-4444-555555555555}";
        Assert.IsTrue(StartupSetupRecovery.MatchesBundle(pending, pending.BundleId));
        Assert.IsFalse(StartupSetupRecovery.MatchesBundle(pending, "{11111111-2222-3333-4444-666666666666}"));
        Assert.IsFalse(StartupSetupRecovery.MatchesBundle(pending, ""));
        pending.Kind = "embedded";
        Assert.IsFalse(StartupSetupRecovery.MatchesBundle(pending, pending.BundleId));
    }

    [TestMethod]
    public void RepeatedLogonCannotVerifyOrStartRecoveryBeforeReboot()
    {
        Action unexpected = () => throw new AssertFailedException("An ineligible resume touched its environment.");
        Assert.IsFalse(StartupSetupRecovery.Claim(Pending(), Id, Sid, "boot-a", null, false,
            unexpected, unexpected, unexpected));
    }

    [TestMethod]
    public void ChangedExecutableDoesNotConsumeTheResumeAttempt()
    {
        bool marked = false, removed = false;
        Assert.ThrowsExactly<IOException>(() => StartupSetupRecovery.Claim(Pending(), Id, Sid, "boot-b", null,
            false, () => throw new IOException("changed"), () => marked = true, () => removed = true));
        Assert.IsFalse(marked);
        Assert.IsFalse(removed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void VerifiedCleanupFailureCannotSuppressRepairOrOverrideCancellation(bool canceled)
    {
        var events = new List<string>();
        bool resume = StartupSetupRecovery.Claim(Pending(), Id, Sid, "boot-b", null, canceled,
            () => events.Add("verify"), () => events.Add("mark"),
            () => { events.Add("remove"); throw new IOException("shortcut busy"); });
        Assert.AreEqual(!canceled, resume);
        CollectionAssert.AreEqual(new[] { "verify", "mark", "remove" }, events);
    }

    [TestMethod]
    public void ASecondRequiredRebootGetsANewAttemptOnTheSameVerifiedSnapshot()
    {
        var second = Pending();
        second.Id = "bbbbccccddddeeeeffff111122223333";
        second.BootSessionId = "boot-b";
        Assert.IsFalse(StartupSetupRecovery.IsEligible(second, second.Id, Sid, "boot-b", Id));
        Assert.IsTrue(StartupSetupRecovery.IsEligible(second, second.Id, Sid, "boot-c", Id));
        Assert.AreEqual(StartupSetupRecovery.ExpectedExecutable(Pending()), StartupSetupRecovery.ExpectedExecutable(second));
    }

    [TestMethod]
    [DataRow("embedded")]
    [DataRow("bundle")]
    public void ResumeTargetIsDerivedFromProtectedSnapshotAndCannotBeAnArbitraryExecutable(string kind)
    {
        var pending = Pending();
        pending.Kind = kind;
        if (kind == "bundle") pending.BundleId = "{11111111-2222-3333-4444-555555555555}";
        string expected = StartupSetupRecovery.ExpectedExecutable(pending);
        Assert.IsTrue(expected.StartsWith(Path.Combine(StartupSetupRecovery.NativeProgramFiles, "DS4Windows.Setup") + "\\",
            StringComparison.OrdinalIgnoreCase));
        pending.Executable = Path.Combine(Path.GetTempPath(), "foreign.exe");
        Assert.ThrowsExactly<InvalidDataException>(() => StartupSetupRecovery.Validate(pending));
        pending.Executable = expected;
        pending.SnapshotId = "..\\escape";
        Assert.ThrowsExactly<InvalidDataException>(() => StartupSetupRecovery.Validate(pending));
    }

    private static StartupSetupResume Pending() => new()
    {
        Id = Id, SnapshotId = Id, Kind = "embedded", TargetSid = Sid,
        BootSessionId = "boot-a", StartupRequested = true,
    };
}
