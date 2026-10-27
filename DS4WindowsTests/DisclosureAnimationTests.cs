using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using DS4Windows;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class DisclosureAnimationTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void MockStatesUseCentredGeometryAndStableThemeResources()
    {
        string path = Path.Combine(TestContext.TestResultsDirectory, "circular-toggle-states.png");
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var gallery = new StackPanel { Width = 720 };
            using var source = new HwndSource(new HwndSourceParameters("DS4Windows disclosure render")
                { Width = 720, Height = 660, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = gallery;
            foreach (var choice in AppThemeCatalog.Choices.Where(c => c.Value > 0))
            {
                var theme = AppThemeCatalog.Create((AppThemeChoice)choice.Value, highContrast: false);
                Assert.IsTrue(AppThemeCatalog.Contrast(((SolidColorBrush)theme["AccentColor"]).Color,
                    ((SolidColorBrush)theme["DisclosureActiveGlyphColor"]).Color) >= 3,
                    "The mock's preferred icon colour must remain readable.");
                var panel = new StackPanel { Margin = new Thickness(16, 10, 16, 10) };
                var row = new UniformGrid { Columns = 6, Margin = new Thickness(0, 10, 0, 0) };
                var label = new TextBlock { Text = choice.SourceName };
                label.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundColor"); panel.Children.Add(label);
                foreach (string state in new[] { "Rest", "Hover", "Pressed", "Focus", "Expanded", "Disabled" })
                {
                    var cell = new StackPanel();
                    var caption = new TextBlock { Text = state, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
                    caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundColor"); cell.Children.Add(caption);
                    var frame = new Grid { Width = 40, Height = 40 };
                    var toggle = new ToggleButton { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    toggle.Resources.MergedDictionaries.Add(theme);
                    toggle.SetResourceReference(FrameworkElement.StyleProperty, "AppCircleToggleStyle");
                    frame.Children.Add(toggle); cell.Children.Add(frame); row.Children.Add(cell);
                    ThemeConsistencyValidation.Layout(frame, 40, 40);
                    toggle.IsChecked = state == "Expanded";
                    Assert.IsTrue(VisualStateManager.GoToState(toggle, state == "Expanded" ? "Checked" : "Unchecked", false));
                    if (state == "Disabled") toggle.IsEnabled = false;
                    SetMouseOver(toggle, state is "Hover" or "Pressed");
                    typeof(ButtonBase).GetProperty("IsPressed").SetValue(toggle, state == "Pressed");
                    Assert.IsTrue(VisualStateManager.GoToState(toggle, state is "Hover" ? "MouseOver" : state is "Pressed" ? "Pressed" : state is "Disabled" ? "Disabled" : "Normal", false));
                    if (state == "Focus") frame.Children.Add(new Control { Width = 28, Height = 28, Style = toggle.FocusVisualStyle, IsHitTestVisible = false });
                    Pump(160);
                    ThemeConsistencyValidation.Layout(frame, 40, 40);
                    var circle = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("Circle", toggle);
                    var arrow = (System.Windows.Shapes.Path)toggle.Template.FindName("Arrow", toggle);
                    Point circleCentre = circle.TranslatePoint(new Point(circle.ActualWidth / 2, circle.ActualHeight / 2), frame);
                    Point buttonCentre = toggle.TranslatePoint(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2), frame);
                    Assert.AreEqual(buttonCentre.X, circleCentre.X, .01, "Native theme padding must not offset or clip the circle.");
                    Assert.AreEqual(buttonCentre.Y, circleCentre.Y, .01, "Native theme padding must not offset or clip the circle.");
                    Rect bounds = arrow.Data.Bounds;
                    Point arrowCentre = arrow.TranslatePoint(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), frame);
                    Assert.AreEqual(circleCentre.X, arrowCentre.X, .01, choice.SourceName + " " + state + " horizontal centre");
                    Assert.AreEqual(circleCentre.Y, arrowCentre.Y, .01, choice.SourceName + " " + state + " vertical centre");
                    var active = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("ActiveCircle", toggle);
                    Assert.AreEqual(state is "Hover" or "Pressed" ? 1d : 0d, active.Opacity, choice.SourceName + " " + state);
                    if (choice.Value != (int)AppThemeChoice.HighContrast)
                        Assert.AreEqual((byte)0, ((SolidColorBrush)circle.Stroke).Color.A,
                            "Non-contrast themes must match the mock's borderless circle.");
                }
                panel.Children.Add(row);
                var surface = new Border { Child = panel }; surface.Resources.MergedDictionaries.Add(theme);
                surface.SetResourceReference(Border.BackgroundProperty, "CardBackgroundColor"); gallery.Children.Add(surface);
            }
            ThemeConsistencyValidation.Layout(gallery, 720, 660);
            var bitmap = new RenderTargetBitmap(720, 660, 96, 96, PixelFormats.Pbgra32); bitmap.Render(gallery);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path); encoder.Save(stream);
        });
        TestContext.AddResultFile(path);
    }

    [TestMethod]
    public void HoverAndRotationAnimateWithoutDelayingTheCheckedValue()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var toggle = new ToggleButton { Width = 28, Height = 28 };
            using var source = new HwndSource(new HwndSourceParameters("DS4Windows disclosure timing")
                { Width = 80, Height = 80, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = toggle;
            toggle.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(AppThemeChoice.Dark, highContrast: false));
            toggle.SetResourceReference(FrameworkElement.StyleProperty, "AppCircleToggleStyle");
            ThemeConsistencyValidation.Layout(toggle, 28, 28);
            Assert.IsTrue(VisualStateManager.GoToState(toggle, "Normal", false));
            Assert.IsTrue(VisualStateManager.GoToState(toggle, "Unchecked", false));
            Pump(160);
            var active = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("ActiveCircle", toggle);
            var glyph = (Canvas)toggle.Template.FindName("GlyphFrame", toggle);
            var rotation = (RotateTransform)glyph.RenderTransform;
            SetMouseOver(toggle, true);
            Assert.IsTrue(VisualStateManager.GoToState(toggle, "MouseOver", true));
            if (SystemParameters.ClientAreaAnimation)
            {
                var watch = Stopwatch.StartNew(); Pump(45);
                if (watch.ElapsedMilliseconds < 90)
                    Assert.IsTrue(active.Opacity > 0 && active.Opacity < 1, "Hover must fade through intermediate colours; actual opacity=" + active.Opacity);
            }
            Pump(140); Assert.AreEqual(1d, active.Opacity, .01);
            toggle.IsChecked = true;
            Assert.IsTrue(toggle.IsChecked.Value, "Expansion state must change immediately.");
            if (SystemParameters.ClientAreaAnimation)
            {
                var watch = Stopwatch.StartNew(); Pump(45);
                if (watch.ElapsedMilliseconds < 110)
                    Assert.IsTrue(rotation.Angle > 0 && rotation.Angle < 180, "The chevron must rotate, rather than jump to its final orientation.");
            }
            Pump(160); Assert.AreEqual(180d, rotation.Angle, .01);
            var groups = VisualStateManager.GetVisualStateGroups((FrameworkElement)VisualTreeHelper.GetChild(toggle, 0));
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), groups.Cast<VisualStateGroup>().Single(g => g.Name == "CommonStates").Transitions.Cast<VisualTransition>().Single().GeneratedDuration.TimeSpan);
            Assert.AreEqual(TimeSpan.FromMilliseconds(120), groups.Cast<VisualStateGroup>().Single(g => g.Name == "CheckStates").Transitions.Cast<VisualTransition>().Single().GeneratedDuration.TimeSpan);
            toggle.Resources.MergedDictionaries[0] = AppThemeCatalog.Create(AppThemeChoice.Graphite, "#2DD4BF", highContrast: false);
            ThemeConsistencyValidation.Layout(toggle, 28, 28); Pump(160);
            // Re-templating a hidden HWND re-evaluates native hit testing. Restore
            // the synthetic pointer over the newly created template.
            SetMouseOver(toggle, true);
            Assert.IsTrue(VisualStateManager.GoToState(toggle, "MouseOver", false)); Pump(160);
            var refreshed = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("ActiveCircle", toggle);
            Assert.AreEqual(((SolidColorBrush)toggle.FindResource("AccentColor")).Color, ((SolidColorBrush)refreshed.Fill).Color);
            Assert.AreEqual(1d, refreshed.Opacity, .01, "A live theme/accent change must retain the hover state.");
            Assert.AreEqual(180d, ((RotateTransform)((Canvas)toggle.Template.FindName("GlyphFrame", toggle)).RenderTransform).Angle, .01);
        });
    }

    private static void SetMouseOver(ToggleButton toggle, bool value)
    {
        var key = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        toggle.SetValue(key, value);
    }

    internal static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
