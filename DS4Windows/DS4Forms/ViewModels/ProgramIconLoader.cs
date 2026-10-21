using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DS4WinWPF.DS4Forms.ViewModels;

internal static class ProgramIconLoader
{
    private static readonly SemaphoreSlim Workers = new(2);
    private static readonly ConcurrentDictionary<string, Lazy<Task<ImageSource>>> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static Task<ImageSource> LoadAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOfAny(new[] { '*', '?' }) >= 0)
            return Task.FromResult<ImageSource>(null);
        return Cache.GetOrAdd(path, key => new Lazy<Task<ImageSource>>(() => ExtractAsync(key))).Value;
    }

    private static async Task<ImageSource> ExtractAsync(string path)
    {
        await Workers.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run<ImageSource>(() =>
            {
                try
                {
                    if (!File.Exists(path)) return null;
                    using var icon = Icon.ExtractAssociatedIcon(path);
                    if (icon == null) return null;
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    return bitmap;
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or
                    System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException or System.Security.SecurityException)
                {
                    return null;
                }
            }).ConfigureAwait(false);
        }
        finally { Workers.Release(); }
    }
}
