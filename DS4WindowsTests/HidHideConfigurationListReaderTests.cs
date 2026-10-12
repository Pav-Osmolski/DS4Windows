using System.Runtime.InteropServices;
using DS4WinWPF.DS4Control;

namespace DS4WindowsTests
{
    [TestClass]
    public class HidHideConfigurationListReaderTests
    {
        private const uint ControlCode = 0x80016000;

        [TestMethod]
        public void ParsePreservesPathsOrderDuplicatesWhitespaceAndUtf16CodeUnits()
        {
            string[] expected =
            {
                @"\Device\HarddiskVolume3\Apps\Game.exe", "  ",
                @"\Device\HarddiskVolume3\Apps\Game.exe", "unpaired-\ud800.exe"
            };

            Assert.IsTrue(HidHideConfigurationListReader.TryParse(
                Bytes(string.Join('\0', expected) + "\0\0"), out var entries));

            CollectionAssert.AreEqual(expected, entries);
        }

        [TestMethod]
        [DataRow("\0")]
        [DataRow("\0\0")]
        [DataRow("\0\0unused")]
        public void ParseAcceptsDriverEmptyListAndStandardEmptyList(string value)
        {
            Assert.IsTrue(HidHideConfigurationListReader.TryParse(Bytes(value), out var entries));
            Assert.AreEqual(0, entries.Count);
        }

        [TestMethod]
        public void ParseStopsAtFirstListTerminatorBeforeOversizedDriverTail()
        {
            Assert.IsTrue(HidHideConfigurationListReader.TryParse(
                Bytes("first\0second\0\0garbage\uffff\ud800"), out var entries));
            CollectionAssert.AreEqual(new[] { "first", "second" }, entries);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("unterminated")]
        [DataRow("first\0")]
        [DataRow("first\0unterminated")]
        [DataRow("\0not-an-empty-list")]
        public void ParseRejectsMissingTerminationWithoutReturningPartialEntries(string value)
        {
            Assert.IsFalse(HidHideConfigurationListReader.TryParse(Bytes(value), out var entries));
            Assert.AreEqual(0, entries.Count);
        }

        [TestMethod]
        public void ParseRejectsOddAndOversizedBuffers()
        {
            Assert.IsFalse(HidHideConfigurationListReader.TryParse(new byte[] { 0 }, out var odd));
            Assert.AreEqual(0, odd.Count);
            Assert.IsFalse(HidHideConfigurationListReader.TryParse(
                new byte[HidHideConfigurationListReader.MaximumBytes + 2], out var oversized));
            Assert.AreEqual(0, oversized.Count);
        }

        [TestMethod]
        public void ReadRequiresSuccessfulSizeQueryEvenIfItReportsAUsableLength()
        {
            using var handle = new FakeHandle();
            int calls = 0;
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                calls++;
                returned = 4;
                return false;
            }

            Assert.IsFalse(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, entries.Count);
        }

        [TestMethod]
        [DataRow(-2)]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(3)]
        [DataRow(1048578)]
        public void ReadRejectsInvalidSizeBeforeAllocatingOrFilling(int size)
        {
            using var handle = new FakeHandle();
            int calls = 0;
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                calls++;
                returned = size;
                return true;
            }

            Assert.IsFalse(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, entries.Count);
        }

        [TestMethod]
        [DataRow(false, 4)]
        [DataRow(true, -2)]
        [DataRow(true, 0)]
        [DataRow(true, 1)]
        [DataRow(true, 3)]
        [DataRow(true, 10)]
        public void ReadRejectsFailedOrInvalidFill(bool fillSucceeded, int fillLength)
        {
            using var handle = new FakeHandle();
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                returned = length == 0 ? 8 : fillLength;
                return length == 0 || fillSucceeded;
            }

            Assert.IsFalse(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
            Assert.AreEqual(0, entries.Count);
        }

        [TestMethod]
        public void ReadOnlyParsesReturnedBytesAndAcceptsShortValidFill()
        {
            using var handle = new FakeHandle();
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                Assert.AreEqual(ControlCode, code);
                Assert.AreEqual(new IntPtr(42), device);
                returned = length == 0 ? 32 : 6;
                if (length == 0) Assert.AreEqual(IntPtr.Zero, output);
                else
                {
                    Assert.AreEqual(32, length);
                    Marshal.Copy(Bytes("a\0\0ignored"), 0, output, 20);
                }
                return true;
            }

            Assert.IsTrue(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
            CollectionAssert.AreEqual(new[] { "a" }, entries);
        }

        [TestMethod]
        public void ReadDoesNotManufactureTerminationForIncompleteDriverWrite()
        {
            using var handle = new FakeHandle();
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                returned = 8;
                if (length > 0) Marshal.Copy(Bytes("a\0"), 0, output, 4);
                return true;
            }

            Assert.IsFalse(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
            Assert.AreEqual(0, entries.Count);
        }

        [TestMethod]
        public void ReadAcceptsOversizedReportedFillWhenActualListIsTerminated()
        {
            using var handle = new FakeHandle();
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                returned = 32;
                if (length > 0) Marshal.Copy(Bytes("a\0\0"), 0, output, 6);
                return true;
            }

            Assert.IsTrue(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
            CollectionAssert.AreEqual(new[] { "a" }, entries);
        }

        [TestMethod]
        public void ReadPinsHandleAcrossQueriesAndReleasesItAfterConcurrentDispose()
        {
            var handle = new FakeHandle();
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                Assert.IsFalse(handle.Released);
                returned = 2;
                if (length == 0) handle.Dispose();
                else Marshal.WriteInt16(output, 0);
                Assert.IsFalse(handle.Released);
                return true;
            }

            Assert.IsTrue(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
            Assert.AreEqual(0, entries.Count);
            Assert.IsTrue(handle.Released);
        }

        [TestMethod]
        public void ReadReleasesProtectedHandleOnFailedQueryAfterConcurrentDispose()
        {
            var handle = new FakeHandle();
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                handle.Dispose();
                Assert.IsFalse(handle.Released);
                returned = 0;
                return false;
            }

            Assert.IsFalse(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
            Assert.AreEqual(0, entries.Count);
            Assert.IsTrue(handle.Released);
        }

        [TestMethod]
        public void ReadSkipsClosedInvalidAndNullHandles()
        {
            using var invalid = new FakeHandle(IntPtr.Zero);
            var closed = new FakeHandle();
            closed.Dispose();
            bool Read(IntPtr device, uint code, IntPtr output, int length, out int returned)
            {
                returned = 0;
                Assert.Fail("An invalid handle reached an IOCTL.");
                return false;
            }

            foreach (SafeHandle handle in new SafeHandle[] { null, invalid, closed })
            {
                Assert.IsFalse(HidHideConfigurationListReader.TryRead(handle, ControlCode, Read, out var entries));
                Assert.AreEqual(0, entries.Count);
            }
        }

        private static byte[] Bytes(string value)
        {
            byte[] bytes = new byte[value.Length * 2];
            for (int index = 0; index < value.Length; index++)
            {
                bytes[index * 2] = (byte)value[index];
                bytes[index * 2 + 1] = (byte)(value[index] >> 8);
            }
            return bytes;
        }

        private sealed class FakeHandle : SafeHandle
        {
            internal bool Released { get; private set; }
            internal FakeHandle() : this(new IntPtr(42)) { }
            internal FakeHandle(IntPtr value) : base(IntPtr.Zero, true) => SetHandle(value);
            public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
            protected override bool ReleaseHandle()
            {
                Released = true;
                return true;
            }
        }
    }
}
