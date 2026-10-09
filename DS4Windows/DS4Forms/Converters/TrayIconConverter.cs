using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;

namespace DS4WinWPF.DS4Forms.Converters;

public sealed class TrayIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string resource || string.IsNullOrEmpty(resource)) return null;
        using Stream stream = Application.GetResourceStream(new Uri(resource, UriKind.RelativeOrAbsolute)).Stream;
        return TrayIconLoader.Load(stream, TrayIconLoader.GetPixelSize());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class TrayIconLoader
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    internal static int GetPixelSize()
    {
        // The tray belongs to Explorer's taskbar, which may be on another monitor.
        uint dpi = GetDpiForWindow(FindWindow("Shell_TrayWnd", null));
        if (dpi == 0) dpi = GetDpiForSystem();
        return Math.Max(16, GetSystemMetricsForDpi(49 /* SM_CXSMICON */, dpi));
    }

    internal static Icon Load(Stream source, int pixelSize)
    {
        if (pixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSize));
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        byte[] data = bytes.ToArray();
        using var reader = new BinaryReader(new MemoryStream(data));
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1)
            throw new InvalidDataException("Invalid tray icon header.");
        int count = reader.ReadUInt16();
        int selected = -1, selectedSize = 0, selectedDepth = 0;
        for (int index = 0; index < count; index++)
        {
            int offset = 6 + index * 16;
            if (offset + 16 > data.Length) throw new InvalidDataException("Truncated tray icon directory.");
            int width = data[offset] == 0 ? 256 : data[offset];
            int height = data[offset + 1] == 0 ? 256 : data[offset + 1];
            if (width != height) continue;
            int depth = BitConverter.ToUInt16(data, offset + 6);
            // Prefer an exact size, otherwise downsample the next larger frame.
            // A smaller frame is used only when no sufficiently large one exists.
            bool better = selected < 0 ||
                (width >= pixelSize && (selectedSize < pixelSize || width < selectedSize)) ||
                (width < pixelSize && selectedSize < pixelSize && width > selectedSize) ||
                (width == selectedSize && depth > selectedDepth);
            if (better) { selected = offset; selectedSize = width; selectedDepth = depth; }
        }
        if (selected < 0) throw new InvalidDataException("Tray icon has no square frames.");
        uint length = BitConverter.ToUInt32(data, selected + 8);
        uint start = BitConverter.ToUInt32(data, selected + 12);
        if (length == 0 || start < 6 + count * 16 || (ulong)start + length > (ulong)data.Length)
            throw new InvalidDataException("Invalid tray icon frame.");
        using var frame = new MemoryStream();
        using (var writer = new BinaryWriter(frame, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write(data, selected, 12);
            writer.Write((uint)22);
            writer.Write(data, checked((int)start), checked((int)length));
        }
        frame.Position = 0;
        return new Icon(frame, selectedSize, selectedSize);
    }
}
