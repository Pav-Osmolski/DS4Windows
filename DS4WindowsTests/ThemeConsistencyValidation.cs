using System.Runtime.CompilerServices;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Converters;

namespace DS4WindowsTests;

internal static class ThemeConsistencyValidation
{
    internal static void Validate(Application application, string directory, List<string> renderedFiles)
    {
        var original = application.Resources.MergedDictionaries.ToArray();
        object converter = application.Resources["ShellViewportWidth"];
        var settings = Global.store.triggerLabSettings[8];
        try
        {
            application.Resources.MergedDictionaries.Clear();
            application.Resources["ShellViewportWidth"] = new ShellViewportWidthConverter();
            var dark = Dictionary("DarkTheme");
            application.Resources.MergedDictionaries.Add(dark);
            application.Resources.MergedDictionaries.Add(Dictionary("BridgeShellStyles"));
            Global.store.triggerLabSettings[8] = new TriggerLabProfileSettings { Enabled = true };
            // Construct under Dark as the main window does, then keep the same
            // controls and styles alive while switching to Light and back.
            var lab = new TriggerLabControl();
            lab.SetDevice(8);
            lab.ClearValue(FrameworkElement.WidthProperty);
            lab.HorizontalAlignment = HorizontalAlignment.Stretch;
            lab.SetResourceReference(Control.ForegroundProperty, "ForegroundColor");
            var toolbar = Extract("MainWindow", "profOptsToolbar");
            var auto = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string name in new[] { "addProgramsBtn", "hideUncheckedBtn" })
                auto.Children.Add(Extract("AutoProfiles", name));
            foreach (string theme in new[] { "DarkTheme", "DefaultTheme", "DarkTheme" })
            {
                application.Resources.MergedDictionaries[0] = Dictionary(theme);
                var host = new StackPanel();
                host.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackgroundColor");
                host.Children.Add(toolbar); host.Children.Add(auto); host.Children.Add(lab);
                Layout(host, 1500, 1250);
                Color foreground = ((SolidColorBrush)application.FindResource("ForegroundColor")).Color;
                Color raised = ((SolidColorBrush)application.FindResource("RaisedBackgroundColor")).Color;
                if (theme == "DefaultTheme") Assert.AreEqual(Color.FromRgb(234, 241, 250), raised);
                var texts = Visuals(lab).OfType<TextBlock>().ToArray();
                foreach (var title in texts.Where(t => t.Text is "L2" or "R2" or "Full-pull action" or "Game rumble vibration"))
                    Assert.AreEqual(foreground, ((SolidColorBrush)title.Foreground).Color, title.Text);
                var surfaces = Visuals(lab).OfType<Border>().Where(b =>
                    b.Style == lab.FindResource("LabRaisedSurface")).ToArray();
                Assert.AreEqual(8, surfaces.Length);
                foreach (var surface in surfaces) Assert.AreEqual(raised, ((SolidColorBrush)surface.Background).Color);
                foreach (var button in Visuals(toolbar).OfType<Button>().Concat(auto.Children.OfType<Button>()))
                {
                    button.IsEnabled = true;
                    bool primary = button.Name == "newProfListBtn";
                    Color expected = primary ? Colors.White : button.Name == "deleteProfBtn"
                        ? ((SolidColorBrush)application.FindResource("DangerColor")).Color : foreground;
                    Assert.AreEqual(expected, ((SolidColorBrush)button.Foreground).Color, button.Name);
                    foreach (var text in Visuals(button).OfType<TextBlock>())
                        Assert.AreEqual(expected, ((SolidColorBrush)text.Foreground).Color, button.Name + ": " + text.Text);
                    if (!primary)
                    {
                        var chrome = (Border)button.Template.FindName("Chrome", button);
                        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",
                            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
                        button.SetValue(hoverKey, true);
                        Assert.AreEqual(raised, ((SolidColorBrush)chrome.Background).Color, "Hover must follow the current theme.");
                        button.SetValue(hoverKey, false);
                        button.IsEnabled = false;
                        Assert.AreEqual(0.5, button.Opacity, "Unavailable actions must remain visibly distinct.");
                        button.IsEnabled = true;
                    }
                }
                Save(host, Path.Combine(directory, "Theme-consistency-" + theme + ".png"), 1500, 1250);
                host.Children.Clear();
                ValidateRgb(theme, directory);
            }
            foreach (string theme in new[] { "DarkTheme", "DefaultTheme" })
            foreach (string prefix in new[] { "Theme-consistency-", "Lightbar-colours-" })
                renderedFiles.Add(Path.Combine(directory, prefix + theme + ".png"));
        }
        finally
        {
            Global.store.triggerLabSettings[8] = settings;
            application.Resources.MergedDictionaries.Clear();
            foreach (var item in original) application.Resources.MergedDictionaries.Add(item);
            if (converter == null) application.Resources.Remove("ShellViewportWidth");
            else application.Resources["ShellViewportWidth"] = converter;
        }
    }

    private static ResourceDictionary Dictionary(string name) => new()
    {
        Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{name}.xaml", UriKind.Relative)
    };

    private static void ValidateRgb(string theme, string directory)
    {
        Grid root = ProfileAdvancedLayoutTests.Load(theme, "Lightbar");
        foreach (var tab in Logical(root).OfType<TabControl>()) tab.SelectedIndex = 0;
        Layout(root, 980, 1300);
        var surfaces = Visuals(root).OfType<Border>().Where(b => b.Height == 40 && b.Child is Slider).ToArray();
        Assert.AreEqual(6, surfaces.Length);
        string[] light = ["#FFFDE8E8", "#FFE7F5EB", "#FFE5EEFF"];
        for (int i = 0; i < surfaces.Length; i++)
        {
            foreach (int alpha in new[] { 0, 96, 255 })
            {
                string colour = $"#{alpha:X2}" + (i % 3 == 0 ? "FF0000" : i % 3 == 1 ? "00FF00" : "0000FF");
                surfaces[i].Tag = colour;
                Color expected = (Color)ColorConverter.ConvertFromString(theme == "DarkTheme" ? colour : light[i % 3]);
                Assert.AreEqual(expected, ((SolidColorBrush)surfaces[i].Background).Color,
                    "Both normal and low-battery channels must retain their dark behaviour and readable light tints.");
            }
            surfaces[i].Tag = "#60" + (i % 3 == 0 ? "FF0000" : i % 3 == 1 ? "00FF00" : "0000FF");
        }
        Layout(root, 980, 1300);
        Save(root, Path.Combine(directory, "Lightbar-colours-" + theme + ".png"), 980, 1300);
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject parent)
    {
        yield return parent;
        foreach (object item in LogicalTreeHelper.GetChildren(parent))
            if (item is DependencyObject child)
                foreach (var descendant in Logical(child)) yield return descendant;
    }

    private static FrameworkElement Extract(string file, string name)
    {
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = LocalizedSource.Load(Path.Combine(SourceDirectory(), file + ".xaml"));
        var element = new XElement(source.Descendants().Single(e => (string)e.Attribute(x + "Name") == name));
        foreach (var menu in element.Descendants(wpf + "Button.ContextMenu").ToArray()) menu.Remove();
        foreach (var attr in element.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attr.Name.LocalName == "Click") attr.Remove();
            else if (attr.Value.StartsWith("{lex:Loc ")) attr.Value = LocalizedSource.Resolve(attr.Value);
        }
        // A toolbar button keeps its page-local style when rendered in isolation.
        if ((string)element.Attribute("Style") == "{StaticResource AutoProfileToolbarButtonStyle}")
        {
            var style = source.Descendants(wpf + "Style").Single(e => (string)e.Attribute(x + "Key") == "AutoProfileToolbarButtonStyle");
            var inlineStyle = new XElement(style);
            inlineStyle.Attribute(x + "Key")!.Remove();
            element.Attribute("Style")!.Remove();
            element.AddFirst(new XElement(wpf + "Button.Style", inlineStyle));
        }
        element.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        return (FrameworkElement)XamlReader.Parse(LocalizedSource.Markup(element));
    }

    internal static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }

    internal static void Save(FrameworkElement element, string path, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    internal static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Visuals(child)) yield return nested;
        }
    }

    private static string SourceDirectory([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "DS4Windows", "DS4Forms"));
}
