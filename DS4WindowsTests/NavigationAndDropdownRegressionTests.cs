using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class NavigationAndDropdownRegressionTests
{
    public TestContext TestContext { get; set; }
    private static readonly AppThemeChoice[] Themes = [AppThemeChoice.Light, AppThemeChoice.Dark,
        AppThemeChoice.Graphite, AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast];
    private static ResourceDictionary Shell() => new() { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) };
    private static Color Colour(object brush) => ((SolidColorBrush)brush).Color;

    [TestMethod]
    public void AllNavigationHelpBelongsOnlyToVisibleHeadersAndKeepsChildHelp()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var source = LocalizedSource.Load(Source("MainWindow.xaml"));
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var tabs = source.Descendants(wpf + "TabItem").Where(n => n.Attributes().Any(a => a.Name.LocalName == "AppToolTips.HeaderHelp")).ToArray();
            Assert.AreEqual(9, tabs.Length);
            var serviceType = typeof(FrameworkElement).Assembly.GetType("System.Windows.Controls.PopupControlService", true);
            var service = serviceType.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var find = serviceType.GetMethod("FindToolTipOwner", BindingFlags.Instance | BindingFlags.NonPublic);
            var action = Enum.Parse(find.GetParameters()[1].ParameterType, "Mouse");
            foreach (var theme in Themes)
            foreach (var node in tabs)
            {
                Assert.IsNull(node.Attribute("ToolTip"), "A navigation TabItem must not expose help to its whole logical page.");
                var page = new Grid { Background = Brushes.Transparent };
                var child = new Button { Content = "Specific action", ToolTip = "Specific help", Width = 140, Height = 36 };
                page.Children.Add(child);
                var item = new TabItem { Header = LocalizedSource.Resolve((string)node.Attribute("Header")), Content = page };
                AppToolTips.SetHeaderHelp(item, node.Attributes().Single(a => a.Name.LocalName == "AppToolTips.HeaderHelp").Value);
                var host = new TabControl();
                host.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, highContrast: false));
                host.Resources.MergedDictionaries.Add(Shell());
                host.SetResourceReference(FrameworkElement.StyleProperty, "BridgeMainTabControlStyle");
                host.Items.Add(item); host.SelectedIndex = 0;
                ThemeConsistencyValidation.Layout(host, 1180, 680);
                var header = (Border)item.Template.FindName("NavItem", item);
                Assert.IsNotNull(header.ToolTip);
                Assert.AreSame(header, find.Invoke(service, new object[] { header, action }), theme + " header help");
                Assert.IsNull(find.Invoke(service, new object[] { page, action }), theme + " empty page must not borrow navigation help");
                Assert.AreSame(child, find.Invoke(service, new object[] { child, action }), theme + " specific child help");
                // Look for the owner from the actual displayed button chrome too.
                Assert.AreSame(child, find.Invoke(service, new object[] { ThemeConsistencyValidation.Visuals(child).OfType<Border>().First(), action }));
            }
        });
    }

    [TestMethod]
    public void RetainedPrimaryActionsFollowEveryThemeAndMatchSpecialActions()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var palette = AppThemeCatalog.Create(AppThemeChoice.Light, highContrast: false);
            row.Resources.MergedDictionaries.Add(palette); row.Resources.MergedDictionaries.Add(Shell());
            var overview = new Button { Content = "Edit active profile", Margin = new Thickness(0, 0, 8, 0) };
            overview.Style = (Style)row.FindResource("BridgePrimaryButtonStyle"); // retain initial Light style
            var special = new Button { Content = "New Action" };
            special.SetResourceReference(FrameworkElement.StyleProperty, "BridgePrimaryButtonStyle");
            row.Children.Add(overview); row.Children.Add(special);
            var hover = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var press = (DependencyPropertyKey)typeof(ButtonBase).GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (var theme in Themes.Concat(Themes.Reverse()))
            {
                palette = AppThemeCatalog.Create(theme, highContrast: false);
                row.Resources.MergedDictionaries[0] = palette;
                foreach (string state in new[] { "Normal", "Hover", "Pressed", "Disabled" })
                {
                    foreach (var button in new[] { overview, special })
                    {
                        button.IsEnabled = state != "Disabled";
                        button.SetValue(hover, state is "Hover" or "Pressed"); button.SetValue(press, state == "Pressed");
                    }
                    ThemeConsistencyValidation.Layout(row, 600, 70);
                    var chrome = (Border)overview.Template.FindName("PrimaryChrome", overview);
                    var reference = (Border)special.Template.FindName("PrimaryChrome", special);
                    Assert.AreEqual(Colour(reference.Background), Colour(chrome.Background));
                    Assert.AreEqual(Colour(special.Foreground), Colour(overview.Foreground));
                    Assert.AreEqual(Colour(palette[state == "Hover" ? "NavigationSelectionColor" : "AccentColor"]), Colour(chrome.Background));
                    Assert.AreEqual(Colour(palette[state == "Hover" ? "ForegroundColor" : "AccentForegroundColor"]), Colour(overview.Foreground));
                    Assert.AreEqual(state == "Disabled" ? .5 : 1, overview.Opacity);
                    Assert.AreEqual(new Thickness(1), chrome.BorderThickness);
                    if (state == "Hover")
                    {
                        var bitmap = new RenderTargetBitmap(600, 70, 96, 96, PixelFormats.Pbgra32); bitmap.Render(row);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        string path = Path.Combine(TestContext.TestResultsDirectory, "primary-hover-" + theme + ".png");
                        using (var stream = File.Create(path)) encoder.Save(stream);
                        TestContext.AddResultFile(path);
                    }
                }
            }
        });
    }

    [TestMethod]
    public void DropdownPixelScrollingHasNoTrailingItemGapAndHoverCannotScroll()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var theme in Themes)
            foreach (double fontSize in new[] { 14d, 20d })
            {
                var combo = new ComboBox { Width = 280, Height = 36, MaxDropDownHeight = 173, FontSize = fontSize };
                combo.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, highContrast: false));
                // Only the popup scroller is needed from the shell. Avoid unrelated
                // implicit styles whose base resources are application-owned.
                combo.Resources["BridgeDropDownScrollViewerStyle"] = Shell()["BridgeDropDownScrollViewerStyle"];
                foreach (string name in new[] { "English", "العربية", "Čeština", "Deutsch", "Ελληνικά", "Español", "Suomi", "Français", "עברית", "Magyar", "Polski", "Português", "Русский", "Svenska", "Türkçe", "Українська", "Tiếng Việt", "中文（简体）", "中文（繁體）" }) combo.Items.Add(name);
                combo.SelectedIndex = 0;
                ThemeConsistencyValidation.Layout(combo, 280, 36);
                var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
                var content = (Border)popup.Child;
                ThemeConsistencyValidation.Layout(content, 280, 173);
                var scroll = ThemeConsistencyValidation.Visuals(content).OfType<ScrollViewer>().First();
                Assert.IsFalse(scroll.CanContentScroll, "Popup offsets must use pixels, not partially filled logical item pages.");
                scroll.ScrollToEnd(); DisclosureAnimationTests.Pump(30); content.UpdateLayout();
                Assert.IsTrue(scroll.ScrollableHeight > 0);
                Assert.AreEqual(scroll.ScrollableHeight, scroll.VerticalOffset, .01);
                var last = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(combo.Items.Count - 1);
                Assert.IsNotNull(last);
                var presenter = ThemeConsistencyValidation.Visuals(scroll).OfType<ScrollContentPresenter>().First();
                double gap = presenter.ActualHeight - last.TransformToAncestor(presenter).Transform(new Point(0, last.ActualHeight)).Y;
                Assert.IsTrue(gap >= -.1 && gap <= 2, theme + " bottom gap: " + gap);
                scroll.ScrollToVerticalOffset(0); DisclosureAnimationTests.Pump(30); content.UpdateLayout();
                Assert.AreEqual(0d, scroll.VerticalOffset, .01, "Reset before pointer entry");
                var edge = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(5);
                var mouse = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.IsTrue(InsetComboBox.GetEnabled(edge), "Item behavior must be enabled.");
                Assert.AreSame(combo, ItemsControl.ItemsControlFromItemContainer(edge), "Item must retain its ComboBox owner.");
                SetMouseOver(edge, mouse, true);
                Assert.AreEqual(0d, scroll.VerticalOffset, .01, "Setting mouse-over alone");
                var request = (RequestBringIntoViewEventArgs)Activator.CreateInstance(typeof(RequestBringIntoViewEventArgs),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { edge, Rect.Empty }, null);
                request.RoutedEvent = FrameworkElement.RequestBringIntoViewEvent;
                Assert.IsTrue(edge.IsMouseOver);
                var navigation = (DependencyProperty)typeof(InsetComboBox).GetField("KeyboardNavigationProperty", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.IsFalse((bool)combo.GetValue(navigation));
                edge.RaiseEvent(request);
                Assert.IsTrue(request.Handled);
                DisclosureAnimationTests.Pump(30);
                Assert.IsTrue(request.Handled); Assert.AreEqual(0d, scroll.VerticalOffset, .01, "Entering a partially visible item must not move the list.");
                SetMouseOver(edge, mouse, false);
                edge.BringIntoView(); DisclosureAnimationTests.Pump(30);
                Assert.IsTrue(scroll.VerticalOffset > 0, "Explicit navigation must still scroll an off-screen item into view.");
                scroll.ScrollToVerticalOffset(0); DisclosureAnimationTests.Pump(30);
                SetMouseOver(edge, mouse, true);
                using var inputSource = new HwndSource(new HwndSourceParameters("Dropdown keyboard regression")
                    { Width = 10, Height = 10, WindowStyle = unchecked((int)0x80000000) });
                combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, Key.Down)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.IsTrue((bool)combo.GetValue(navigation));
                edge.BringIntoView(); DisclosureAnimationTests.Pump(30);
                Assert.IsTrue(scroll.VerticalOffset > 0, "Keyboard navigation must scroll even with the pointer over a clipped item.");
                SetMouseOver(edge, mouse, false);
            }
        });
    }

    private static void SetMouseOver(UIElement element, DependencyPropertyKey key, bool value)
    {
        // IsMouseOver's CLR getter uses WPF's cached reverse-inheritance flag.
        // Set both pieces without moving the user's physical pointer.
        element.SetValue(key, value);
        var write = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(write);
        var flag = Enum.Parse(write.GetParameters()[0].ParameterType, "IsMouseOverCache");
        write.Invoke(element, new object[] { flag, value });
    }

    private static string Source(string name, [CallerFilePath] string file = "") => Path.Combine(Path.GetDirectoryName(file)!, "..", "DS4Windows", "DS4Forms", name);
}
