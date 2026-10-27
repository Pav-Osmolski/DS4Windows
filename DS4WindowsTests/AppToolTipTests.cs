using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mouse = System.Windows.Input.Mouse;
using System.Xml.Serialization;
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
    private static ToolTip Active()
    {
        var weak = (WeakReference<ToolTip>)typeof(AppToolTips).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return weak != null && weak.TryGetTarget(out var value) ? value : null;
    }
}
