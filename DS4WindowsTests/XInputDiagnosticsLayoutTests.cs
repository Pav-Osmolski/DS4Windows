using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class XInputDiagnosticsLayoutTests
{
    [TestMethod]
    [DataRow("DefaultTheme", 812, 792)]
    [DataRow("DarkTheme", 812, 792)]
    [DataRow("DefaultTheme", 672, 392)]
    [DataRow("DarkTheme", 672, 392)]
    public void FourLiveSlotsRemainReadableAndScrollable(string theme, int width, int height)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var document = LocalizedSource.Load(SourcePath());
            var markup = new XElement(document.Root.Element(wpf + "DockPanel"));
            markup.SetAttributeValue(XNamespace.Xmlns + "x", xaml.NamespaceName);
            markup.Descendants().Attributes("Click").Remove();
            // This is the production content with production bindings. No window is
            // shown, timer started, or native XInput queried by the render test.
            var styles = LocalizedSource.Load(Path.Combine(Path.GetDirectoryName(SourcePath()), "Themes", "BridgeShellStyles.xaml")).Root;
            styles.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
                new XElement(wpf + "ResourceDictionary", new XAttribute("Source",
                    $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))));
            markup.AddFirst(new XElement(wpf + "DockPanel.Resources", styles));
            var content = (DockPanel)XamlReader.Parse(LocalizedSource.Markup(markup));
            content.Margin = new Thickness(0);
            var host = new Border { Child = content };
            host.Background = (Brush)content.FindResource("SurfaceBackgroundColor");
            TextElement.SetForeground(host, (Brush)content.FindResource("ForegroundColor"));
            var model = new XInputDiagnosticsViewModel();
            model.Slots[0].Apply(new(0, new() { Gamepad = new() {
                Buttons = 0x1001, LeftX = -32768, RightY = 32767, LeftTrigger = 180 } }));
            model.Slots[1].Apply(new(1167, default));
            model.Slots[2].Apply(new(0, new() { Gamepad = new() { Buttons = 0xFFFF } }));
            model.Slots[3].Apply(new(5, default));
            host.DataContext = model;
            Layout(host, width, height);
            var visuals = Visuals(host).ToArray();
            Assert.AreEqual(4, visuals.OfType<TextBlock>().Count(text => text.Text.StartsWith("Slot ")));
            Assert.AreEqual(8, visuals.OfType<ProgressBar>().Count());
            Assert.IsTrue(visuals.OfType<TextBlock>().Any(text => text.Text == "X: -32768   Y: 0"));
            Assert.IsTrue(visuals.OfType<TextBlock>().Any(text => text.Text == "Read failed (Windows error 5)"));
            foreach (var text in visuals.OfType<TextBlock>().Where(text => text.Text.StartsWith("X: ")))
                Assert.IsTrue(text.DesiredSize.Width <= text.ActualWidth + 1, "Raw stick readings must fit at minimum width.");
            var scroll = visuals.OfType<ScrollViewer>().First();
            scroll.ScrollToEnd();
            Layout(host, width, height);
            Assert.AreEqual(scroll.ScrollableHeight, scroll.VerticalOffset, 1d);
            string directory = Environment.GetEnvironmentVariable("DS4W_XINPUT_LAYOUT_ARTIFACTS");
            if (!string.IsNullOrEmpty(directory))
            {
                scroll.ScrollToHome(); Layout(host, width, height);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(directory);
                using var file = File.Create(Path.Combine(directory, $"xinput-{theme}-{width}.png"));
                encoder.Save(file);
            }
            model.Close();
        });
    }

    private static void Layout(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
    private static string SourcePath([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", "DS4Windows", "DS4Forms", "XInputDiagnosticsWindow.xaml"));
}
