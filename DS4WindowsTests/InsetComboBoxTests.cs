using System.Reflection;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS4Windows;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class InsetComboBoxTests
{
    public TestContext TestContext { get; set; }
    [TestMethod]
    public void InsetDropdownPreservesGeometryAndRendersAllThemesAndStates()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var choice in new[] { AppThemeChoice.Light, AppThemeChoice.Dark, AppThemeChoice.Graphite,
                AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast })
            {
                var host = new StackPanel { Width = 440 };
                var theme = AppThemeCatalog.Create(choice, highContrast: false);
                host.Resources.MergedDictionaries.Add(theme);
                var shell = new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) };
                host.Resources.MergedDictionaries.Add(shell);
                var surface = new Border { Child = host, Padding = new Thickness(18) };
                surface.SetResourceReference(Border.BackgroundProperty, "BackgroundColor");
                // Resources must also resolve from the containing surface.
                surface.Resources = host.Resources;
                using var source = new HwndSource(new HwndSourceParameters("DS4Windows dropdown render") { Width = 476, Height = 850, WindowStyle = unchecked((int)0x80000000) });
                source.RootVisual = surface;
                var states = new[] { "Normal", "Hover", "Arrow hover", "Pressed", "Open", "Keyboard focus", "Disabled", "Editable", "Profile" };
                var combos = new List<ComboBox>();
                foreach (string state in states)
                {
                    host.Children.Add(new TextBlock { Text = state, Foreground = (Brush)theme["MutedForegroundColor"], Margin = new Thickness(0, 8, 0, 4) });
                    var combo = new ComboBox { Width = 420, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left };
                    combo.Items.Add("Windows Control"); combo.Items.Add("Default"); combo.SelectedIndex = 0;
                    host.Children.Add(combo); combos.Add(combo);
                    if (state == "Editable") combo.IsEditable = true;
                    if (state == "Profile") { combo.Height = 42; InsetComboBox.SetShowControllerIcon(combo, true); }
                    if (state == "Disabled") combo.IsEnabled = false;
                }
                surface.Measure(new Size(476, double.PositiveInfinity));
                surface.Arrange(new Rect(0, 0, 476, surface.DesiredSize.Height)); surface.UpdateLayout();
                for (int i = 0; i < combos.Count; i++)
                {
                    var combo = combos[i];
                    Assert.IsTrue(combo.ActualHeight >= 32, choice + ": minimum field height");
                    var root = (FrameworkElement)combo.Template.FindName("InsetRoot", combo);
                    Assert.IsNotNull(root, choice.ToString());
                    var well = (Border)combo.Template.FindName("ArrowWell", combo);
                    Assert.AreEqual(27d, well.ActualWidth);
                    Assert.AreEqual(new Thickness(0, 4, 4, 4), well.Margin);
                    var bounds = well.TransformToAncestor(combo).TransformBounds(new Rect(well.RenderSize));
                    Assert.AreEqual(combo.ActualHeight / 2, bounds.Top + bounds.Height / 2, .01);
                    string state = states[i];
                    if (state is "Hover" or "Arrow hover" or "Pressed") VisualStateManager.GoToState(combo, "Hovered", false);
                    if (state == "Arrow hover") VisualStateManager.GoToState(combo, "ArrowHovered", false);
                    if (state == "Pressed") VisualStateManager.GoToState(combo, "Pressed", false);
                    if (state == "Open") VisualStateManager.GoToState(combo, "Opened", false);
                    if (state == "Keyboard focus") VisualStateManager.GoToState(combo, "Focused", false);
                    DisclosureAnimationTests.Pump(30);
                    double height = combo.ActualHeight;
                    surface.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
                    surface.UpdateLayout();
                    Assert.AreEqual(height, combo.ActualHeight, .01, "Paint states must not change layout.");
                    var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
                    Assert.AreEqual(4d, popup.VerticalOffset);
                    if (state == "Editable")
                        Assert.AreEqual(Visibility.Visible, ((TextBox)combo.Template.FindName("PART_EditableTextBox", combo)).Visibility);
                    Assert.IsNull(combo.Template.FindName("FocusLine", combo), "Dropdown focus must use the outer outline, without an underline.");
                    if (state == "Keyboard focus")
                        Assert.AreEqual(1d, ((Border)combo.Template.FindName("FocusBorder", combo)).Opacity,
                            "Removing the underline must retain the outer keyboard focus indicator.");
                    if (state == "Profile")
                        Assert.AreEqual(Visibility.Visible, ((FrameworkElement)combo.Template.FindName("ControllerIcon", combo)).Visibility);
                    if (state == "Open")
                        Assert.AreEqual(180d, ((RotateTransform)((FrameworkElement)combo.Template.FindName("OpenArrow", combo)).Parent.GetValue(UIElement.RenderTransformProperty)).Angle);
                }
                string directory = Environment.GetEnvironmentVariable("DS4W_DROPDOWN_ARTIFACTS") ?? TestContext.TestResultsDirectory;
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "inset-dropdown-" + choice + ".png");
                var bitmap = new RenderTargetBitmap(476, (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(path)) encoder.Save(file);
                TestContext.AddResultFile(path);
            }
        });
    }
    private static double CurrentAngle(ComboBox combo) => ((RotateTransform)((FrameworkElement)combo.Template.FindName("OpenArrow", combo)).Parent.GetValue(UIElement.RenderTransformProperty)).Angle;

    [TestMethod]
    public void InsetDropdownFadesAndRotatesAndKeepsBindingsAndLiveThemeColours()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var combo = new ComboBox { Width = 280, Height = 42, FontSize = 14 };
            var host = new Border { Child = combo };
            host.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(AppThemeChoice.Dark, highContrast: false));
            using var source = new HwndSource(new HwndSourceParameters("DS4Windows inset timing")
                { Width = 280, Height = 42, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = host;
            combo.Items.Add("Windows Control"); combo.Items.Add("Default"); combo.SelectedIndex = 0;
            ThemeConsistencyValidation.Layout(host, 280, 42);
            InsetComboBox.Update(combo, false); DisclosureAnimationTests.Pump(30);
            var key = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            combo.SetValue(key, true); InsetComboBox.Update(combo);
            Assert.IsTrue(VisualStateManager.GoToState(combo, "Hovered", true));
            var hover = (Border)combo.Template.FindName("HoverSurface", combo);
            if (SystemParameters.ClientAreaAnimation)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew(); DisclosureAnimationTests.Pump(40);
                if (watch.ElapsedMilliseconds < 90) Assert.IsTrue(hover.Opacity > 0 && hover.Opacity < 1, "Hover must fade through intermediate colours; opacity=" + hover.Opacity + ", pointer=" + combo.IsMouseOver);
            }
            DisclosureAnimationTests.Pump(140); Assert.AreEqual(1d, hover.Opacity, .01);
            // A hidden HWND cannot retain native popup mouse capture. Test the
            // paint clock independently without showing a popup on the desktop.
            InsetComboBox.SetEnabled(combo, false);
            Assert.IsTrue(VisualStateManager.GoToState(combo, "Closed", false)); DisclosureAnimationTests.Pump(160);
            Assert.IsTrue(VisualStateManager.GoToState(combo, "Opened", true));
            var arrow = (RotateTransform)((FrameworkElement)combo.Template.FindName("OpenArrow", combo)).Parent.GetValue(UIElement.RenderTransformProperty);
            if (SystemParameters.ClientAreaAnimation)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew(); DisclosureAnimationTests.Pump(40);
                if (watch.ElapsedMilliseconds < 100) Assert.IsTrue(CurrentAngle(combo) > 0 && CurrentAngle(combo) < 180, "The chevron must rotate smoothly; angle=" + CurrentAngle(combo));
            }
            DisclosureAnimationTests.Pump(160); Assert.AreEqual(180d, CurrentAngle(combo), .01);
            Assert.AreEqual(42d, combo.ActualHeight);
            host.Resources.MergedDictionaries[0] = AppThemeCatalog.Create(AppThemeChoice.Paper, "#2DD4BF", highContrast: false);
            ThemeConsistencyValidation.Layout(host, 280, 42); DisclosureAnimationTests.Pump(30);
            Assert.AreEqual(((SolidColorBrush)combo.FindResource("RaisedBackgroundColor")).Color, ((SolidColorBrush)combo.Background).Color);
            combo.IsEditable = true;
            combo.Text = "A custom profile"; ThemeConsistencyValidation.Layout(host, 280, 42);
            var editor = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
            Assert.AreEqual(combo.Text, editor.Text);
            editor.Text = "Changed by typing";
            Assert.AreEqual("Changed by typing", combo.Text);
            combo.FlowDirection = FlowDirection.RightToLeft; ThemeConsistencyValidation.Layout(host, 280, 42);
            var well = (Border)combo.Template.FindName("ArrowWell", combo);
            Assert.IsTrue(well.TransformToAncestor(host).TransformBounds(new Rect(well.RenderSize)).Left < 40,
                "RTL layouts must place the inset arrow at the leading edge.");
        });
    }

    [TestMethod]
    public void DropdownOptionsRetainSelectedHoverPressedAndDisabledFeedback()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var theme in new[] { AppThemeChoice.Light, AppThemeChoice.Dark, AppThemeChoice.Graphite, AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast })
            {
                var host = new StackPanel { Width = 320 };
                host.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, highContrast: false));
                using var source = new HwndSource(new HwndSourceParameters("DS4Windows dropdown options")
                    { Width = 320, Height = 140, WindowStyle = unchecked((int)0x80000000) });
                source.RootVisual = host;
                var selected = new ComboBoxItem { Content = "Windows Control", IsSelected = true };
                selected.SetResourceReference(FrameworkElement.StyleProperty, "AppInsetComboBoxItemStyle");
                host.Children.Add(selected);
                ThemeConsistencyValidation.Layout(host, 320, 140); DisclosureAnimationTests.Pump(30);
                Assert.AreEqual(Visibility.Visible, ((FrameworkElement)selected.Template.FindName("SelectedCheck", selected)).Visibility);
                var highlight = (DependencyPropertyKey)typeof(ComboBoxItem).GetField("IsHighlightedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                selected.SetValue(highlight, true);
                var mouse = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                selected.SetValue(mouse, true);
                InsetComboBox.SetEnabled(selected, false);
                Assert.IsTrue(VisualStateManager.GoToState(selected, "ItemHovered", false)); DisclosureAnimationTests.Pump(160);
                Assert.AreEqual(1d, ((Border)selected.Template.FindName("ItemHover", selected)).Opacity);
                InsetComboBox.SetEnabled(selected, true);
                selected.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                Assert.IsTrue(InsetComboBox.GetIsItemPressed(selected));
                Assert.AreEqual(((SolidColorBrush)selected.FindResource("AccentForegroundColor")).Color, ((SolidColorBrush)selected.Foreground).Color);
                selected.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                Assert.IsFalse(InsetComboBox.GetIsItemPressed(selected));
                selected.IsEnabled = false;
                Assert.AreEqual(.45, selected.Opacity);
            }
        });
    }

}
