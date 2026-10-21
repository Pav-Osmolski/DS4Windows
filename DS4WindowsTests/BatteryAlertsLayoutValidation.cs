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
            }
        }
        finally
        {
            application.Resources.MergedDictionaries.Clear();
            foreach (var resource in previous) application.Resources.MergedDictionaries.Add(resource);
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
