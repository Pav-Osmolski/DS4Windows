using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MdXaml;

namespace DS4WindowsTests;

[TestClass]
public sealed class ChangelogReaderLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme")]
    [DataRow("DefaultTheme")]
    public void MarkdownTablesImagesAndReleaseBoundariesFitBothThemesAndResize(string theme)
    {
        Exception failure = null;
        Action thread = () =>
        {
            string imageFile = Path.Combine(Path.GetTempPath(), $"ds4w-changelog-{Guid.NewGuid():N}.png");
            try
            {
                var bitmap = new WriteableBitmap(2400, 1200, 96, 96, PixelFormats.Bgra32, null);
                byte[] pixels = new byte[2400 * 1200 * 4];
                for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 120; pixels[i+1] = 80; pixels[i+2] = 32; pixels[i+3] = 255; }
                bitmap.WritePixels(new Int32Rect(0, 0, 2400, 1200), pixels, 2400 * 4, 0);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(imageFile)) encoder.Save(file);
                var reader = new MarkdownScrollViewer
                {
                    IsToolBarVisible = false,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
                reader.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative) });
                reader.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/ChangelogReaderStyles.xaml", UriKind.Relative) });
                reader.MarkdownStyle = (Style)reader.FindResource("ChangelogDocumentStyle");
                reader.SetResourceReference(Control.BackgroundProperty, "SurfaceBackgroundColor");
                reader.Markdown = "## Version 5.0.33\n\nA readable release summary with [a link](https://github.com/Pav-Osmolski/DS4Windows).\n\n" +
                    "| Section | Includes |\n| --- | --- |\n| General | Startup and window settings |\n| Appearance | Theme and language |\n\n" +
                    "End of the previous release.\n\n## Version 5.0.32\n\nAnother release.\n\n" +
                    $"![Large screenshot]({new Uri(imageFile).AbsoluteUri})\n\n" +
                    "1. First ordered item\n2. Second ordered item\n\n> A note\n\n```text\nCode example\n```";
                foreach (double width in new[] { 900d, 500d, 900d })
                {
                    Layout(reader, width);
                    var all = Logical(reader.Document).ToArray();
                    var table = all.OfType<Table>().Single();
                    var foreground = ((SolidColorBrush)reader.FindResource("ForegroundColor")).Color;
                    Assert.AreEqual(foreground, ((SolidColorBrush)table.Foreground).Color);
                    var header = table.RowGroups.First(group => Equals(group.Tag, "TableHeader"));
                    Assert.AreEqual(((SolidColorBrush)reader.FindResource("RaisedBackgroundColor")).Color, ((SolidColorBrush)header.Background).Color);
                    var even = all.OfType<TableRow>().First(row => Equals(row.Tag, "EvenTableRow"));
                    Assert.AreEqual(((SolidColorBrush)reader.FindResource("CardBackgroundColor")).Color, ((SolidColorBrush)even.Background).Color);
                    var heading = all.OfType<Paragraph>().Single(p => new TextRange(p.ContentStart, p.ContentEnd).Text.Contains("Version 5.0.32"));
                    var preceding = (Paragraph)heading.PreviousBlock;
                    double gap = heading.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top - preceding.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom;
                    Assert.IsTrue(gap >= 24, $"Release boundary gap too small: {gap}");
                    var image = all.OfType<Image>().Single();
                    Assert.IsTrue(image.ActualWidth > 100 && image.ActualWidth <= width - 40, $"Image overflows {width}: {image.ActualWidth}");
                    Assert.IsTrue(image.ActualHeight > 50 && image.ActualHeight <= reader.ActualHeight * 0.8 + 1);
                    Assert.AreEqual(2d, image.ActualWidth / image.ActualHeight, 0.02, "Image aspect ratio must be preserved.");
                    Assert.IsTrue(all.OfType<System.Windows.Documents.List>().Any(list => list.MarkerStyle == TextMarkerStyle.Decimal), "Ordered lists must retain their numbering.");
                    Save(reader, theme, width);
                }
            }
            catch (Exception error) { failure = error; }
            finally { if (File.Exists(imageFile)) File.Delete(imageFile); }
        };
HeadlessWpfDispatcher.Invoke(thread);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    [DataRow("DarkTheme", 520d)]
    [DataRow("DefaultTheme", 520d)]
    [DataRow("DarkTheme", 900d)]
    [DataRow("DefaultTheme", 900d)]
    public void FullChangelogWindowKeepsHeaderFooterAndRetryReachable(string theme, double width)
    {
        Exception failure = null;
        Action thread = () =>
        {
            DS4WinWPF.DS4Forms.ChangelogWindow window = null;
            try
            {
                window = new DS4WinWPF.DS4Forms.ChangelogWindow();
                var root = (Grid)window.Content;
                root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative) });
                var model = new DS4WinWPF.DS4Forms.ViewModels.ChangelogViewModel(() => Task.FromResult(""));
                window.DataContext = model;
                model.DisplayChangelog().GetAwaiter().GetResult();
                Layout(root, width);
                var controls = Logical(root).OfType<Button>().Where(button => button.IsVisible || button.ActualHeight > 0).ToArray();
                Assert.AreEqual(2, controls.Length, "Retry and Close must both be present on failure.");
                foreach (var button in controls)
                {
                    Point at = button.TranslatePoint(new Point(), root);
                    Assert.IsTrue(at.X >= 0 && at.X + button.ActualWidth <= width + 1);
                    Assert.IsTrue(at.Y >= 0 && at.Y + button.ActualHeight <= 701);
                }
                var reader = (MarkdownScrollViewer)window.FindName("changelogReader");
                Assert.IsTrue(reader.ActualHeight >= 400, "The reading area must retain useful height.");
                Save(root, "Window-error-" + theme, width);
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); }
        };
HeadlessWpfDispatcher.Invoke(thread);         if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Layout(FrameworkElement root, double width)
    {
        root.Measure(new Size(width, 700)); root.Arrange(new Rect(0, 0, width, 700)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); root.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependency)
                foreach (var descendant in Logical(dependency)) yield return descendant;
    }

    private static void Save(FrameworkElement root, string theme, double width)
    {
        string directory = Environment.GetEnvironmentVariable("DS4W_CHANGELOG_LAYOUT_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)width, 700, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(directory, $"Changelog-{theme}-{width}.png")); encoder.Save(file);
    }
}
