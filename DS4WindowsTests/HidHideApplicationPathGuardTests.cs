using DS4WinWPF.DS4Control;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DS4WindowsTests
{
    [TestClass]
    public class HidHideApplicationPathGuardTests
    {
        [TestMethod]
        [DataRow(@"C:\Apps\DS4Windows.exe", @"\\?\C:\Apps\DS4Windows.exe")]
        [DataRow(@"c:/Program Files/App/app.exe", @"\\?\c:\Program Files\App\app.exe")]
        [DataRow(@"\Device\HarddiskVolume3\Apps\app.exe", @"\\?\GLOBALROOT\Device\HarddiskVolume3\Apps\app.exe")]
        [DataRow(@"\device\harddiskvolume123\Apps\app.exe", @"\\?\GLOBALROOT\device\harddiskvolume123\Apps\app.exe")]
        public void AdmittedPathsUseOnlyBoundedNativeNamespaces(string path,
            string expectedNativePath)
        {
            var metadata = new FakeMetadataReader();

            Assert.IsTrue(HidHideApplicationPathGuard.TryInspect(path,
                metadata.Read, out bool alias, out int error));

            Assert.AreEqual(expectedNativePath, metadata.Path);
            Assert.AreEqual(1, metadata.Calls);
            Assert.IsFalse(alias);
            Assert.AreEqual(0, error);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow("app.exe")]
        [DataRow(@"C:app.exe")]
        [DataRow(@"\app.exe")]
        [DataRow(@"\\server\share\app.exe")]
        [DataRow(@"\\?\UNC\server\share\app.exe")]
        [DataRow(@"\\?\C:\Apps\app.exe")]
        [DataRow(@"\\?\GLOBALROOT\Device\HarddiskVolume3\Apps\app.exe")]
        [DataRow(@"\\.\PhysicalDrive0")]
        [DataRow(@"\Device\NamedPipe\app.exe")]
        [DataRow(@"\Device\HarddiskVolumeShadowCopy3\app.exe")]
        [DataRow(@"\Device\HarddiskVolume\app.exe")]
        [DataRow(@"\Device\HarddiskVolume3evil\app.exe")]
        [DataRow(@"\Device\HarddiskVolume3")]
        [DataRow(@"\Device\HarddiskVolume3\")]
        [DataRow(@"C:\")]
        [DataRow(@"C:\Apps\")]
        [DataRow(@"C:\Apps\\app.exe")]
        [DataRow(@"C:\Apps\..\app.exe")]
        [DataRow(@"C:\Apps\.\app.exe")]
        [DataRow(@"C:\Apps\app.exe:stream")]
        [DataRow(@"C:\Apps\app*.exe")]
        [DataRow(@"C:\Apps\app?.exe")]
        [DataRow(@"C:\Apps\app.exe.")]
        [DataRow(@"C:\Apps\app.exe ")]
        [DataRow("C:\\Apps\\app\0.exe")]
        public void InvalidPathsNeverReachNativeInspection(string path)
        {
            var metadata = new FakeMetadataReader();

            Assert.IsFalse(HidHideApplicationPathGuard.TryInspect(path,
                metadata.Read, out bool alias, out int error));

            Assert.AreEqual(0, metadata.Calls);
            Assert.IsFalse(alias);
            Assert.AreEqual(123, error);
        }

        [TestMethod]
        [DataRow(0x400u, 0x8000001Bu, true)]
        [DataRow(0x420u, 0x8000001Bu, true)]
        [DataRow(0x80u, 0u, false)]
        [DataRow(0x80u, 0x8000001Bu, false)]
        [DataRow(0x400u, 0xA000000Cu, false)] // Ordinary symbolic link.
        [DataRow(0x400u, 0xA0000003u, false)] // Mount-point tag, not APPEXECLINK.
        [DataRow(0x400u, 0x9000001Au, false)] // Cloud placeholder.
        public void OnlyExactAppExecutionAliasMetadataIsAnAlias(
            uint attributes, uint tag, bool expectedAlias)
        {
            var metadata = new FakeMetadataReader { Attributes = attributes, Tag = tag };

            Assert.IsTrue(HidHideApplicationPathGuard.TryInspect(
                @"C:\AnyFolder\app.exe", metadata.Read, out bool alias, out int error));

            Assert.AreEqual(expectedAlias, alias);
            Assert.AreEqual(0, error);
            Assert.AreEqual(1, metadata.Calls,
                "Inspection must not attempt to resolve or inspect an alias target.");
        }

        [TestMethod]
        public void WindowsAppsFolderNameAloneDoesNotIdentifyAnAlias()
        {
            var metadata = new FakeMetadataReader();

            Assert.IsTrue(HidHideApplicationPathGuard.TryInspect(
                @"C:\Users\User\AppData\Local\Microsoft\WindowsApps\python.exe",
                metadata.Read, out bool alias, out int error));

            Assert.IsFalse(alias);
            Assert.AreEqual(0, error);
        }

        [TestMethod]
        [DataRow(2)] // Missing file.
        [DataRow(3)] // Missing parent.
        [DataRow(5)] // Access denied.
        [DataRow(32)] // Sharing violation.
        [DataRow(1920)] // Cannot access file: not proof of APPEXECLINK.
        public void NativeFailureRemainsUnknownInsteadOfAnAlias(int expectedError)
        {
            var metadata = new FakeMetadataReader
            {
                Success = false, Error = expectedError,
                Attributes = 0x400, Tag = 0x8000001B,
            };

            Assert.IsFalse(HidHideApplicationPathGuard.TryInspect(
                @"C:\Apps\app.exe", metadata.Read, out bool alias, out int error));

            Assert.IsFalse(alias);
            Assert.AreEqual(expectedError, error);
        }

        [TestMethod]
        public void DirectoryIsNotAnApplicationFile()
        {
            var metadata = new FakeMetadataReader { Attributes = 0x10 };

            Assert.IsFalse(HidHideApplicationPathGuard.TryInspect(
                @"C:\Apps", metadata.Read, out bool alias, out int error));

            Assert.IsFalse(alias);
            Assert.AreEqual(267, error);
        }

        [TestMethod]
        public void NativeInspectionReadsExistingFileWithoutChangingIt()
        {
            string path = System.IO.Path.GetTempFileName();
            byte[] contents = { 0x4D, 0x5A, 1, 2, 3 };
            try
            {
                File.WriteAllBytes(path, contents);
                DateTime writeTime = File.GetLastWriteTimeUtc(path);

                Assert.IsTrue(HidHideApplicationPathGuard.TryInspect(path,
                    out bool alias, out int error));

                Assert.IsFalse(alias);
                Assert.AreEqual(0, error);
                Assert.IsTrue(DS4Windows.ControlService.CanRegisterHidHideApplication(
                    path, out string rejection), rejection);
                CollectionAssert.AreEqual(contents, File.ReadAllBytes(path));
                Assert.AreEqual(writeTime, File.GetLastWriteTimeUtc(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void NativeInspectionRejectsMissingFilesAndDirectories()
        {
            string missingPath = Path.Combine(Path.GetTempPath(),
                "DS4Windows-HidHide-guard-" + Guid.NewGuid().ToString("N") + ".exe");

            Assert.IsFalse(HidHideApplicationPathGuard.TryInspect(missingPath,
                out bool missingAlias, out int missingError));
            Assert.IsFalse(missingAlias);
            Assert.AreEqual(2, missingError);
            Assert.IsFalse(DS4Windows.ControlService.CanRegisterHidHideApplication(
                missingPath, out string missingReason));
            StringAssert.Contains(missingReason, "Windows error 2");

            Assert.IsFalse(HidHideApplicationPathGuard.TryInspect(
                Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                out bool directoryAlias, out int directoryError));
            Assert.IsFalse(directoryAlias);
            Assert.AreEqual(267, directoryError);
        }

        [TestMethod]
        public void NativeInspectionAcceptsActualHidHideVolumePath()
        {
            string path = Path.GetTempFileName();
            try
            {
                var volumePath = new StringBuilder(32768);
                using (SafeFileHandle handle = File.OpenHandle(path,
                           FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    uint length = GetFinalPathNameByHandleW(handle, volumePath,
                        (uint)volumePath.Capacity, 2); // VOLUME_NAME_NT.
                    Assert.IsTrue(length > 0 && length < volumePath.Capacity,
                        "Could not read the temporary file's volume path.");
                }

                StringAssert.StartsWith(volumePath.ToString(), @"\Device\HarddiskVolume");
                Assert.IsTrue(HidHideApplicationPathGuard.TryInspect(
                    volumePath.ToString(), out bool alias, out int error),
                    $"Volume-path inspection failed with Windows error {error}.");
                Assert.IsFalse(alias);
                Assert.AreEqual(0, error);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode,
            ExactSpelling = true, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file,
            StringBuilder path, uint pathLength, uint flags);

        private sealed class FakeMetadataReader
        {
            internal bool Success = true;
            internal uint Attributes = 0x80;
            internal uint Tag;
            internal int Error;
            internal int Calls;
            internal string Path;

            internal bool Read(string nativePath, out uint attributes,
                out uint tag, out int error)
            {
                Calls++;
                Path = nativePath;
                attributes = Attributes;
                tag = Tag;
                error = Error;
                return Success;
            }
        }
    }
}
