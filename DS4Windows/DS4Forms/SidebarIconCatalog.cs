using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

public sealed class SidebarIconOption : INotifyPropertyChanged
{
    private readonly Lazy<BitmapSource> image;
    private readonly Lazy<BitmapSource> hueSource;
    private BitmapSource shiftedImage;
    private double hue;
    public event PropertyChangedEventHandler PropertyChanged;

    internal SidebarIconOption(SidebarIconChoice value, string name, string filename)
    {
        Value = value;
        Name = name;
        BitmapSource Load(int decodeWidth)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(
                $"pack://application:,,,/DS4Windows;component/Resources/Branding/{filename}.png");
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        image = new Lazy<BitmapSource>(() => Load(0));
        // Enough pixels for the 44 DIP sidebar even at 400% scaling. Decode once.
        hueSource = new Lazy<BitmapSource>(() => Load(256));
    }

    public SidebarIconChoice Value { get; }
    public string Name { get; }
    public ImageSource Image => shiftedImage ?? image.Value;

    internal void SetHue(double degrees)
    {
        degrees = SidebarIconPolicy.NormalizeHue(degrees) % 360;
        if (hue == degrees) return;
        hue = degrees;
        shiftedImage = degrees == 0 ? null : SidebarIconCatalog.RotateHue(hueSource.Value, degrees);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
    }
    public override string ToString() => Name;
}

internal static class SidebarIconCatalog
{
    internal const string ResourceKey = "SidebarBrandIcon";

    internal static IReadOnlyList<SidebarIconOption> Choices { get; } =
        Array.AsReadOnly(new[]
        {
            new SidebarIconOption(SidebarIconChoice.BlueTile, "Blue Tile", "blue-tile"),
            new SidebarIconOption(SidebarIconChoice.MidnightCircle, "Midnight Circle", "midnight-circle"),
            new SidebarIconOption(SidebarIconChoice.BlueFrame, "Blue Frame", "blue-frame"),
            new SidebarIconOption(SidebarIconChoice.Controller, "Controller", "controller"),
            new SidebarIconOption(SidebarIconChoice.AngularBadge, "Angular Badge", "angular-badge"),
        });

    internal static void Apply(ResourceDictionary resources, SidebarIconChoice choice, double hue = 0)
    {
        foreach (var option in Choices) option.SetHue(hue);
        resources[ResourceKey] = Choices[(int)SidebarIconPolicy.Normalize(choice)].Image;
    }

    internal static BitmapSource RotateHue(BitmapSource source, double degrees)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        double shift = SidebarIconPolicy.NormalizeHue(degrees) / 60;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0) continue;
            double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double chroma = max - min;
            if (chroma == 0) continue; // Preserve whites, greys and blacks exactly.
            double hue = max == r ? (g - b) / chroma : max == g ? (b - r) / chroma + 2 : (r - g) / chroma + 4;
            hue = (hue + shift + 6) % 6;
            double x = chroma * (1 - Math.Abs(hue % 2 - 1));
            (double rr, double gg, double bb) = hue switch
            {
                < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x), < 4 => (0d, x, chroma),
                < 5 => (x, 0d, chroma), _ => (chroma, 0d, x),
            };
            pixels[i] = (byte)Math.Round(bb + min);
            pixels[i + 1] = (byte)Math.Round(gg + min);
            pixels[i + 2] = (byte)Math.Round(rr + min);
        }
        var result = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
