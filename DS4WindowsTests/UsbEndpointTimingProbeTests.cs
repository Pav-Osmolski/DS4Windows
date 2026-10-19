using System;
using System.Buffers.Binary;
using System.Linq;
using DS4Windows;

namespace DS4WindowsTests
{
    [TestClass]
    public class UsbEndpointTimingProbeTests
    {
        [TestMethod]
        [DataRow(0, 10, 10.0)]
        [DataRow(1, 1, 1.0)]
        [DataRow(1, 4, 4.0)]
        [DataRow(1, 6, 6.0)]
        [DataRow(2, 1, 0.125)]
        [DataRow(2, 4, 1.0)]
        [DataRow(2, 5, 2.0)]
        [DataRow(2, 6, 4.0)]
        [DataRow(2, 7, 8.0)]
        [DataRow(3, 6, 4.0)]
        [DataRow(3, 16, 4096.0)]
        public void IntervalUsesActualSpeedAndDescriptor(
            int speed, int interval, double milliseconds)
        {
            Assert.AreEqual(TimeSpan.FromMilliseconds(milliseconds),
                UsbEndpointTimingProbe.DecodeInterval((byte)speed, (byte)interval));
        }

        [TestMethod]
        [DataRow(0, 1)]
        [DataRow(1, 0)]
        [DataRow(2, 0)]
        [DataRow(2, 17)]
        [DataRow(3, 255)]
        [DataRow(4, 6)]
        [DataRow(255, 6)]
        public void InvalidOrUnknownTimingIsNotReplacedWithGuessedRate(
            int speed, int interval) => Assert.IsNull(
                UsbEndpointTimingProbe.DecodeInterval((byte)speed, (byte)interval));

        [TestMethod]
        public void CompositeControllerUsesItsHidInterfaceAndSeparateDirections()
        {
            var descriptor = Configuration(
                Interface(0, 1), Endpoint(1, 1, 1), // audio, not HID
                Interface(3, 3), Endpoint(0x84, 3, 4), Endpoint(4, 3, 6));
            var endpoints = UsbEndpointTimingProbe.ParseConfiguration(descriptor, 2, 3);
            CollectionAssert.AreEqual(new byte[] { 0x84, 4 },
                endpoints.Select(e => e.Address).ToArray());
            Assert.AreEqual(TimeSpan.FromMilliseconds(1), endpoints[0].Interval);
            Assert.AreEqual(TimeSpan.FromMilliseconds(4), endpoints[1].Interval);
            Assert.AreEqual(0, UsbEndpointTimingProbe.ParseConfiguration(
                descriptor, 2, 0).Count, "Never borrow the audio interface's interval.");
        }

        [TestMethod]
        public void MultipleHidInterfacesRequireExactPhysicalInterface()
        {
            var descriptor = Configuration(
                Interface(1, 3), Endpoint(0x81, 3, 4),
                Interface(3, 3), Endpoint(0x83, 3, 7));
            Assert.AreEqual(0, UsbEndpointTimingProbe.ParseConfiguration(
                descriptor, 2, null).Count);
            var selected = UsbEndpointTimingProbe.ParseConfiguration(descriptor, 2, 3);
            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual(TimeSpan.FromMilliseconds(8), selected[0].Interval);
            Assert.AreEqual(0, UsbEndpointTimingProbe.ParseConfiguration(
                descriptor, 2, 4).Count);
        }

        [TestMethod]
        public void MissingOutputEndpointDoesNotBorrowInputInterval()
        {
            var descriptor = Configuration(Interface(0, 3), Endpoint(0x81, 3, 4));
            var selected = UsbEndpointTimingProbe.ParseConfiguration(descriptor, 2, null);
            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual((byte)0x81, selected[0].Address);
        }

