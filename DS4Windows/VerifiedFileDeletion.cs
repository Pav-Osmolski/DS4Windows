using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DS4Windows;

internal static class VerifiedFileDeletion
{
    private const uint Delete = 0x00010000;
    private const uint ReadAttributes = 0x00000080;
    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const int FileDispositionInformation = 4;

    internal static SafeFileHandle OpenExclusive(string path, bool readContents = false) =>
        CreateFile(path, Delete | ReadAttributes | (readContents ? GenericRead : 0), 0,
            IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);

    internal static bool TryGetInfo(SafeFileHandle handle, out long size, out long lastWrite)
    {
        size = lastWrite = 0;
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) ||
            (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            return false;
        size = ((long)info.SizeHigh << 32) | info.SizeLow;
        lastWrite = ((long)info.WriteHigh << 32) | info.WriteLow;
        return true;
    }

    // Windows deletes this exact file when the exclusive handle closes.
    internal static bool MarkForDeletion(SafeFileHandle handle)
    {
        var disposition = new Disposition { DeleteFile = true };
        return SetFileInformationByHandle(handle, FileDispositionInformation, ref disposition, 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref Disposition information, uint size);
}
