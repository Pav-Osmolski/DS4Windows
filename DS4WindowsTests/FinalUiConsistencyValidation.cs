using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;
using Xceed.Wpf.Toolkit;

namespace DS4WindowsTests;

internal static class FinalUiConsistencyValidation
{
    internal static void Validate(Application app, string directory, List<string> images)
    {
        var original = app.Resources.MergedDictionaries.ToArray();
        object previousConverter = app.Resources["BooleanToVisibilityConverter"];
        try
        {
            foreach (var theme in new[] { AppThemeChoice.Light, AppThemeChoice.Dark, AppThemeChoice.Graphite,
                         AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast })
            {
                app.Resources.MergedDictionaries.Clear();
                app.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, highContrast: false));
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                var gallery = new StackPanel { Width = 940 };
                gallery.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackgroundColor");
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
                foreach (string key in new[] { "BridgePrimaryButtonStyle", "BridgeSecondaryButtonStyle" })
                {
                    var button = new Button { Content = key.Contains("Primary") ? "New profile" : "Open log folder", Margin = new Thickness(0, 0, 12, 0) };
                    button.SetResourceReference(FrameworkElement.StyleProperty, key); actions.Children.Add(button);
                    ThemeConsistencyValidation.Layout(actions, 900, 50);
                    var chrome = (Border)button.Template.FindName(key.Contains("Primary") ? "PrimaryChrome" : "Chrome", button);
                    Color before = ((SolidColorBrush)chrome.BorderBrush).Color;
                    var focus = (DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    button.SetValue(focus, true);
                    Assert.AreEqual(before, ((SolidColorBrush)chrome.BorderBrush).Color,
                        "Programmatic focus must not paint an initial action outline in " + theme);
                    Assert.IsNotNull(button.FocusVisualStyle, "Keyboard navigation must retain a focus cue.");
                    button.SetValue(focus, false);
                }
                gallery.Children.Add(actions);
                var fields = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
                foreach (Control number in new Control[] { new IntegerUpDown(), new DoubleUpDown(), new DecimalUpDown(), new SByteUpDown(), new UIntegerUpDown() })
                {
                    number.Width = 110; number.Height = 34; number.Margin = new Thickness(0, 0, 10, 0); fields.Children.Add(number);
                    foreach (bool enabled in new[] { true, false, true })
                    {
                        number.IsEnabled = enabled; ThemeConsistencyValidation.Layout(fields, 900, 40);
                        Color expected = ((SolidColorBrush)app.FindResource("BorderColor")).Color;
                        Assert.AreEqual(expected, ((SolidColorBrush)number.BorderBrush).Color, theme + " " + number.GetType().Name);
                        var spinner = ThemeConsistencyValidation.Visuals(number).OfType<ButtonSpinner>().First();
                        Assert.AreEqual(expected, ((SolidColorBrush)spinner.BorderBrush).Color, "The rendered spinner must inherit the numeric control's themed outline.");
                    }
                }
                gallery.Children.Add(fields);
                var tabs = new TabControl { Margin = new Thickness(12), Height = 70 };
                tabs.SetResourceReference(FrameworkElement.StyleProperty, "AppSegmentedTabsStyle");
                tabs.Items.Add(new TabItem { Header = "KB & Rel Mouse", Content = new TextBlock { Text = "Keyboard mapping" } });
                tabs.Items.Add(new TabItem { Header = "Abs Mouse", Content = new TextBlock { Text = "Absolute mouse mapping" } });
                gallery.Children.Add(tabs);
                foreach (int index in new[] { 0, 1, 0 })
                {
                    tabs.SelectedIndex = index; ThemeConsistencyValidation.Layout(tabs, 900, 70);
                    foreach (TabItem tab in tabs.Items)
                        Assert.AreEqual(((SolidColorBrush)app.FindResource("ForegroundColor")).Color, ((SolidColorBrush)tab.Foreground).Color,
                            "Selected and unselected mapping tabs must remain readable in " + theme);
                }
                var list = ControllerList(app); gallery.Children.Add(list);
                ThemeConsistencyValidation.Layout(gallery, 940, 560);
                var item = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsNotNull(item);
                var card = (Border)item.Template.FindName("Card", item);
                Rect bounds = card.TransformToAncestor(list).TransformBounds(new Rect(card.RenderSize));
                Assert.AreEqual(bounds.Left, bounds.Top, .01, "Top and side card insets must match in " + theme);
                Assert.AreEqual(bounds.Left, list.ActualWidth - bounds.Right, .01, "Left/right card insets must match in " + theme);
                var combo = ThemeConsistencyValidation.Visuals(card).OfType<ComboBox>().Single(c => c.Name == "selectProfCombo");
                var edit = ThemeConsistencyValidation.Visuals(card).OfType<SplitButton>().Single();
                var light = ThemeConsistencyValidation.Visuals(card).OfType<Button>().Single(b => b.Name == "LightColorBtn");
                foreach (FrameworkElement control in new FrameworkElement[] { edit, light })
                {
                    Assert.AreEqual(combo.ActualHeight, control.ActualHeight, .01);
                    Assert.AreEqual(combo.TranslatePoint(new Point(0, combo.ActualHeight / 2), card).Y,
                        control.TranslatePoint(new Point(0, control.ActualHeight / 2), card).Y, .01);
                }
                var link = ThemeConsistencyValidation.Visuals(card).OfType<CheckBox>().Single();
                Assert.AreEqual(combo.TranslatePoint(new Point(0, combo.ActualHeight / 2), card).Y,
                    link.TranslatePoint(new Point(0, link.ActualHeight / 2), card).Y, .01, "Link profile checkbox must be centred too.");
                Save(gallery, directory, "final-controls-" + theme + ".png", 940, 560, images);
                var touch = ProfileAdvancedLayoutTests.Load(theme is AppThemeChoice.Light or AppThemeChoice.Paper ? "DefaultTheme" : "DarkTheme", "Touchpad");
                ((TabControl)touch.FindName("touchOutputTabControl")).SelectedIndex = 0;
                ThemeConsistencyValidation.Layout(touch, 1000, 900);
                touch.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { })); touch.UpdateLayout();
                foreach (string[] group in new[] { new[] { "touchSlideUD", "touchTapUD", "touchScrollUD" }, new[] { "touchMouseRotationUD", "touchMouseThresholdUD", "frictionUD" } })
                {
                    var controls = group.Select(name => (FrameworkElement)touch.FindName(name)).ToArray();
                    Assert.IsTrue(controls.All(c => c != null && c.ActualWidth > 0), "Validate visible, arranged touchpad fields.");
                    double x = controls[0].TranslatePoint(new Point(), touch).X;
                    foreach (var control in controls) Assert.AreEqual(x, control.TranslatePoint(new Point(), touch).X, .01, "Touchpad field alignment: " + control.Name);
                }
                Save(touch, directory, "final-touchpad-" + theme + ".png", 1000, 900, images);
            }
        }
        finally { app.Resources.MergedDictionaries.Clear(); foreach (var resource in original) app.Resources.MergedDictionaries.Add(resource);
            if (previousConverter == null) app.Resources.Remove("BooleanToVisibilityConverter"); else app.Resources["BooleanToVisibilityConverter"] = previousConverter; }
    }

    private static ListView ControllerList(Application app)
    {
        var document = LocalizedSource.Load(Source("MainWindow.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var node = document.Descendants().Single(n => (string)n.Attribute(x + "Name") == "controllerLV");
        var markup = new XElement(node.Element(wpf + "ListView.ItemTemplate").Elements().Single());
        foreach (var declaration in document.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
            markup.SetAttributeValue(declaration.Name, declaration.Value.StartsWith("clr-namespace:") && !declaration.Value.Contains("assembly=") ? declaration.Value + ";assembly=DS4Windows" : declaration.Value);
        foreach (var declaration in markup.DescendantsAndSelf().Attributes().Where(a => a.IsNamespaceDeclaration).ToArray())
            if (declaration.Value.StartsWith("clr-namespace:") && !declaration.Value.Contains("assembly=")) declaration.Value += ";assembly=DS4Windows";
        foreach (var attribute in markup.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attribute.Name.NamespaceName.StartsWith("clr-namespace:") && !attribute.Name.NamespaceName.Contains("assembly="))
            {
                attribute.Parent.SetAttributeValue(XName.Get(attribute.Name.LocalName, attribute.Name.NamespaceName + ";assembly=DS4Windows"), attribute.Value);
                attribute.Remove(); continue;
            }
            if (new[] { "Click", "KeyDown", "MouseEnter", "MouseLeave", "SelectionChanged", "ToolTipOpening", "MouseRightButtonUp", "MouseLeftButtonUp", "MouseDoubleClick", "PreviewMouseDown", "MouseDown", "MouseUp" }.Contains(attribute.Name.LocalName)) attribute.Remove();
            else if (attribute.Value.StartsWith("{Binding ") && !attribute.Value.Contains(',')) attribute.Value = attribute.Value[..^1] + ", Mode=OneWay}";
        }
        var list = new ListView { Height = 280, Padding = new Thickness(8, 8, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        list.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        list.SetResourceReference(FrameworkElement.StyleProperty, "BridgeControllerCardsStyle");
        list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "BridgeControllerCardItemStyle");
        // Parse while application palette/style resources are in scope.
        app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        list.ItemTemplate = (DataTemplate)XamlReader.Parse(LocalizedSource.Markup(markup));
        ScrollViewer.SetCanContentScroll(list, true); VirtualizingPanel.SetIsVirtualizing(list, false);
        list.Items.Add(new { PrimaryDevice = true, CanLinkProfile = true, LinkedProfile = false, SelectedIndex = 0,
            IdText = "DualSense", ProfileListCol = new[] { new { Name = "Windows Control" } }, LightColor = Brushes.Purple,
            BatteryState = "100%", HasControllerArtwork = false, SupportsSwitch2StandaloneHoldMode = false, SupportsSwitch2Identification = false,
            JoyConLinkAction = new { Visible = false } });
        list.SelectedIndex = 0;
        return list;
    }
    private static void Save(FrameworkElement root, string directory, string name, int width, int height, List<string> images)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(directory, name); using (var stream = File.Create(path)) encoder.Save(stream); images.Add(path);
    }
    private static string Source(string name, [CallerFilePath] string file = "") => Path.Combine(Path.GetDirectoryName(file)!, "..", "DS4Windows", "DS4Forms", name);
}