        [TestMethod]
        public void MalformedTruncatedAndDuplicateDescriptorsAreUnknown()
        {
            var descriptor = Configuration(Interface(0, 3), Endpoint(0x81, 3, 4));
            for (int length = 0; length < descriptor.Length; length++)
                Assert.AreEqual(0, UsbEndpointTimingProbe.ParseConfiguration(
                    descriptor.AsSpan(0, length), 2, null).Count);
            descriptor[9] = 0;
            Assert.AreEqual(0, UsbEndpointTimingProbe.ParseConfiguration(
                descriptor, 2, null).Count);
            descriptor = Configuration(Interface(0, 3),
                Endpoint(0x81, 3, 4), Endpoint(0x81, 3, 6));
            Assert.AreEqual(0, UsbEndpointTimingProbe.ParseConfiguration(
                descriptor, 2, null).Count);
        }

        [TestMethod]
        public void CapturedPhysicalDualSenseDescriptorMatchesActivePackedPipes()
        {
            var descriptor = Convert.FromHexString("0902E300040100C0FA0904000000010100000A2401000149000201020C24020101010604330000000C24060201010300000000000924030301030402000C2402040204030203000000092406050401030000092403060101010500090401000001020000090401010101020000072401010101000B2402010402100180BB0009050109880104000007250100000000090402000001020000090402010101020000072401060101000B2402010202100180BB0009058205C400040000072501000000000904030002030000000921110100012221010705840340000607050303400006");
            var connection = Convert.FromHexString("0400000012010002000000404C05E60C0001010200010102000B00020000000100000007058403400006000000000705030340000600000000");
            var timings = UsbEndpointTimingProbe.ParseConfiguration(descriptor, 2, 3);
            Assert.AreEqual(2, timings.Count);
            Assert.AreEqual(TimeSpan.FromMilliseconds(4), timings[0].Interval);
            Assert.AreEqual(TimeSpan.FromMilliseconds(4), timings[1].Interval);
            Assert.IsTrue(UsbEndpointTimingProbe.MatchesOpenPipes(connection, timings));
        }

        [TestMethod]
        public void ActiveAlternateTimingMustMatchInsteadOfAssumingAltZero()
        {
            var descriptor = Configuration(Interface(0, 3), Endpoint(0x81, 3, 4));
            var timings = UsbEndpointTimingProbe.ParseConfiguration(descriptor, 2, 0);
            byte[] connection = new byte[46]; connection[23] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(connection.AsSpan(27), 1);
            Endpoint(0x81, 3, 4).CopyTo(connection, 35);
            Assert.IsTrue(UsbEndpointTimingProbe.MatchesOpenPipes(connection, timings));
            connection[41] = 6;
            Assert.IsFalse(UsbEndpointTimingProbe.MatchesOpenPipes(connection, timings));
            Assert.IsFalse(UsbEndpointTimingProbe.MatchesOpenPipes(
                connection.AsSpan(0, 40), timings));
        }

        [TestMethod]
        [DataRow("\\\\?\\hid#vid_054c&pid_0ce6&mi_03#1", 3)]
        [DataRow("\\\\?\\hid#vid_054c&pid_0ce6&MI_0A#1", 10)]
        public void CompositeInterfaceNumberUsesHexadecimalPathValue(
            string path, int expected) => Assert.AreEqual(expected,
                UsbEndpointTimingProbe.InterfaceNumberFromPath(path));

        private static byte[] Configuration(params byte[][] items)
        {
            byte[] bytes = new byte[9 + items.Sum(i => i.Length)];
            new byte[] { 9, 2, 0, 0, 4, 1, 0, 0x80, 50 }.CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)bytes.Length);
            int position = 9;
            foreach (var item in items) { item.CopyTo(bytes, position); position += item.Length; }
            return bytes;
        }
        private static byte[] Interface(byte number, byte deviceClass) =>
            new byte[] { 9, 4, number, 0, 1, deviceClass, 0, 0, 0 };
        private static byte[] Endpoint(byte address, byte attributes, byte interval) =>
            new byte[] { 7, 5, address, attributes, 64, 0, interval };
    }
}
