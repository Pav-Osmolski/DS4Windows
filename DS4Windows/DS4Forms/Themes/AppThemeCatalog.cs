using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using DS4Windows;

namespace DS4WinWPF.DS4Forms.Themes;

public sealed record AppThemeItem(int Value, string SourceName, string BackgroundHex, string ForegroundHex, string AccentHex)
{
    public string Name => UiText.Get(SourceName);
    public Brush Background => AppThemeCatalog.Brush(BackgroundHex);
    public Brush Foreground => AppThemeCatalog.Brush(ForegroundHex);
    public Brush Accent => AppThemeCatalog.Brush(AccentHex);
}

public sealed record AppAccentItem(string Value, string SourceName, string Colour)
{
    public string Name => UiText.Get(SourceName);
    public Brush Preview => AppThemeCatalog.Brush(Colour);
}

public static class AppThemeCatalog
{
    public static IReadOnlyList<AppThemeItem> Choices { get; } = new AppThemeItem[]
    {
        new(0, "Follow Windows", "#E8EDF3", "#202733", "#0066CC"),
        new(1, "Light", "#FFFFFF", "#202733", "#0066CC"),
        new(2, "Dark", "#070D15", "#F4F7FB", "#2C7DF7"),
        new(3, "Graphite", "#181A1F", "#F1F2F4", "#6DABFF"),
        new(4, "Paper", "#F4F1E8", "#252D36", "#145DA0"),
        new(5, "Midnight", "#000000", "#FFFFFF", "#80BFFF"),
        new(6, "High contrast", "#000000", "#FFFFFF", "#00FFFF"),
    };

    public static IReadOnlyList<AppAccentItem> AccentChoices(string selected)
    {
        var items = new List<AppAccentItem>
        {
            new("Theme", "Theme accent", "#2C7DF7"), new("Windows", "Windows accent", "#0078D4"),
            new("#2C7DF7", "Blue", "#2C7DF7"), new("#A78BFA", "Violet", "#A78BFA"),
            new("#2DD4BF", "Teal", "#2DD4BF"), new("#FBBF24", "Amber", "#FBBF24"),
            new("#FB7185", "Rose", "#FB7185"),
        };
        selected = NormalizeAccent(selected);
        if (!items.Any(item => item.Value == selected)) items.Add(new(selected, "Custom colour", selected));
        return items;
    }

