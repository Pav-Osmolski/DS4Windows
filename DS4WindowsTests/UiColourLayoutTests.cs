using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class UiColourLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme")]
    [DataRow("DefaultTheme")]
    public void BindingRgbRailsAndThumbsAreCentredInEqualSurfaces(string theme)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = LocalizedSource.Load(Source("BindingWindow.xaml"));
            var resource = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(wpf + "ResourceDictionary.MergedDictionaries", new XElement(wpf + "ResourceDictionary",
                    new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))),
                new XElement(source.Descendants(wpf + "Style").Single(e => (string)e.Attribute(x + "Key") == "ProfileRgbSliderStyle")));
            var panel = new StackPanel { Width = 160, Resources = (ResourceDictionary)XamlReader.Parse(LocalizedSource.Markup(resource)) };
            var channels = source.Descendants(wpf + "Border").Where(e => ((string)e.Attribute("Tag"))?.Contains("ExtrasColor") == true).ToArray();
            Assert.AreEqual(3, channels.Length);
            foreach (var channel in channels)
            {
                var rowMarkup = new XElement(channel.Parent!);
                rowMarkup.AddFirst(new XElement(wpf + "Grid.Resources", new XElement(resource)));
                var row = (Grid)XamlReader.Parse(LocalizedSource.Markup(rowMarkup));
                panel.Children.Add(row);
            }
            panel.Measure(new Size(160, 500));
            panel.Arrange(new Rect(0, 0, 160, panel.DesiredSize.Height));
            panel.UpdateLayout();
            foreach (Grid row in panel.Children)
            {
                var border = row.Children.OfType<Border>().Single();
                var slider = (Slider)border.Child;
                slider.Value = 127.5;
                panel.UpdateLayout();
                var rail = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
                Assert.IsNotNull(rail);
                var thumb = rail.Thumb;
                Assert.AreEqual(18d, border.TranslatePoint(new Point(), row).X, 0.01);
                Assert.AreEqual(142d, border.ActualWidth, 0.01);
                foreach (FrameworkElement part in new FrameworkElement[] { rail, thumb })
                {
                    var point = part.TranslatePoint(new Point(part.ActualWidth / 2, part.ActualHeight / 2), border);
                    Assert.AreEqual(border.ActualWidth / 2, point.X, 0.01);
                    Assert.AreEqual(border.ActualHeight / 2, point.Y, 0.01);
                }
            }
            string output = Environment.GetEnvironmentVariable("DS4W_COLOUR_ARTIFACTS");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                foreach (double scale in new[] { 1d, 1.5d, 2d })
                {
                    var bitmap = new RenderTargetBitmap((int)(160 * scale), (int)(panel.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(panel);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"rgb-{theme}-{scale * 100:0}.png")); encoder.Save(file);
                }
            }
        });
    }

    [TestMethod]
    [DataRow("DarkTheme", false)]
    [DataRow("DarkTheme", true)]
    [DataRow("DefaultTheme", false)]
    [DataRow("DefaultTheme", true)]
    public void AddProgramsPopupUsesCurrentThemeAndHasNoOuterItemPadding(string theme, bool hideSteam)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = LocalizedSource.Load(Source("AutoProfiles.xaml"));
            var dictionary = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(wpf + "ResourceDictionary.MergedDictionaries", new XElement(wpf + "ResourceDictionary",
                    new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))),
                source.Descendants(wpf + "Style").Where(e => (string)e.Attribute(x + "Key") is "AutoProfileMenuItemStyle" or "AutoProfileMenuStyle").Select(e => new XElement(e)));
            var resources = (ResourceDictionary)XamlReader.Parse(LocalizedSource.Markup(dictionary));
            var menu = new ContextMenu { Resources = resources, Style = (Style)resources["AutoProfileMenuStyle"] };
            var actualMenu = source.Descendants(wpf + "Button").Single(e => (string)e.Attribute(x + "Name") == "addProgramsBtn")
                .Descendants(wpf + "ContextMenu").Single();
            foreach (var entry in actualMenu.Elements())
            {
                if (entry.Name == wpf + "Separator") menu.Items.Add(new Separator());
                else if (entry.Name == wpf + "MenuItem") menu.Items.Add(new MenuItem
                {
                    Header = (string)entry.Attribute("Header"),
                    Visibility = hideSteam && (string)entry.Attribute(x + "Name") == "steamMenuItem" ? Visibility.Collapsed : Visibility.Visible
                });
            }
            Assert.AreEqual(6, menu.Items.Count);
            menu.ApplyTemplate();
            menu.Measure(new Size(500, 500));
            menu.Arrange(new Rect(new Point(), menu.DesiredSize));
            menu.UpdateLayout();
            Assert.IsTrue(menu.ActualHeight > 0);
            foreach (var item in menu.Items.OfType<MenuItem>()) Assert.AreEqual(typeof(MenuItem), item.Style.TargetType);
            var separator = menu.Items.OfType<Separator>().Single();
            Assert.AreEqual(typeof(Separator), separator.Style.TargetType);
            var paintedLine = Descendants(separator).OfType<Border>().FirstOrDefault(border =>
                border.Background is SolidColorBrush || border.BorderThickness.Top > 0 || border.BorderThickness.Bottom > 0);
            Assert.IsNotNull(paintedLine, "The separator must render a line.");
            Assert.AreEqual(0d, paintedLine.TranslatePoint(new Point(), separator).X, 0.01,
                "The visible separator line must not inherit the native menu icon-gutter inset.");
            Assert.AreEqual(separator.ActualWidth, paintedLine.ActualWidth, 0.01);
            var left = separator.TranslatePoint(new Point(), menu);
            var right = separator.TranslatePoint(new Point(separator.ActualWidth, 0), menu);
            Assert.AreEqual(menu.BorderThickness.Left, left.X, 0.01, "The separator must start at the menu's inside edge.");
            Assert.AreEqual(menu.ActualWidth - menu.BorderThickness.Right, right.X, 0.01, "The separator must reach the opposite inside edge.");
            Assert.AreEqual(new Thickness(0), menu.Padding);
            Assert.AreEqual(new Thickness(1), menu.BorderThickness);
            var initialColour = ((SolidColorBrush)menu.Background).Color;
            string nextTheme = theme == "DarkTheme" ? "DefaultTheme" : "DarkTheme";
            resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{nextTheme}.xaml", UriKind.Relative) };
            Assert.AreNotEqual(initialColour, ((SolidColorBrush)menu.Background).Color);
            menu.Measure(new Size(500, 500));
            menu.UpdateLayout();
            Assert.AreEqual(((SolidColorBrush)resources["CardBackgroundColor"]).Color, ((SolidColorBrush)menu.Background).Color);
            Assert.AreEqual(((SolidColorBrush)resources["ForegroundColor"]).Color, ((SolidColorBrush)menu.Foreground).Color);
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static string Source(string filename, [CallerFilePath] string caller = "") =>
        Path.Combine(Path.GetDirectoryName(caller)!, "..", "DS4Windows", "DS4Forms", filename);
}
