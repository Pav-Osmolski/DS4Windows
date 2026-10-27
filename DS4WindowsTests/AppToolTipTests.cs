using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mouse = System.Windows.Input.Mouse;
using System.Xml.Serialization;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DS4WinWPF.DS4Forms.Themes;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class AppToolTipTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TooltipPreferenceRoundTripsAndOldSettingsDefaultToOn(bool enabled)
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        var dto = new AppSettingsDTO(); dto.MapFrom(new BackingStore { showTooltips = enabled });
        using var writer = new StringWriter(); serializer.Serialize(writer, dto);
        using var reader = new StringReader(writer.ToString());
        var restored = new BackingStore(); ((AppSettingsDTO)serializer.Deserialize(reader)).MapTo(restored);
        Assert.AreEqual(enabled, restored.showTooltips);
        using var old = new StringReader("<Profile />");
        ((AppSettingsDTO)serializer.Deserialize(old)).MapTo(restored);
        Assert.IsTrue(restored.showTooltips, "Existing installations must keep tooltips enabled by default.");
    }

    [TestMethod]
    public void TooltipLifetimeClosesForOwnerRemovalPointerExitAndReplacementAndHonoursDisable()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            bool previous = Global.ShowTooltips;
            try
            {
                AppToolTips.Initialize(); Global.ShowTooltips = true;
                var owner = new Button { ToolTip = "Help" };
                var first = new ToolTip { PlacementTarget = owner, Content = "Help" };
                first.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent));
                Assert.AreSame(first, Active());
                owner.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                Assert.IsNull(Active(), "Leaving the tooltip owner must dismiss the popup.");
                first.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent));
                owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert.IsNull(Active(), "Changing pages must not leave a tooltip behind.");
                first.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent));
                var second = new ToolTip { PlacementTarget = owner, Content = "Other help" };
                second.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent));
                Assert.AreSame(second, Active());
                first.RaiseEvent(new RoutedEventArgs(ToolTip.ClosedEvent));
                Assert.AreSame(second, Active(), "A late close from the previous popup must not detach its replacement.");
                InputManager.Current.ProcessInput(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                    { RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = owner });
                Assert.IsNull(Active(), "Scrolling must dismiss the current tooltip immediately.");
                ToolTipService.SetShowDuration(owner, 500);
                first.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent));
                DisclosureAnimationTests.Pump(650);
                Assert.IsNull(Active(), "A tooltip must still expire if the usual mouse-leave notification is missed.");
                Global.ShowTooltips = false; AppToolTips.Dismiss();
                Assert.IsNull(Active());
                first.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent));
                Assert.IsNull(Active(), "Disabled tooltips must not become active, including programmatic openings.");
                var opening = (ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { true }, null);
                owner.RaiseEvent(opening);
                Assert.IsTrue(opening.Handled, "The off switch must suppress WPF hover openings.");
            }
            finally { AppToolTips.Dismiss(); Global.ShowTooltips = previous; }
        });
    }
    [TestMethod]
    [DataRow("Settings category")]
    [DataRow("Advanced category")]
    [DataRow("Circular expander")]
    [DataRow("Nintendo expander")]
    public void PresentedTabContentKeepsHoverHelpDuringMotionAndCursorQueries(string kind)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            bool previous = Global.ShowTooltips;
            try
            {
                AppToolTips.Initialize(); Global.ShowTooltips = true;
                foreach (var choice in new[] { AppThemeChoice.Light, AppThemeChoice.Dark, AppThemeChoice.Graphite,
                    AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast })
                {
                    var root = new Border();
                    root.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(choice, highContrast: false));
                    root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                    var label = new TextBlock { Text = kind == "Settings category" ? "General" : "Output" };
                    FrameworkElement control;
                    Image artwork = null;
                    if (kind.EndsWith("expander", StringComparison.OrdinalIgnoreCase))
                    {
                        object headerContent = label;
                        if (kind == "Nintendo expander")
                        {
                            artwork = new Image { Width = 64, Height = 44, Margin = new Thickness(0, 0, 12, 0) };
                            var illustrated = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
                            illustrated.Children.Add(artwork); illustrated.Children.Add(label); headerContent = illustrated;
                        }
                        control = new Expander { Header = headerContent, Content = "Options", Margin = new Thickness(20) };
                    }
                    else
                    {
                        var radio = new RadioButton { Content = label, Margin = new Thickness(20) };
                        radio.SetResourceReference(FrameworkElement.StyleProperty, "BridgeSettingsCategoryStyle");
                        control = radio;
                    }
                    var logicalTab = new TabItem { Header = "Settings", ToolTip = "Tab help", Content = control };
                    var tabs = new TabControl { SelectedIndex = 0 };
                    tabs.SetResourceReference(FrameworkElement.StyleProperty, "BridgeProfileTabControlStyle");
                    tabs.Items.Add(logicalTab); root.Child = tabs;
                    using var source = new HwndSource(new HwndSourceParameters("DS4Windows tooltip ownership")
                        { Width = 420, Height = 160, WindowStyle = unchecked((int)0x80000000) });
                    source.RootVisual = root;
                    ThemeConsistencyValidation.Layout(root, 420, 160);
                    Assert.AreEqual(0d, logicalTab.ActualWidth, "Exercise the real pattern where tab content is displayed apart from its header.");
                    FrameworkElement hoverOwner = control;
                    DependencyObject pointerTarget = label;
                    if (control is Expander expander)
                    {
                        var header = (ToggleButton)expander.Template.FindName("HeaderSite", expander);
                        hoverOwner = header; pointerTarget = (DependencyObject)header.Template.FindName("Circle", header);
                        Assert.AreSame(header, AppToolTips.ResolveHoverOwner(logicalTab, label), "Arrow and header text must share one hover owner.");
                    }
                    Assert.AreSame(hoverOwner, AppToolTips.ResolveHoverOwner(logicalTab, pointerTarget), choice + " " + kind);
                    ToolTipService.SetShowDuration(logicalTab, int.MaxValue);
                    var tip = new ToolTip { PlacementTarget = logicalTab, Content = "Hover help" };
                    AppToolTips.Track(tip, pointerTarget);
                    var timer = (System.Windows.Threading.DispatcherTimer)typeof(AppToolTips).GetField("expiry", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    Assert.AreEqual(TimeSpan.FromMilliseconds(int.MaxValue), timer.Interval, "Do not cap WPF's stay-while-hovered duration.");
                    InputManager.Current.ProcessInput(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                        { RoutedEvent = Mouse.PreviewMouseMoveEvent, Source = pointerTarget });
                    InputManager.Current.ProcessInput(new QueryCursorEventArgs(Mouse.PrimaryDevice, 0)
                        { RoutedEvent = Mouse.QueryCursorEvent, Source = pointerTarget });
                    label.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                    if (artwork != null)
                    {
                        Assert.AreSame(hoverOwner, AppToolTips.ResolveHoverOwner(logicalTab, artwork), "The Nintendo artwork must share its header's hover lifetime.");
                        artwork.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                    }
                    DisclosureAnimationTests.Pump(750);
                    Assert.AreSame(tip, Active(), "Motion within the control, cursor queries and child-element transitions must not close hover help after half a second.");
                    hoverOwner.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                    Assert.IsNull(Active(), "Leaving the actual visible hover control must still dismiss immediately.");
                }
            }
            finally { AppToolTips.Dismiss(); Global.ShowTooltips = previous; }
        });
    }

    private static ToolTip Active()
    {
        var weak = (WeakReference<ToolTip>)typeof(AppToolTips).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return weak != null && weak.TryGetTarget(out var value) ? value : null;
    }
}
