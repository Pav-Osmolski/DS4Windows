using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DS4Windows
{
    internal sealed record UsbEndpointTiming(byte Address, TimeSpan Interval);

    /// <summary>
    /// Read-only, best-effort descriptor discovery. No driver replacement,
    /// interface claim, reset or polling-rate request is performed. Call off
    /// the input/UI thread and scope the result to this physical connection.
    /// </summary>
    internal static class UsbEndpointTimingProbe
    {
        private const uint ConnectionInformationEx = 0x220448;
        private const uint DescriptorFromConnection = 0x220410;
        private static readonly Guid HubInterface =
            new("f18a0e88-c30c-11d0-8815-00a0c906bed8");
        private static readonly PropertyKey AddressKey = new()
        {
            FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 30,
        };

        internal static IReadOnlyList<UsbEndpointTiming> Detect(
            string hidPath, int vendorId, int productId)
        {
            try
            {
                string id = Global.GetInstanceIdFromDevicePath(hidPath);
                if (string.IsNullOrEmpty(id) ||
                    CM_Locate_DevNode(out uint node, id, 0) != 0)
                    return Array.Empty<UsbEndpointTiming>();

                int? interfaceNumber = InterfaceNumberFromPath(hidPath);
                for (int depth = 0; depth < 12; depth++)
                {
                    if (CM_Get_Parent(out uint parent, node, 0) != 0)
                        break;
                    var parentId = new StringBuilder(512);
                    if (CM_Get_Device_ID(parent, parentId, parentId.Capacity, 0) != 0)
                        break;
                    string hubPath = GetHubPath(parentId.ToString());
                    if (hubPath != null && TryGetAddress(node, out uint port))
                    {
                        // USBView uses GENERIC_WRITE to open the hub for these read-only IOCTLs.
                        using var hub = CreateFile(hubPath, 0x40000000,
                            3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                        if (hub.IsInvalid) break;
                        return ReadHub(hub, port, vendorId, productId,
                            interfaceNumber);
                    }
                    node = parent;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or
                InvalidOperationException or System.IO.IOException or
                System.ComponentModel.Win32Exception)
            {
                // A disappearing device or an unsupported hub is unknown,
                // never a reason to assume a controller-specific frequency.
            }
            return Array.Empty<UsbEndpointTiming>();
        }

        private static IReadOnlyList<UsbEndpointTiming> ReadHub(
            SafeFileHandle hub, uint port, int vendorId, int productId,
            int? interfaceNumber)
        {
            // USB_NODE_CONNECTION_INFORMATION_EX uses the USB headers' one-byte
            // packing: speed at 23, open-pipe count at 27, status at 31.
            byte[] info = new byte[4096];
            BinaryPrimitives.WriteUInt32LittleEndian(info, port);
            if (!Query(hub, ConnectionInformationEx, info, out int returned) ||
                returned < 35 || BinaryPrimitives.ReadUInt32LittleEndian(
                    info.AsSpan(31)) != 1 || info[24] != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(12)) != vendorId ||
                BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(14)) != productId)
                return Array.Empty<UsbEndpointTiming>();

            byte speed = info[23], configuration = info[22];
            int count = info[21];
            // Bound work for unusual or malformed devices. Unknown is safer
            // than selecting the first configuration or another HID interface.
            if (count is < 1 or > 8 || configuration == 0)
                return Array.Empty<UsbEndpointTiming>();
            for (int index = 0; index < count; index++)
            {
                byte[] header = ReadConfiguration(hub, port, index, 9);
                if (header == null || header[0] != 9 || header[1] != 2)
                    continue;
                int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
                if (length < 9 || header[5] != configuration) continue;
                byte[] descriptor = ReadConfiguration(hub, port, index, length);
                if (descriptor == null) break;
                var timings = ParseConfiguration(descriptor, speed, interfaceNumber);
                return MatchesOpenPipes(info.AsSpan(0, returned), timings) ?
                    timings : Array.Empty<UsbEndpointTiming>();
            }
            return Array.Empty<UsbEndpointTiming>();
        }

        private static byte[] ReadConfiguration(SafeFileHandle hub,
            uint port, int index, int length)
        {
            byte[] request = new byte[12 + length];
            BinaryPrimitives.WriteUInt32LittleEndian(request, port);
            request[4] = 0x80; request[5] = 6; // GET_DESCRIPTOR, device-to-host
            request[6] = (byte)index; request[7] = 2; // configuration
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(10), (ushort)length);
            if (!Query(hub, DescriptorFromConnection, request, out int returned) ||
                returned < request.Length) return null;
            return request.AsSpan(12, length).ToArray();
        }

        internal static int? InterfaceNumberFromPath(string path)
        {
            int index = path?.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase) ?? -1;
            return index >= 0 && path.Length >= index + 6 &&
                byte.TryParse(path.AsSpan(index + 4, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out byte value) ? value : null;
        }

        internal static IReadOnlyList<UsbEndpointTiming> ParseConfiguration(
            ReadOnlySpan<byte> descriptor, byte windowsSpeed, int? interfaceNumber)
        {
            if (descriptor.Length < 9 || descriptor[0] != 9 || descriptor[1] != 2 ||
                BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(2)) != descriptor.Length)
                return Array.Empty<UsbEndpointTiming>();

            var result = new List<UsbEndpointTiming>();
            var hidInterfaces = new HashSet<byte>();
            bool selected = false;
            for (int offset = 9; offset < descriptor.Length;)
            {
                if (offset + 2 > descriptor.Length) return Array.Empty<UsbEndpointTiming>();
                int length = descriptor[offset];
                if (length < 2 || offset + length > descriptor.Length)
                    return Array.Empty<UsbEndpointTiming>();
                var item = descriptor.Slice(offset, length);
                if (item[1] == 4)
                {
                    if (length < 9) return Array.Empty<UsbEndpointTiming>();
                    bool hid = item[5] == 3 && item[3] == 0;
                    if (hid) hidInterfaces.Add(item[2]);
                    selected = hid && (interfaceNumber == null || item[2] == interfaceNumber);
                }
                else if (item[1] == 5 && selected)
                {
                    if (length < 7) return Array.Empty<UsbEndpointTiming>();
                    // Interrupt endpoints only. Audio/isochronous and control
                    // transfers do not define the HID input/output interval.
                    if ((item[3] & 3) == 3 && (item[2] & 15) != 0)
                    {
                        TimeSpan? interval = DecodeInterval(windowsSpeed, item[6]);
                        byte address = item[2];
                        if (interval == null || result.Exists(e => e.Address == address))
                            return Array.Empty<UsbEndpointTiming>();
                        result.Add(new UsbEndpointTiming(item[2], interval.Value));
                    }
                }
                offset += length;
            }
            return interfaceNumber == null && hidInterfaces.Count != 1 ?
                Array.Empty<UsbEndpointTiming>() : result;
        }

        internal static TimeSpan? DecodeInterval(byte windowsSpeed, byte interval)
        {
            if (interval == 0) return null;
            // USB_DEVICE_SPEED in Windows is zero based, unlike USB/IP speed.
            return windowsSpeed switch
            {
                0 when interval >= 10 => TimeSpan.FromMilliseconds(interval),
                1 => TimeSpan.FromMilliseconds(interval),
                2 or 3 when interval <= 16 =>
                    TimeSpan.FromTicks((1L << (interval - 1)) * 1250),
                _ => null,
            };
        }

        internal static bool MatchesOpenPipes(ReadOnlySpan<byte> connection,
            IReadOnlyList<UsbEndpointTiming> timings)
        {
            if (connection.Length < 35 || timings.Count == 0) return false;
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(connection.Slice(27));
            // Packed USB_PIPE_INFO: seven-byte endpoint descriptor followed by
            // ULONG ScheduleOffset. Confirm active pipes, not just alt zero.
            if (count > 255 || connection.Length < 35 + count * 11) return false;
            foreach (var timing in timings)
            {
                bool matched = false;
                for (int index = 0; index < count; index++)
                {
                    var pipe = connection.Slice(35 + index * 11, 11);
                    if (pipe[0] == 7 && pipe[1] == 5 && pipe[2] == timing.Address &&
                        (pipe[3] & 3) == 3 &&
                        DecodeInterval(connection[23], pipe[6]) == timing.Interval)
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched) return false;
            }
            return true;
        }

        private static bool TryGetAddress(uint node, out uint address)
        {
            byte[] buffer = new byte[4]; uint size = 4;
            address = 0;
            if (CM_Get_DevNode_Property(node, in AddressKey, out uint type,
                buffer, ref size, 0) != 0 || type != 7 || size != 4) return false;
            address = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            return address is > 0 and <= 255;
        }

        private static string GetHubPath(string instance)
        {
            Guid guid = HubInterface;
            // Retry a bounded number of times if PnP changes the required size.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (CM_Get_Device_Interface_List_Size(out uint size, ref guid,
                    instance, 0) != 0 || size is < 2 or > 32768) return null;
                char[] buffer = new char[size];
                if (CM_Get_Device_Interface_List(ref guid, instance, buffer, size, 0) != 0)
                    continue;
                string[] paths = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                return paths.Length == 1 ? paths[0] : null;
            }
            return null;
        }

        private static bool Query(SafeFileHandle hub, uint code, byte[] buffer,
            out int returned) => DeviceIoControl(hub, code, buffer, buffer.Length,
                buffer, buffer.Length, out returned, IntPtr.Zero);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid FormatId; public uint Id; }
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
        private static extern int CM_Locate_DevNode(out uint node, string id, uint flags);
        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
        private static extern int CM_Get_Device_ID(uint node, StringBuilder id, int length, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_DevNode_PropertyW")]
        private static extern int CM_Get_DevNode_Property(uint node, in PropertyKey key,
            out uint type, byte[] buffer, ref uint size, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_List_SizeW")]
        private static extern int CM_Get_Device_Interface_List_Size(out uint size, ref Guid guid,
            string id, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_ListW")]
        private static extern int CM_Get_Device_Interface_List(ref Guid guid, string id,
            [Out] char[] buffer, uint size, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing,
            IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code,
            [In] byte[] input, int inputLength, [Out] byte[] output, int outputLength,
            out int returned, IntPtr overlapped);
    }
}
