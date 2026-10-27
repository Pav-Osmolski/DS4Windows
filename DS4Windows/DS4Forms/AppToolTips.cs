using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
        if (sender is not ToolTip tip) return;
        if (!Global.ShowTooltips) { tip.IsOpen = false; return; }
        Dismiss();
        current = new WeakReference<ToolTip>(tip);
        if (tip.PlacementTarget is FrameworkElement target)
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
        expiry.Interval = TimeSpan.FromMilliseconds(Math.Clamp(duration, 500, 15000));
        expiry.Start();
    }

    private static void Closed(object sender, RoutedEventArgs e)
    {
        if (current != null && current.TryGetTarget(out var tip) && ReferenceEquals(tip, sender)) Detach();
    }
    private static void BeforeInput(object sender, PreProcessInputEventArgs e)
    {
        if (current == null) return;
        InputEventArgs input = e.StagingItem.Input;
        if (input is MouseWheelEventArgs or KeyEventArgs or KeyboardFocusChangedEventArgs ||
            input is MouseButtonEventArgs { ButtonState: MouseButtonState.Pressed }) Dismiss();
        else if (input is MouseEventArgs && owner != null && owner.TryGetTarget(out var target) &&
            !new Rect(target.RenderSize).Contains(System.Windows.Input.Mouse.GetPosition(target))) Dismiss();
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