    public static AppThemeChoice Normalize(AppThemeChoice value) => Enum.IsDefined(value) ? value : AppThemeChoice.Default;
    public static string NormalizeAccent(string value)
    {
        if (string.Equals(value, "Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
        return Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$") ? value.ToUpperInvariant() : "Theme";
    }

    public static AppThemeChoice Resolve(AppThemeChoice requested, bool windowsDark, bool highContrast) =>
        highContrast ? AppThemeChoice.HighContrast : Normalize(requested) == AppThemeChoice.Default
            ? windowsDark ? AppThemeChoice.Dark : AppThemeChoice.Light : Normalize(requested);

    public static ResourceDictionary Create(AppThemeChoice requested, string accent = "Theme",
        bool? windowsDark = null, bool? highContrast = null)
    {
        bool systemContrast = highContrast ?? SystemParameters.HighContrast;
        var choice = Resolve(requested, windowsDark ?? Util.SystemAppsUsingDarkTheme(), systemContrast);
        var dictionary = new ResourceDictionary { Source = new Uri(
            choice is AppThemeChoice.Light or AppThemeChoice.Paper
                ? "/DS4Windows;component/DS4Forms/Themes/DefaultTheme.xaml" : "/DS4Windows;component/DS4Forms/Themes/DarkTheme.xaml", UriKind.Relative) };
        var colours = Palette(choice, systemContrast);
        foreach (var item in colours) dictionary[item.Key] = Brush(item.Value);
        var surface = ((SolidColorBrush)dictionary["SurfaceBackgroundColor"]).Color;
        var surfaces = new[] { surface, ((SolidColorBrush)dictionary["CardBackgroundColor"]).Color,
            ((SolidColorBrush)dictionary["RaisedBackgroundColor"]).Color };
        if (choice != AppThemeChoice.HighContrast)
            foreach (string key in new[] { "MutedForegroundColor", "SuccessColor", "DangerColor" })
                dictionary[key] = Frozen(ReadableAccent(((SolidColorBrush)dictionary[key]).Color, surfaces));
        var original = ((SolidColorBrush)dictionary["AccentColor"]).Color;
        accent = NormalizeAccent(accent);
        Color selected = choice == AppThemeChoice.HighContrast || accent == "Theme" ? original : accent == "Windows"
            ? Color.FromRgb(SystemParameters.WindowGlassColor.R, SystemParameters.WindowGlassColor.G, SystemParameters.WindowGlassColor.B)
            : (Color)ColorConverter.ConvertFromString(accent);
        // Accent text must be readable on the page, including custom colours.
        if (choice != AppThemeChoice.HighContrast) selected = ReadableAccent(selected, surfaces);
        dictionary["AccentColor"] = Frozen(selected);
        dictionary["AccentForegroundColor"] = Frozen(systemContrast ? SystemColors.WindowColor :
            Contrast(selected, Colors.White) >= Contrast(selected, Colors.Black) ? Colors.White : Colors.Black);
        return dictionary;
    }

    private static Dictionary<string, string> Palette(AppThemeChoice choice, bool systemContrast)
    {
        string[] values = choice switch
        {
            AppThemeChoice.Graphite => new[] { "#181A1F", "#F1F2F4", "#4A505B", "#314A70", "#6DABFF", "#181A1F", "#14161A", "#202329", "#292D35", "#314A70", "#BEC3CD", "#5BD789", "#FF8A94" },
            AppThemeChoice.Paper => new[] { "#F4F1E8", "#252D36", "#B9B1A2", "#DCE7F2", "#145DA0", "#F4F1E8", "#FCFAF5", "#FFFEFA", "#E8E3D8", "#DCE7F2", "#59616C", "#166C39", "#A31D2B" },
            AppThemeChoice.Midnight => new[] { "#000000", "#FFFFFF", "#4C4C4C", "#183A5A", "#80BFFF", "#000000", "#050505", "#0B0B0B", "#151515", "#183A5A", "#B8BEC8", "#65E596", "#FF929D" },
            AppThemeChoice.HighContrast => new[] { "#000000", "#FFFFFF", "#FFFFFF", "#303030", "#00FFFF", "#000000", "#000000", "#000000", "#181818", "#303030", "#FFFFFF", "#FFFFFF", "#FFFFFF" },
            _ => Array.Empty<string>()
        };
        string[] keys = { "BackgroundColor", "ForegroundColor", "BorderColor", "SecondaryColor", "AccentColor", "SurfaceBackgroundColor", "SidebarBackgroundColor", "CardBackgroundColor", "RaisedBackgroundColor", "NavigationSelectionColor", "MutedForegroundColor", "SuccessColor", "DangerColor" };
        var palette = keys.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second);
        if (choice == AppThemeChoice.HighContrast && systemContrast)
        {
            string Hex(Color colour) => colour.ToString(CultureInfo.InvariantCulture);
            foreach (string key in new[] { "BackgroundColor", "SurfaceBackgroundColor", "SidebarBackgroundColor", "CardBackgroundColor", "RaisedBackgroundColor" }) palette[key] = Hex(SystemColors.WindowColor);
            foreach (string key in new[] { "ForegroundColor", "MutedForegroundColor", "BorderColor", "SuccessColor", "DangerColor" }) palette[key] = Hex(SystemColors.WindowTextColor);
            palette["AccentColor"] = Hex(SystemColors.WindowTextColor);
            palette["SecondaryColor"] = Hex(SystemColors.ControlColor);
            // Navigation retains normal text and icon brushes. A system-colour
            // surface plus the selected outline avoids mismatched highlight text.
            palette["NavigationSelectionColor"] = Hex(SystemColors.WindowColor);
        }
        if (palette.TryGetValue("CardBackgroundColor", out string card))
        {
            palette["ToolTipBackgroundColor"] = card;
            palette["ToolTipForegroundColor"] = palette["ForegroundColor"];
            palette["MenuSeparatorColor"] = palette["BorderColor"];
        }
        return palette;
    }

    public static SolidColorBrush Brush(string value) => Frozen((Color)ColorConverter.ConvertFromString(value));
    private static SolidColorBrush Frozen(Color colour) { var brush = new SolidColorBrush(colour); brush.Freeze(); return brush; }
    public static double Contrast(Color first, Color second)
    {
        double L(Color colour)
        {
            double Channel(byte b) { double n = b / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Channel(colour.R) + .7152 * Channel(colour.G) + .0722 * Channel(colour.B);
        }
        double a = L(first), b = L(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static Color ReadableAccent(Color colour, params Color[] backgrounds)
    {
        Color target = backgrounds.Min(background => Contrast(Colors.White, background)) > backgrounds.Min(background => Contrast(Colors.Black, background)) ? Colors.White : Colors.Black;
        Color original = colour;
        for (int step = 1; step <= 100 && backgrounds.Min(background => Contrast(colour, background)) < 4.5; step++)
        {
            double amount = step / 100d;
            colour = Color.FromRgb((byte)(original.R + (target.R - original.R) * amount),
                (byte)(original.G + (target.G - original.G) * amount), (byte)(original.B + (target.B - original.B) * amount));
        }
        return colour;
    }
}
