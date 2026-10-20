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
            var source = XDocument.Load(Source("BindingWindow.xaml"));
            var resource = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(wpf + "ResourceDictionary.MergedDictionaries", new XElement(wpf + "ResourceDictionary",
                    new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))),
                new XElement(source.Descendants(wpf + "Style").Single(e => (string)e.Attribute(x + "Key") == "ProfileRgbSliderStyle")));
            var panel = new StackPanel { Width = 160, Resources = (ResourceDictionary)XamlReader.Parse(resource.ToString()) };
            var channels = source.Descendants(wpf + "Border").Where(e => ((string)e.Attribute("Tag"))?.Contains("ExtrasColor") == true).ToArray();
            Assert.AreEqual(3, channels.Length);
            foreach (var channel in channels)
            {
                var rowMarkup = new XElement(channel.Parent!);
                rowMarkup.AddFirst(new XElement(wpf + "Grid.Resources", new XElement(resource)));
                var row = (Grid)XamlReader.Parse(rowMarkup.ToString());
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
                var rail = (FrameworkElement)slider.Template.FindName("RgbRail", slider);
                var thumb = (FrameworkElement)slider.Template.FindName("RgbThumb", slider);
                Assert.AreEqual(18d, border.TranslatePoint(new Point(), row).X, 0.01);
                Assert.AreEqual(142d, border.ActualWidth, 0.01);
                foreach (var part in new[] { rail, thumb })
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
    public void AddProgramsPopupUsesCurrentThemeAndHasNoOuterItemPadding()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = XDocument.Load(Source("AutoProfiles.xaml"));
            var dictionary = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(wpf + "ResourceDictionary.MergedDictionaries", new XElement(wpf + "ResourceDictionary",
                    new XAttribute("Source", "/DS4Windows;component/DS4Forms/Themes/DarkTheme.xaml"))),
                source.Descendants(wpf + "Style").Where(e => (string)e.Attribute(x + "Key") is "AutoProfileMenuItemStyle" or "AutoProfileMenuStyle").Select(e => new XElement(e)));
            var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            var menu = new ContextMenu { Resources = resources, Style = (Style)resources["AutoProfileMenuStyle"] };
            menu.Items.Add(new MenuItem { Header = "Add Directory" });
            Assert.AreEqual(new Thickness(0), menu.Padding);
            Assert.AreEqual(new Thickness(1), menu.BorderThickness);
            var dark = ((SolidColorBrush)menu.Background).Color;
            resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/DefaultTheme.xaml", UriKind.Relative) };
            Assert.AreNotEqual(dark, ((SolidColorBrush)menu.Background).Color);
            Assert.AreEqual(((SolidColorBrush)resources["CardBackgroundColor"]).Color, ((SolidColorBrush)menu.Background).Color);
            Assert.AreEqual(((SolidColorBrush)resources["ForegroundColor"]).Color, ((SolidColorBrush)menu.Foreground).Color);
        });
    }

    private static string Source(string filename, [CallerFilePath] string caller = "") =>
        Path.Combine(Path.GetDirectoryName(caller)!, "..", "DS4Windows", "DS4Forms", filename);
}
