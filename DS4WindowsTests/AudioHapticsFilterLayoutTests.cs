using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace DS4WindowsTests;

[TestClass]
public sealed class AudioHapticsFilterLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme", 520d)]
    [DataRow("DefaultTheme", 520d)]
    [DataRow("DarkTheme", 1040d)]
    [DataRow("DefaultTheme", 1040d)]
    public void ExpandedFiltersFitBothThemesAndKeepValuesWhenCollapsed(string theme, double width)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = Load(theme);
                var low = (Slider)root.FindName("lowPassSlider");
                var high = (Slider)root.FindName("highPassSlider");
                var toggle = (CheckBox)root.FindName("customFiltersToggle");
                var expander = (Expander)System.Windows.LogicalTreeHelper.GetParent(System.Windows.LogicalTreeHelper.GetParent(toggle));
                expander.IsExpanded = true;
                low.Value = 160; high.Value = 20;
                ((TextBlock)root.FindName("lowPassValueText")).Text = "160 Hz";
                ((TextBlock)root.FindName("highPassValueText")).Text = "20 Hz";
                root.Measure(new Size(width, 1100));
                root.Arrange(new Rect(0, 0, width, 1100));
                root.UpdateLayout();
                Assert.IsTrue(low.ActualWidth > 100 && high.ActualWidth > 100);
                foreach (FrameworkElement element in new FrameworkElement[] { low, high, toggle })
                {
                    Point point = element.TranslatePoint(new Point(), root);
                    Assert.IsTrue(point.X >= 0 && point.X + element.ActualWidth <= width + 1,
                        $"{theme}/{width}: {element.Name} overflows.");
                    Assert.IsTrue(element.ActualHeight > 0);
                }
                string directory = Environment.GetEnvironmentVariable("DS4W_AUDIO_FILTER_LAYOUT_ARTIFACTS");
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    var bitmap = new RenderTargetBitmap((int)width, 1100, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(directory, $"AudioFilters-{theme}-{width}.png"));
                    encoder.Save(file);
                }
                expander.IsExpanded = false;
                root.UpdateLayout();
                Assert.AreEqual(160d, low.Value);
                Assert.AreEqual(20d, high.Value);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static UserControl Load(string theme, [CallerFilePath] string source = "")
    {
        string forms = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "DS4Windows", "DS4Forms"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement markup = XDocument.Load(Path.Combine(forms, "AudioHapticsControl.xaml")).Root!;
        markup.Attribute(x + "Class")!.Remove();
        markup.Attribute("Width")!.Remove();
        foreach (var attribute in markup.DescendantsAndSelf().Attributes().ToArray())
            if (attribute.Name.LocalName is "Click" or "SelectionChanged" or "ValueChanged") attribute.Remove();
        XElement bridge = XDocument.Load(Path.Combine(forms, "Themes", "BridgeShellStyles.xaml")).Root!;
        bridge.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
            new XElement(wpf + "ResourceDictionary", new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))));
        XElement resources = markup.Element(wpf + "UserControl.Resources")!;
        var existing = resources.Elements().ToArray();
        resources.RemoveNodes();
        resources.Add(new XElement(wpf + "ResourceDictionary",
            new XElement(wpf + "ResourceDictionary.MergedDictionaries", bridge), existing));
        return (UserControl)XamlReader.Parse(markup.ToString());
    }
}
