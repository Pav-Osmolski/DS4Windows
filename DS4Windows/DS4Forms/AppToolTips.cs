using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

// WPF still owns hover detection and opening delays. Keep popup lifetime tied
// to the current owner so scrolling, navigation and lost focus cannot strand it.
internal static class AppToolTips
{
    private static bool initialized;
    private static WeakReference<ToolTip> current;
    private static WeakReference<FrameworkElement> owner;
    private static WeakReference<Window> window;
    private static DispatcherTimer expiry;

    internal static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler(Opening));
        EventManager.RegisterClassHandler(typeof(FrameworkContentElement), ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler(Opening));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(Opened));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.ClosedEvent, new RoutedEventHandler(Closed));
        InputManager.Current.PreProcessInput += BeforeInput;
        expiry = new DispatcherTimer(DispatcherPriority.Background);
        expiry.Tick += (_, _) => Dismiss();
    }

    private static void Opening(object sender, ToolTipEventArgs e)
    {
        if (!Global.ShowTooltips) { e.Handled = true; Dismiss(); }
    }

    private static void Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ToolTip tip) Track(tip, System.Windows.Input.Mouse.DirectlyOver as DependencyObject);
    }

    internal static void Track(ToolTip tip, DependencyObject pointerTarget)
    {
        if (!Global.ShowTooltips) { tip.IsOpen = false; return; }
        Dismiss();
        current = new WeakReference<ToolTip>(tip);
        if (ResolveHoverOwner(tip.PlacementTarget as FrameworkElement, pointerTarget) is FrameworkElement target)
        {
            owner = new WeakReference<FrameworkElement>(target);
            target.MouseLeave += OwnerLeft;
            target.Unloaded += OwnerUnloaded;
            target.IsVisibleChanged += OwnerVisibilityChanged;
            if (Window.GetWindow(target) is Window ownerWindow)
            {
                window = new WeakReference<Window>(ownerWindow);
                ownerWindow.Deactivated += WindowChanged;
                ownerWindow.LocationChanged += WindowChanged;
            }
        }
        int duration = tip.PlacementTarget is DependencyObject placement ? ToolTipService.GetShowDuration(placement) : 5000;
        // Honour WPF's configured duration, including its stay-while-hovered
        // default. A safety cap must not silently shorten legitimate hover help.
        expiry.Interval = TimeSpan.FromMilliseconds(Math.Max(duration, 1));
        expiry.Start();
    }

    internal static FrameworkElement ResolveHoverOwner(FrameworkElement placement, DependencyObject pointerTarget)
    {
        // TabItem content can be rendered by a separate ContentPresenter. The
        // tooltip placement target then describes a hidden/sidebar tab header,
        // not the button or expander under the pointer. Its RenderSize/bounds
        // cannot be used as a hit-test for the displayed content.
        for (DependencyObject node = pointerTarget; node != null; node = Parent(node))
            if (ReferenceEquals(node, placement)) return placement;

        Window placementWindow = placement == null ? null : Window.GetWindow(placement);
        for (DependencyObject node = pointerTarget; node != null; node = Parent(node))
            if (node is Control control && control is not ToolTip &&
                (placement == null || ReferenceEquals(Window.GetWindow(control), placementWindow))) return control;
        return placement;
    }

    private static DependencyObject Parent(DependencyObject node) => node is Visual
        ? VisualTreeHelper.GetParent(node)
        : node is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(node);

    private static void Closed(object sender, RoutedEventArgs e)
    {
        if (current != null && current.TryGetTarget(out var tip) && ReferenceEquals(tip, sender)) Detach();
    }
    private static void BeforeInput(object sender, PreProcessInputEventArgs e)
    {
        if (current == null) return;
        InputEventArgs input = e.StagingItem.Input;
        if (input is MouseWheelEventArgs || input is KeyEventArgs { IsDown: true } ||
            input is MouseButtonEventArgs { ButtonState: MouseButtonState.Pressed }) Dismiss();
        // Moving within a tooltip's hover control, querying the cursor, or
        // receiving popup-related focus notifications must not dismiss it.
        // MouseLeave on the real hover control handles pointer departure.
    }
    private static void OwnerLeft(object sender, MouseEventArgs e) => Dismiss();
    private static void OwnerUnloaded(object sender, RoutedEventArgs e) => Dismiss();
    private static void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (!(bool)e.NewValue) Dismiss(); }
    private static void WindowChanged(object sender, EventArgs e) => Dismiss();

    internal static void Dismiss()
    {
        ToolTip tip = null;
        current?.TryGetTarget(out tip);
        Detach();
        if (tip != null) tip.IsOpen = false;
    }
    private static void Detach()
    {
        expiry?.Stop();
        if (owner != null && owner.TryGetTarget(out var target))
        {
            target.MouseLeave -= OwnerLeft;
            target.Unloaded -= OwnerUnloaded;
            target.IsVisibleChanged -= OwnerVisibilityChanged;
        }
        if (window != null && window.TryGetTarget(out var ownerWindow))
        {
            ownerWindow.Deactivated -= WindowChanged;
            ownerWindow.LocationChanged -= WindowChanged;
        }
        current = null; owner = null; window = null;
    }
}
