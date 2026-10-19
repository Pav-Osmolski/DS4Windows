using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

public sealed class SidebarIconOption
{
    private readonly Lazy<BitmapSource> image;

    internal SidebarIconOption(SidebarIconChoice value, string name, string filename)
    {
        Value = value;
        Name = name;
        image = new Lazy<BitmapSource>(() =>
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(
                $"pack://application:,,,/DS4Windows;component/Resources/Branding/{filename}.png");
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        });
    }

    public SidebarIconChoice Value { get; }
    public string Name { get; }
    public ImageSource Image => image.Value;
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

    internal static void Apply(ResourceDictionary resources, SidebarIconChoice choice) =>
        resources[ResourceKey] = Choices[(int)SidebarIconPolicy.Normalize(choice)].Image;
}
