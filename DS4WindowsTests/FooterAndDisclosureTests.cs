using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS4Windows;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class FooterAndDisclosureTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void ExpectedHapticsWarmupKeepsFooterRunningAndPersistentFailureClearsOnRecovery()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var footer = new AppFooterControl();
            var policy = new UsbHapticsReadinessPolicy();
            footer.UpdateService(true);
            var pending = new AudioEndpointPendingException("The matching VIIPER four-channel audio endpoint is not ready.");
            var notice = policy.Observe(pending, TimeSpan.Zero).Value;
            footer.UpdateActivity(notice.Reason, notice.Warning, DateTime.Now);
            Assert.AreEqual(UiText.Get("Running"), ((TextBlock)footer.FindName("serviceStatus")).Text);
            notice = policy.Observe(pending, UsbHapticsReadinessPolicy.ReadinessGrace).Value;
            footer.UpdateActivity(notice.Reason, notice.Warning, DateTime.Now);
            Assert.AreEqual(UiText.Get("Needs attention"), ((TextBlock)footer.FindName("serviceStatus")).Text);
            policy.Ready();
            footer.UpdateActivity("DualSense USB native haptics started.", false, DateTime.Now);
            Assert.AreEqual(UiText.Get("Running"), ((TextBlock)footer.FindName("serviceStatus")).Text);
            footer.UpdateService(false);
            footer.UpdateService(true);
            notice = policy.Observe(pending, TimeSpan.FromSeconds(60)).Value;
            footer.UpdateActivity(notice.Reason, notice.Warning, DateTime.Now);
            Assert.AreEqual(UiText.Get("Running"), ((TextBlock)footer.FindName("serviceStatus")).Text,
                "Stop/Start must get a fresh readiness grace period.");
        });
    }

    [TestMethod]
    public void FooterKeeps38DipHeightAndExposesFullMessagesAndWorkingActionsAcrossThemes()
    {
        string imagePath = Path.Combine(TestContext.TestResultsDirectory, "footer-themes.png");
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var gallery = new StackPanel { Width = 940 };
            foreach (var choice in AppThemeCatalog.Choices.Where(c => c.Value > 0))
            {
                var footer = new AppFooterControl();
                var host = new Border { Child = footer };
                var theme = AppThemeCatalog.Create((AppThemeChoice)choice.Value, highContrast: false);
                host.Resources.MergedDictionaries.Add(theme);
                void Layout()
                {
                    host.Measure(new Size(940, double.PositiveInfinity));
                    host.Arrange(new Rect(0, 0, 940, host.DesiredSize.Height)); host.UpdateLayout();
                }
                footer.UpdateService(true); footer.UpdateControllerCount(2);
                Layout();
                Assert.AreEqual(38d, footer.ActualHeight, .01, choice.SourceName);
                Assert.AreEqual(UiText.Get("Running"), ((TextBlock)footer.FindName("serviceStatus")).Text);
                Assert.AreEqual(DS4WinWPF.Translations.Strings.StopText, footer.ServiceButton.Content);
                string longMessage = "Controller output failed: " + new string('x', 1500);
                footer.UpdateActivity(longMessage, true, new DateTime(2026, 10, 27, 10, 42, 16));
                Layout();
                Assert.AreEqual(38d, footer.ActualHeight, .01, "Long activity must not grow the collapsed footer.");
                Assert.AreEqual(UiText.Get("Needs attention"), ((TextBlock)footer.FindName("serviceStatus")).Text);
                Assert.AreEqual(((SolidColorBrush)theme["DangerColor"]).Color,
                    ((SolidColorBrush)((TextBlock)footer.FindName("controllerSummary")).Foreground).Color);
                Assert.AreEqual(longMessage, ((TextBox)footer.FindName("activityMessage")).Text);
                foreach (string name in new[] { "hotkeysButton", "aboutButton", "serviceButton" })
                {
                    var button = (Button)footer.FindName(name);
                    Assert.AreEqual(28d, button.ActualHeight);
                    var bounds = button.TransformToAncestor(footer).TransformBounds(new Rect(button.RenderSize));
                    Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= footer.ActualWidth, name + " must remain within the footer.");
                }
                footer.IsActivityExpanded = true; Layout();
                var bar = (Border)footer.FindName("footerBar");
                Assert.AreEqual(38d, bar.ActualHeight);
                Assert.IsTrue(bar.TranslatePoint(new Point(), footer).Y > 0, "Details must open above the fixed-height bar.");
                Assert.IsTrue(footer.ActualHeight <= 240, "Verbose messages need a bounded reading area.");
                footer.IsActivityExpanded = false; Layout();
                Assert.AreEqual(38d, footer.ActualHeight);
                int hotkeys = 0, about = 0, service = 0, log = 0;
                footer.HotkeysRequested += (_, _) => hotkeys++;
                footer.AboutRequested += (_, _) => about++;
                footer.ServiceToggleRequested += (_, _) => service++;
                footer.LogRequested += (_, _) => log++;
                foreach (string name in new[] { "hotkeysButton", "aboutButton", "serviceButton" })
                    ((Button)footer.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, hotkeys); Assert.AreEqual(1, about); Assert.AreEqual(1, service);
                var openLog = ThemeConsistencyValidation.Visuals(footer).OfType<Button>()
                    .First(button => Equals(button.Content, UiText.Get("Open log")));
                openLog.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.AreEqual(1, log);
                footer.UpdateActivity("DualSense USB native haptics started for controller 1.", false, DateTime.Now);
                footer.UpdateService(false); Layout();
                Assert.AreEqual(UiText.Get("Stopped"), ((TextBlock)footer.FindName("serviceStatus")).Text);
                footer.UpdateService(true);
                host.FlowDirection = FlowDirection.RightToLeft; Layout();
                Assert.AreEqual(38d, footer.ActualHeight);
                host.FlowDirection = FlowDirection.LeftToRight;
                var label = new TextBlock { Text = choice.SourceName, Margin = new Thickness(18, 12, 0, 6) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundColor");
                var row = new StackPanel(); row.Resources.MergedDictionaries.Add(theme);
                row.SetResourceReference(Panel.BackgroundProperty, "CardBackgroundColor");
                row.Children.Add(label); row.Children.Add(host); gallery.Children.Add(row);
            }
            ThemeConsistencyValidation.Layout(gallery, 940, 420);
            var bitmap = new RenderTargetBitmap(940, 420, 96, 96, PixelFormats.Pbgra32); bitmap.Render(gallery);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(imagePath); encoder.Save(stream);
        });
        TestContext.AddResultFile(imagePath);
    }

    [TestMethod]
    public void CircularExpandersKeepPositionAndKeyboardAccessInEveryTheme()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var choice in AppThemeCatalog.Choices.Where(c => c.Value > 0))
            {
                var expander = new Expander { Header = "Controller settings", Content = new TextBlock { Text = "Settings" } };
                expander.Resources.MergedDictionaries.Add(AppThemeCatalog.Create((AppThemeChoice)choice.Value, highContrast: false));
                ThemeConsistencyValidation.Layout(expander, 360, 90);
                var toggle = (ToggleButton)expander.Template.FindName("HeaderSite", expander);
                Assert.IsTrue(toggle.Focusable && toggle.IsTabStop, "The expander must remain keyboard accessible.");
                Assert.IsNotNull(toggle.FocusVisualStyle);
                var circle = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("Circle", toggle);
                var before = circle.TranslatePoint(new Point(), expander);
                var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                toggle.SetValue(hoverKey, true);
                Assert.IsTrue(VisualStateManager.GoToState(toggle, "MouseOver", false));
                DisclosureAnimationTests.Pump(160);
                var activeCircle = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("ActiveCircle", toggle);
                Assert.AreEqual(((SolidColorBrush)expander.FindResource("AccentColor")).Color,
                    ((SolidColorBrush)activeCircle.Fill).Color);
                Assert.AreEqual(1d, activeCircle.Opacity);
                var headerSurface = (Border)toggle.Template.FindName("HeaderSurface", toggle);
                Assert.AreEqual((byte)0, ((SolidColorBrush)headerSurface.Background).Color.A,
                    "Ordinary expanders must highlight only the circle, not the full header.");
                var headerBitmap = new RenderTargetBitmap(360, 90, 96, 96, PixelFormats.Pbgra32);
                headerBitmap.Render(expander);
                byte[] headerPixel = new byte[4];
                headerBitmap.CopyPixels(new Int32Rect(340, 14, 1, 1), headerPixel, 4, 0);
                Assert.AreEqual((byte)0, headerPixel[3], "The far end of an ordinary header must remain unpainted on hover.");
                var arrow = (System.Windows.Shapes.Path)toggle.Template.FindName("ActiveArrow", toggle);
                Assert.AreEqual(((SolidColorBrush)expander.FindResource("DisclosureActiveGlyphColor")).Color,
                    ((SolidColorBrush)arrow.Stroke).Color);
                typeof(ButtonBase).GetProperty("IsPressed").SetValue(toggle, true);
                Assert.IsTrue(VisualStateManager.GoToState(toggle, "Pressed", false));
                DisclosureAnimationTests.Pump(160);
                Assert.AreEqual(Visibility.Visible,
                    ((System.Windows.Shapes.Ellipse)toggle.Template.FindName("PressRing", toggle)).Visibility);
                typeof(ButtonBase).GetProperty("IsPressed").SetValue(toggle, false);
                toggle.SetValue(hoverKey, false);
                toggle.IsChecked = true;
                ThemeConsistencyValidation.Layout(expander, 360, 90);
                Assert.IsTrue(expander.IsExpanded);
                Assert.AreEqual(before, circle.TranslatePoint(new Point(), expander));
                var body = (FrameworkElement)expander.Content;
                double headerBottom = toggle.TranslatePoint(new Point(0, toggle.ActualHeight), expander).Y;
                Assert.AreEqual(6d, body.TranslatePoint(new Point(), expander).Y - headerBottom, .01,
                    "Expanded content needs a minimal, consistent gap below the indicator.");
                expander.IsExpanded = false;
                Assert.IsFalse(toggle.IsChecked.Value);
                expander.IsEnabled = false;
                Assert.IsFalse(toggle.IsEnabled);
                Assert.AreEqual(.45, toggle.Opacity);
            }
        });
    }
}
