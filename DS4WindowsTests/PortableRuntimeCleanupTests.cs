using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class PortableRuntimeCleanupTests
{
    [TestMethod]
    public void RemovesOnlyExactUnownedDuplicatesFromTheRootAndCanRunAgain()
    {
        using var fixture = new Fixture();
        fixture.WritePair("coreclr.dll", "same", "same");
        fixture.WritePair("createdump.exe", "same", "same");
        fixture.WritePair("hostfxr.dll", "new", "old");
        fixture.WritePair("DS4Windows.dll", "new", "old");
        fixture.WritePair("DS4Windows.deps.json", "same config", "same config");
        fixture.WritePair("Owned.dll", "same", "same");
        fixture.WritePair("Unlisted.dll", "same", "same");
        fixture.WritePair("Profiles.xml", "same", "same");
        fixture.WriteManifest("Runtime/coreclr.dll", "Runtime/createdump.exe", "Runtime/hostfxr.dll",
            "Runtime/DS4Windows.dll", "Runtime/DS4Windows.deps.json", "Runtime/Owned.dll", "Owned.dll", "Runtime/Profiles.xml");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "extras"));
        File.WriteAllText(Path.Combine(fixture.Root, "extras", "coreclr.dll"), "same");

        Assert.AreEqual(3, PortableRuntimeCleanup.RemoveLeftovers(fixture.Root, fixture.Runtime));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "DS4Windows.deps.json")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "coreclr.dll")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "createdump.exe")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Runtime, "coreclr.dll")));
        foreach (string name in new[] { "hostfxr.dll", "DS4Windows.dll", "Owned.dll", "Unlisted.dll",
                     "Profiles.xml", Path.Combine("extras", "coreclr.dll") })
            Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, name)), name);
        Assert.AreEqual(0, PortableRuntimeCleanup.RemoveLeftovers(fixture.Root, fixture.Runtime));
    }

    [TestMethod]
    public void KeepsInUseFilesAndFilesWithoutAnActiveSplitLayout()
    {
        using var fixture = new Fixture();
        fixture.WritePair("hostfxr.dll", "same", "same");
        string leftover = Path.Combine(fixture.Root, "hostfxr.dll");
        Assert.AreEqual(0, PortableRuntimeCleanup.RemoveLeftovers(fixture.Root, fixture.Runtime));
        fixture.WriteManifest("Runtime/hostfxr.dll");
        Assert.AreEqual(0, PortableRuntimeCleanup.RemoveLeftovers(fixture.Root, fixture.Root));
        using (File.Open(leftover, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.AreEqual(0, PortableRuntimeCleanup.RemoveLeftovers(fixture.Root, fixture.Runtime));
        Assert.IsTrue(File.Exists(leftover));
        Assert.AreEqual(1, PortableRuntimeCleanup.RemoveLeftovers(fixture.Root, fixture.Runtime));
    }

    [TestMethod]
    public void KeepsUnverifiedFilesEvenWithKnownVersionedDiagnosticsNames()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Runtime, "mscordaccore.dll"), "installed DAC");
        fixture.WriteManifest("Runtime/mscordaccore.dll");
        string legacy = Path.Combine(fixture.Root, "mscordaccore_amd64_amd64_10.0.1426.52326.dll");
        using (var different = File.Create(legacy))
            different.SetLength(1356120);

        Assert.AreEqual(0, PortableRuntimeCleanup.RemoveLeftovers(fixture.Root, fixture.Runtime));
        Assert.IsTrue(File.Exists(legacy));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("ds4w-runtime-test-").FullName;
        internal string Runtime => Path.Combine(Root, "Runtime");

        internal Fixture() => Directory.CreateDirectory(Runtime);

        internal void WritePair(string name, string packaged, string leftover)
        {
            File.WriteAllText(Path.Combine(Runtime, name), packaged);
            File.WriteAllText(Path.Combine(Root, name), leftover);
        }

        internal void WriteManifest(params string[] files) =>
            File.WriteAllLines(Path.Combine(Root, ".ds4windows-managed-files.txt"), files);

        public void Dispose()
        {
            Assert.IsTrue(Path.GetFileName(Path.GetFullPath(Root)).StartsWith("ds4w-runtime-test-"));
            Directory.Delete(Root, recursive: true);
        }
    }
}
