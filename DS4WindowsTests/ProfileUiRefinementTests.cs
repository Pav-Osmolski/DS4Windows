using System.Reflection;
using DS4Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class ProfileUiRefinementTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void ColourPickerRendersAndSynchronisesPaletteRgbAndHexInEveryTheme()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var choice in AppThemeCatalog.Choices.Where(c => c.Value > 0))
            {
                var window = new ColorPickerWindow();
                window.Resources.MergedDictionaries.Add(AppThemeCatalog.Create((AppThemeChoice)choice.Value, highContrast: false));
                window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                try
                {
                    Assert.AreEqual("Choose colour", window.Title);
                    int events = 0;
                    window.ColorChanged += (_, _) => events++;
                    window.colorPicker.SelectedColor = Color.FromRgb(47, 128, 237);
                    var content = (FrameworkElement)window.Content;
                    ThemeConsistencyValidation.Layout(content, 400, 510);
                    window.colorPicker.ApplyTemplate();
                    var palette = (Canvas)window.colorPicker.Template.FindName("PART_ColorShadingCanvas", window.colorPicker);
                    Assert.IsGreaterThan(200d, palette.ActualWidth);
                    Assert.AreEqual((byte)128, window.colorPicker.G);
                    window.colorPicker.R = 160;
                    Assert.AreEqual((byte)160, window.colorPicker.SelectedColor.Value.R);
                    window.colorPicker.HexadecimalString = "#123456";
                    Assert.AreEqual(Color.FromRgb(18, 52, 86), window.colorPicker.SelectedColor.Value);
                    Assert.IsGreaterThanOrEqualTo(3, events);
                    var hex = (TextBox)window.colorPicker.Template.FindName("PART_HexadecimalTextBox", window.colorPicker);
                    StringAssert.Contains(hex.Text.ToUpperInvariant(), "123456");
                    foreach (var number in Visuals(content).OfType<Xceed.Wpf.Toolkit.IntegerUpDown>())
                    { Assert.AreEqual(76d, number.ActualWidth); Assert.AreEqual(32d, number.ActualHeight); }
                    ThemeConsistencyValidation.Layout(content, 400, 510);
                    DisclosureAnimationTests.Pump(30);
                    var selector = (Canvas)window.colorPicker.Template.FindName("PART_ColorShadeSelector", window.colorPicker);
                    var position = (TranslateTransform)selector.RenderTransform;
                    Assert.IsTrue(double.IsFinite(position.X) && double.IsFinite(position.Y), "Palette selection must stay visible after RGB/hex edits.");
                    var scroll = Visuals(content).OfType<ScrollViewer>().First();
                    Assert.IsLessThanOrEqualTo(1d, scroll.ScrollableHeight, "The default picker must show all three RGB rows.");
                    string path = Path.Combine(TestContext.TestResultsDirectory, "colour-picker-" + choice.SourceName + ".png");
                    var image = new RenderTargetBitmap(400, 510, 96, 96, PixelFormats.Pbgra32);
                    image.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using (var output = File.Create(path)) encoder.Save(output);
                    TestContext.AddResultFile(path);
                }
                finally { window.Close(); }
            }
        });
    }

    [TestMethod]
    public void StickAndMotionPlotsShareTheSameTopEdgeInEveryTheme()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var choice in AppThemeCatalog.Choices.Where(c => c.Value > 0))
            {
                var root = ProfileAdvancedLayoutTests.Load("DarkTheme", "ControllerReadings");
                root.Resources.MergedDictionaries.Add(AppThemeCatalog.Create((AppThemeChoice)choice.Value, highContrast: false));
                ThemeConsistencyValidation.Layout(root, 980, 740);
                var plots = new[] { "lsCanvas", "rsCanvas", "sixaxisCanvas" }.Select(n => (Canvas)root.FindName(n)).ToArray();
                double top = plots[0].TranslatePoint(new Point(), root).Y;
                foreach (var plot in plots) Assert.AreEqual(top, plot.TranslatePoint(new Point(), root).Y, .01, choice.SourceName);
            }
        });
    }

    [TestMethod]
    public void StorageHeaderHoverAndPressFadeAndRemainHighlightedOverSiblingArrow()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var header = new Grid();
            var button = new Button { Content = "Settings & profiles" }; header.Children.Add(button);
            using var source = new HwndSource(new HwndSourceParameters("Profile header verification") { Width = 400, Height = 80, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = header;
            header.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(AppThemeChoice.Dark, highContrast: false));
            header.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
            button.SetResourceReference(FrameworkElement.StyleProperty, "BridgeDisclosureHeaderButtonStyle");
            ThemeConsistencyValidation.Layout(header, 400, 80);
            var hover = (Border)button.Template.FindName("HoverSurface", button);
            var press = (Border)button.Template.FindName("PressedSurface", button);
            void Set(string field, bool value) => header.SetValue((DependencyPropertyKey)typeof(UIElement).GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), value);
            Set("IsMouseOverPropertyKey", true);
            DisclosureAnimationTests.Pump(190);
            Assert.AreEqual(1d, hover.Opacity, .01);
            Set("IsMouseCaptureWithinPropertyKey", true);
            DisclosureAnimationTests.Pump(110);
            Assert.AreEqual(1d, press.Opacity, .01);
            Set("IsMouseCaptureWithinPropertyKey", false);
            DisclosureAnimationTests.Pump(160);
            Assert.AreEqual(0d, press.Opacity, .01);
            Assert.AreEqual(1d, hover.Opacity, .01, "Arrow release must keep the parent hover.");
            Set("IsMouseOverPropertyKey", false);
            DisclosureAnimationTests.Pump(190);
            Assert.AreEqual(0d, hover.Opacity, .01);
        });
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
