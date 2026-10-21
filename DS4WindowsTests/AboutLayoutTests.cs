using System.Runtime.CompilerServices;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class AboutLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme", 912d, 704d)]
    [DataRow("DefaultTheme", 912d, 704d)]
    [DataRow("DarkTheme", 568d, 404d)]
    [DataRow("DefaultTheme", 568d, 404d)]
    public void AllPagesRemainReachableAndUseTheCurrentTheme(string theme, double width, double height)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var window = new About();
            try
            {
                var dictionary = new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative) };
                window.Resources.MergedDictionaries.Add(dictionary);
                var root = (Grid)window.Content;
                root.Margin = new Thickness(0);
                window.Content = null;
                var surface = new Border { Child = root, Padding = new Thickness(24, 20, 24, 16), Resources = window.Resources };
                surface.SetResourceReference(Border.BackgroundProperty, "BackgroundColor");
                ((TextBlock)window.FindName("versionText")).Text = "Version " + typeof(About).Assembly.GetName().Version!.ToString(3);
                var tabs = (TabControl)window.FindName("aboutTabs");
                Assert.AreEqual(5, tabs.Items.Count);
                Assert.AreEqual(0, tabs.SelectedIndex);
                foreach (int index in Enumerable.Range(0, tabs.Items.Count))
                {
                    tabs.SelectedIndex = index;
                    surface.Measure(new Size(width, height));
                    surface.Arrange(new Rect(0, 0, width, height));
                    surface.UpdateLayout();
                    surface.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
                    surface.UpdateLayout();
                    Assert.IsFalse(window.IsVisible, "Layout checks must not open a desktop window.");
                    FrameworkElement page = (FrameworkElement)((TabItem)tabs.Items[index]).Content;
                    Assert.IsTrue(page.ActualHeight > 20, "The page must retain a usable reading area.");
                    if (page is ScrollViewer scroll)
                    {
                        Assert.AreEqual(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                        scroll.ScrollToEnd();
                        surface.UpdateLayout();
                        Assert.AreEqual(scroll.ScrollableHeight, scroll.VerticalOffset, 0.1);
                        scroll.ScrollToTop();
                        surface.UpdateLayout();
                        scroll.ScrollToEnd();
                        surface.UpdateLayout();
                        tabs.SelectedIndex = (index + 1) % tabs.Items.Count;
                        surface.UpdateLayout();
                        tabs.SelectedIndex = index;
                        surface.UpdateLayout();
                        Assert.IsTrue(scroll.Focusable && scroll.IsTabStop, "The scroll container must receive initial focus before any links.");
                        Assert.AreEqual(0d, scroll.VerticalOffset, 0.1, "The first layout must already be at the top, before idle callbacks can hide a flash.");
                        surface.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
                        surface.UpdateLayout();
                        Assert.AreEqual(0d, scroll.VerticalOffset, 0.1, "Idle processing must not move the page.");
                    }
                    foreach (var button in Descendants(page).OfType<Button>())
                    {
                        var chrome = button.Template.FindName("ButtonSurface", button) as Border;
                        if (chrome == null) continue;
                        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                        button.SetValue(hoverKey, true);
                        Assert.AreEqual(((SolidColorBrush)dictionary["NavigationSelectionColor"]).Color, ((SolidColorBrush)chrome.Background).Color);
                        Assert.AreEqual(((SolidColorBrush)dictionary["ForegroundColor"]).Color, ((SolidColorBrush)button.Foreground).Color);
                        Assert.AreEqual(((SolidColorBrush)dictionary["AccentColor"]).Color, ((SolidColorBrush)chrome.BorderBrush).Color);
                        button.SetValue(hoverKey, false);
                        Assert.AreEqual(((SolidColorBrush)dictionary["RaisedBackgroundColor"]).Color, ((SolidColorBrush)chrome.Background).Color);
                    }
                    var firstCard = Descendants(page).OfType<Border>().FirstOrDefault(b => b.CornerRadius.TopLeft == 8);
                    if (firstCard != null && index != 0)
                        Assert.AreEqual(((SolidColorBrush)dictionary["CardBackgroundColor"]).Color, ((SolidColorBrush)firstCard.Background).Color);
                    string output = Environment.GetEnvironmentVariable("DS4W_ABOUT_ARTIFACTS");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        foreach (double scale in new[] { 1d, 1.5d, 2d })
                        {
                            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                            bitmap.Render(surface);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(output, $"about-{theme}-{index}-{scale * 100:0}{(width < 900 ? "-compact" : "")}.png"));
                            encoder.Save(file);
                        }
                        if (index == 2)
                        {
                            var link = Descendants(page).OfType<Button>().First();
                            var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                            link.SetValue(hoverKey, true);
                            surface.UpdateLayout();
                            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(surface);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(output, $"hover-{theme}{(width < 900 ? "-compact" : "")}.png"));
                            encoder.Save(file);
                            link.SetValue(hoverKey, false);
                        }
                    }
                }
                window.Resources.MergedDictionaries.Clear();
                string other = theme == "DarkTheme" ? "DefaultTheme" : "DarkTheme";
                var replacement = new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{other}.xaml", UriKind.Relative) };
                window.Resources.MergedDictionaries.Add(replacement);
                tabs.SelectedIndex = 1;
                surface.UpdateLayout();
                var changedCard = Descendants((DependencyObject)((TabItem)tabs.Items[1]).Content).OfType<Border>().First(b => b.CornerRadius.TopLeft == 8);
                Assert.AreEqual(((SolidColorBrush)replacement["CardBackgroundColor"]).Color, ((SolidColorBrush)changedCard.Background).Color);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void FullBundledLicenseMatchesTheCanonicalCopy()
    {
        string canonical = File.ReadAllText(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "COPYING"));
        Assert.AreEqual(canonical.Replace("\r\n", "\n"), About.LoadLicenseText().Replace("\r\n", "\n"));
        StringAssert.Contains(About.LoadLicenseText(), "END OF TERMS AND CONDITIONS");
    }

    [TestMethod]
    [DataRow(1920d, 1080d, 960d, 780d)]
    [DataRow(640d, 360d, 616d, 336d)]
    public void WindowFitsScaledWorkingArea(double width, double height, double expectedWidth, double expectedHeight)
    {
        Size size = About.GetWindowSize(width, height);
        Assert.AreEqual(expectedWidth, size.Width);
        Assert.AreEqual(expectedHeight, size.Height);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }

    private static string SourcePath([CallerFilePath] string file = "") => file;
}
