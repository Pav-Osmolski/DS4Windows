using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DS4Windows;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;
using DS4WinWPF.DS4Forms.ViewModels;
using System.Windows.Data;

namespace DS4WindowsTests;

internal static class AppThemeValidation
{
    internal static void Validate(Application application, string directory, List<string> rendered)
    {
        var original = application.Resources.MergedDictionaries.ToArray();
        var culture = CultureInfo.CurrentUICulture;
        var localizer = WPFLocalizeExtension.Engine.LocalizeDictionary.Instance;
        var oldLocalizer = localizer.Culture;
        var tiles = new Grid { Width = 1440, Height = 920 };
        for (int i = 0; i < 3; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 2; i++) tiles.RowDefinitions.Add(new RowDefinition());
        try
        {
            var contrast = AppThemeCatalog.Create(AppThemeChoice.Paper, "#FF00FF", highContrast: true);
            Assert.AreEqual(SystemColors.WindowColor, ((SolidColorBrush)contrast["SurfaceBackgroundColor"]).Color);
            Assert.AreEqual(SystemColors.WindowTextColor, ((SolidColorBrush)contrast["ForegroundColor"]).Color);
            Assert.AreEqual(SystemColors.WindowTextColor, ((SolidColorBrush)contrast["AccentColor"]).Color);
            Assert.AreEqual(SystemColors.WindowColor, ((SolidColorBrush)contrast["AccentForegroundColor"]).Color);
            string savedAccent = Global.AppAccent;
            try
            {
                var settings = new SettingsViewModel(() => new(false, false), _ => { }, () => { }, _ => { });
                var selector = new ComboBox { DataContext = settings, SelectedValuePath = "Value" };
                selector.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(settings.AppAccentChoices)));
                selector.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
                    new Binding(nameof(settings.AppAccent)) { Mode = BindingMode.TwoWay });
                ThemeConsistencyValidation.Layout(selector, 280, 48);
                foreach (string colour in new[] { "#2DD4BF", "Windows", "#ABC123", "#654321", "Theme" })
                {
                    settings.AppAccent = colour;
                    selector.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                    Assert.AreEqual(colour, selector.SelectedValue);
                    Assert.AreEqual(colour, Global.AppAccent, "Updating the accent menu must not reset its selection.");
                }
                selector.SelectedValue = "#2C7DF7";
                Assert.AreEqual("#2C7DF7", Global.AppAccent);
            }
            finally { Global.AppAccent = savedAccent; }
            int index = 0;
            foreach (var choice in AppThemeCatalog.Choices.Where(item => item.Value > 0))
            {
                var dictionary = AppThemeCatalog.Create((AppThemeChoice)choice.Value, highContrast: false);
                foreach (string accent in new[] { "Theme", "Windows", "#FFFFFF", "#000000", "#FF00FF", "#2DD4BF" })
                {
                    var selected = AppThemeCatalog.Create((AppThemeChoice)choice.Value, accent, highContrast: false);
                    Color Colour(string key) => ((SolidColorBrush)selected[key]).Color;
                    foreach (string surface in new[] { "SurfaceBackgroundColor", "CardBackgroundColor", "RaisedBackgroundColor" })
                    foreach (string text in new[] { "ForegroundColor", "MutedForegroundColor", "AccentColor", "SuccessColor", "DangerColor" })
                        Assert.IsTrue(AppThemeCatalog.Contrast(Colour(surface), Colour(text)) >= 4.5,
                            $"{choice.SourceName}, {accent}, {text} on {surface}");
                    Assert.IsTrue(AppThemeCatalog.Contrast(Colour("AccentColor"), Colour("AccentForegroundColor")) >= 4.5);
                }
                var tile = new Border { Padding = new Thickness(20), Margin = new Thickness(2) };
                tile.Resources.MergedDictionaries.Add(dictionary);
                tile.Resources.MergedDictionaries.Add(Bridge());
                tile.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundColor");
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = choice.SourceName, FontSize = 24, Margin = new Thickness(0, 0, 0, 18) });
                content.Children.Add(new TextBlock { Text = "Controller preferences", Margin = new Thickness(0, 0, 0, 10) });
                var combo = new ComboBox { ItemsSource = new[] { "DualSense", "Xbox One / Series" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 14) };
                content.Children.Add(combo);
                content.Children.Add(new CheckBox { Content = "Enable battery alerts", IsChecked = true, Margin = new Thickness(0, 0, 0, 14) });
                content.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = 64, Margin = new Thickness(0, 0, 0, 14) });
                content.Children.Add(new TextBox { Text = "Windows Control", Margin = new Thickness(0, 0, 0, 14) });
                var save = new Button { Content = "Save profile", HorizontalAlignment = HorizontalAlignment.Left };
                save.SetResourceReference(FrameworkElement.StyleProperty, "BridgePrimaryButtonStyle");
                content.Children.Add(save);
                var muted = new TextBlock { Text = "Changes apply immediately.", Margin = new Thickness(0, 16, 0, 0) };
                muted.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundColor");
                content.Children.Add(muted);
                tile.Child = content;
                Grid.SetRow(tile, index / 3); Grid.SetColumn(tile, index % 3); tiles.Children.Add(tile); index++;
            }
            ThemeConsistencyValidation.Layout(tiles, 1440, 920);
            Save(tiles, "themes-gallery", 1440, 920);
            foreach (string name in new[] { "pl", "ar", "ja" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                localizer.Culture = CultureInfo.CurrentUICulture;
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(AppThemeChoice.Graphite, highContrast: false));
                application.Resources.MergedDictionaries.Add(Bridge());
                var control = new BatteryAlertsControl(false, new BatteryAlertSettings());
                var host = new Border { Padding = new Thickness(20), Child = control };
                host.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundColor");
                ThemeConsistencyValidation.Layout(host, 740, 1600);
                Assert.IsTrue(ThemeConsistencyValidation.Visuals(control).OfType<TextBlock>().Any(text =>
                    !string.IsNullOrWhiteSpace(text.Text) && text.Text.Any(c => c > 127)), name + " must render localized text.");
                Save(host, "language-" + name, 740, 1600);
            }

            void Save(FrameworkElement element, string name, int width, int height)
            {
                string path = Path.Combine(directory, name + ".png");
                ThemeConsistencyValidation.Save(element, path, width, height); rendered.Add(path);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture; localizer.Culture = oldLocalizer;
            application.Resources.MergedDictionaries.Clear();
            foreach (var resource in original) application.Resources.MergedDictionaries.Add(resource);
        }
    }

    private static ResourceDictionary Bridge() => new() { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) };
}
