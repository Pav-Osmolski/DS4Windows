using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS4Windows;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

internal static class BatteryAlertsLayoutValidation
{
    internal static void Validate(Application application, string directory, List<string> rendered)
    {
        var previous = application.Resources.MergedDictionaries.ToArray();
        try
        {
            foreach (string theme in new[] { "DarkTheme", "DefaultTheme" })
            {
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative) });
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                var settings = new BatteryAlertSettings { LowEnabled = true, FullEnabled = true };
                settings.Low.Vibrate = true;
                settings.Low.SoundName = "Battery warning.wav";
                settings.Low.Mode = BatteryAlertMode.Both;
                var control = new BatteryAlertsControl(false, settings);
                ((ComboBox)control.FindName("voiceCombo")).ItemsSource = new[] { new BatteryAlertChoice("", "System default") };
                ((ComboBox)control.FindName("voiceCombo")).SelectedIndex = 0;
                ((ComboBox)control.FindName("outputCombo")).ItemsSource = new[] { new BatteryAlertChoice("", "Windows default audio device") };
                ((ComboBox)control.FindName("outputCombo")).SelectedIndex = 0;
                var configure = Logical(control).OfType<Expander>().Single(e => e.Header?.ToString() == "Configure voices, sounds and vibration");
                Assert.IsFalse(configure.IsExpanded, "Keep optional detailed settings compact until requested.");
                configure.IsExpanded = true;
                var card = new Border { Padding = new Thickness(20), Child = control };
                card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundColor");
                foreach (double width in new[] { 740d, 480d })
                {
                    card.Measure(new Size(width, double.PositiveInfinity));
                    card.Arrange(new Rect(0, 0, width, card.DesiredSize.Height)); card.UpdateLayout();
                    foreach (var button in Visuals(control).OfType<Button>().Where(b => b.IsVisible))
                    {
                        var bounds = button.TransformToAncestor(card).TransformBounds(new Rect(button.RenderSize));
                        Assert.IsTrue(bounds.Right <= width + .1, $"{theme}: {button.Content} overflows at width {width}.");
                        Assert.IsTrue(button.ActualHeight >= 30, $"{theme}: button height.");
                    }
                }
                card.Measure(new Size(740, double.PositiveInfinity));
                card.Arrange(new Rect(0, 0, 740, card.DesiredSize.Height)); card.UpdateLayout();
                int height = (int)Math.Ceiling(card.ActualHeight);
                var bitmap = new RenderTargetBitmap(740, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(card);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string filename = Path.Combine(directory, $"battery-alerts-{theme}.png");
                using (var stream = File.Create(filename)) encoder.Save(stream);
                rendered.Add(filename);
                ValidatePreviewScrollPosition(control, card, theme);
            }
        }
        finally
        {
            application.Resources.MergedDictionaries.Clear();
            foreach (var resource in previous) application.Resources.MergedDictionaries.Add(resource);
        }
    }
    private static void ValidatePreviewScrollPosition(BatteryAlertsControl control, Border card, string theme)
    {
        var previousHub = DS4Windows.Program.rootHub;
        var previousSettings = Global.BatteryAlerts;
        var hub = (ControlService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
        hub.DS4Controllers = new DS4Device[8];
        var settings = new BatteryAlertSettings();
        settings.Low.Mode = settings.Full.Mode = settings.Check.Mode = BatteryAlertMode.Off;
        var buttons = (WrapPanel)control.FindName("previewButtons");
        var status = (TextBlock)control.FindName("statusText");
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 600 });
        content.Children.Add(card);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        void Layout(double width)
        {
            scroll.Measure(new Size(width, 500));
            scroll.Arrange(new Rect(0, 0, width, 500)); scroll.UpdateLayout();
        }
        buttons.IsEnabledChanged += (_, _) => Assert.IsTrue(buttons.IsEnabled, "Preview must not disable the focused button and move keyboard focus.");
        try
        {
            DS4Windows.Program.rootHub = hub;
            Global.BatteryAlerts = settings; // No real audio, controller writes or settings saves.
            hub.BatteryAlerts.StatusChanged += message => status.Text = message;
            foreach (double width in new[] { 740d, 480d })
            {
                status.Text = "Automatic alerts are optional. Preview audio works without a connected controller; vibration requires one.";
                Layout(width); scroll.ScrollToBottom(); Layout(width);
                Assert.IsTrue(status.ActualHeight < status.FontSize * 2, "Status must occupy one line without reserved blank space.");
                double offset = scroll.VerticalOffset, extent = scroll.ExtentHeight;
                foreach (var button in buttons.Children.OfType<Button>())
                {
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                    Layout(width);
                    Assert.IsTrue(status.Text.StartsWith("Battery alert:"));
                    Assert.AreEqual(offset, scroll.VerticalOffset, .1, $"{theme}: {button.Content} moved the page.");
                    Assert.AreEqual(extent, scroll.ExtentHeight, .1, "Preview status must not resize the page.");
                }
                foreach (string message in new[] { "Preparing battery alert...", "Playing battery alert...", "Battery alert finished.",
                    "Battery alert: Enable 'Stream audio to controller' in Profile Editor > Advanced > Audio and turn off 'Headset only'." })
                {
                    status.Text = message; Layout(width);
                    Assert.AreEqual(message, status.ToolTip, "Keep the full message available when the status is trimmed.");
                    Assert.AreEqual(offset, scroll.VerticalOffset, .1, $"{theme}: preview progress moved the page.");
                    Assert.AreEqual(extent, scroll.ExtentHeight, .1, "Preview progress must not resize the page.");
                }
            }
        }
        finally
        {
            hub.BatteryAlerts.Stop();
            DS4Windows.Program.rootHub = previousHub;
            Global.BatteryAlerts = previousSettings;
        }
    }
    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject item) foreach (var nested in Logical(item)) yield return nested;
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
