using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DS4WindowsTests;

[TestClass]
public sealed class ControllerReadingsLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme", 480d)]
    [DataRow("DefaultTheme", 480d)]
    [DataRow("DarkTheme", 980d)]
    [DataRow("DefaultTheme", 980d)]
    public void LongStatusAndExtremeReadingsFitWithoutChangingPlotCoordinates(string theme, double width)
    {
        Exception failure = null;
        Action thread = () =>
        {
            try
            {
                Grid root = ProfileAdvancedLayoutTests.Load(theme, "ControllerReadings");
                ((Label)root.FindName("inputDelayLb")).Content = "Observed physical report interval: 123.456 ms (not end-to-end latency)";
                ((Label)root.FindName("batteryLvlLb")).Content = "Battery: 100%";
                ((SolidColorBrush)root.FindName("inpuDelayForeBrush")).Color = ((SolidColorBrush)root.FindResource("ForegroundColor")).Color;
                foreach (string name in new[] { "lxInValLb", "lxOutValLb", "lyInValLb", "lyOutValLb", "rxInValLb", "rxOutValLb", "ryInValLb", "ryOutValLb", "l2InValLb", "l2OutValLb", "r2InValLb", "r2OutValLb" })
                    ((Label)root.FindName(name)).Content = 255;
                foreach (string name in new[] { "sixAxisXInValLb", "sixAxisZInValLb", "sixAxisXOutValLb", "sixAxisZOutValLb" })
                    ((Label)root.FindName(name)).Content = -128;
                foreach (string name in new[] { "lsCanvas", "rsCanvas", "sixaxisCanvas" })
                {
                    var canvas = (Canvas)root.FindName(name);
                    Assert.AreEqual(130d, canvas.Width);
                    Assert.AreEqual(130d, canvas.Height);
                }
                foreach (string name in new[] { "l2Slider", "r2Slider" })
                {
                    var slider = (Slider)root.FindName(name);
                    slider.Value = 255;
                    Assert.AreEqual(255d, slider.Maximum);
                    Assert.IsFalse(slider.IsHitTestVisible, "Live telemetry must not look like an editable setting.");
                }
                var scroll = root.Children.OfType<ScrollViewer>().Single();
                foreach (bool end in new[] { false, true })
                {
                    root.Measure(new Size(width, 740));
                    root.Arrange(new Rect(0, 0, width, 740));
                    root.UpdateLayout();
                    if (end) scroll.ScrollToEnd();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    root.UpdateLayout();
                    foreach (Control control in Visuals(root).OfType<Control>().Where(c => c.IsVisible && c.ActualWidth > 0))
                    {
                        Point point = control.TranslatePoint(new Point(), root);
                        Assert.IsTrue(point.X >= -2 && point.X + control.ActualWidth <= width + 2, $"{theme}/{width}: {control.Name} overflows.");
                    }
                    string directory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                        var bitmap = new RenderTargetBitmap((int)width, 740, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(root);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(directory, $"Readings-{theme}-{width}-{end}.png"));
                        encoder.Save(file);
                    }
                }
            }
            catch (Exception error) { failure = error; }
        };
        HeadlessWpfDispatcher.Invoke(thread);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    [DataRow("DarkTheme")]
    [DataRow("DefaultTheme")]
    public void RgbSlidersAreCenteredInsideTheirColorSurfaces(string theme)
    {
        Exception failure = null;
        Action thread = () =>
        {
            try
            {
                Grid root = ProfileAdvancedLayoutTests.Load(theme, "Lightbar");
                foreach (TabControl mode in Logical(root).OfType<TabControl>()) mode.SelectedIndex = 0;
                root.Measure(new Size(980, 740));
                root.Arrange(new Rect(0, 0, 980, 740));
                root.UpdateLayout();
                Border[] surfaces = Visuals(root).OfType<Border>().Where(b => b.Height == 40 && b.Child is Slider).ToArray();
                Assert.AreEqual(6, surfaces.Length);
                foreach (Border surface in surfaces)
                {
                    var slider = (Slider)surface.Child;
                    Point center = slider.TranslatePoint(new Point(slider.ActualWidth / 2, slider.ActualHeight / 2), surface);
                    Assert.AreEqual(surface.ActualHeight / 2, center.Y, 0.01, "RGB tracks must share the surface's vertical center.");
                    var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
                    Assert.IsNotNull(track);
                    foreach (FrameworkElement element in new FrameworkElement[] { track, track.Thumb })
                    {
                        Point actualCenter = element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), surface);
                        Assert.AreEqual(surface.ActualHeight / 2, actualCenter.Y, 0.01, element.GetType().Name + " must be centered, not merely the slider's outer bounds.");
                    }
                    slider.Value = 255;
                    Assert.AreEqual(255d, slider.Value);
                    slider.Value = 0;
                    Slider.IncreaseSmall.Execute(null, slider);
                    Assert.AreEqual(slider.SmallChange, slider.Value, "Keyboard adjustment must remain available.");
                    var thumb = track.Thumb;
                    double beforeDrag = slider.Value;
                    thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(10, 0));
                    Assert.IsTrue(slider.Value > beforeDrag, "Thumb dragging must still update the color value.");
                }
            }
            catch (Exception error) { failure = error; }
        };
        HeadlessWpfDispatcher.Invoke(thread);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependency)
                foreach (DependencyObject item in Logical(dependency)) yield return item;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (DependencyObject child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
