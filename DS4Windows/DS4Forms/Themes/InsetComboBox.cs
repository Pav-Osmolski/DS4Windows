using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DS4WinWPF.DS4Forms.Themes;

// One interaction model for implicit and explicitly sized dropdowns. Animation
// clocks affect paint only; opening the popup never waits for a transition.
public static class InsetComboBox
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(InsetComboBox), new PropertyMetadata(false, OnEnabled));
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static readonly DependencyProperty ShowControllerIconProperty = DependencyProperty.RegisterAttached(
        "ShowControllerIcon", typeof(bool), typeof(InsetComboBox), new PropertyMetadata(false));
    public static void SetShowControllerIcon(DependencyObject value, bool enabled) => value.SetValue(ShowControllerIconProperty, enabled);
    public static bool GetShowControllerIcon(DependencyObject value) => (bool)value.GetValue(ShowControllerIconProperty);

    public static readonly DependencyProperty IsItemPressedProperty = DependencyProperty.RegisterAttached(
        "IsItemPressed", typeof(bool), typeof(InsetComboBox), new PropertyMetadata(false));
    public static bool GetIsItemPressed(DependencyObject value) => (bool)value.GetValue(IsItemPressedProperty);
    private static readonly DependencyPropertyDescriptor HighlightDescriptor =
        DependencyPropertyDescriptor.FromProperty(ComboBoxItem.IsHighlightedProperty, typeof(ComboBoxItem));
    private static void ItemLoaded(object sender, RoutedEventArgs e)
    {
        var item = (ComboBoxItem)sender;
        HighlightDescriptor.RemoveValueChanged(item, ItemHighlightChanged);
        HighlightDescriptor.AddValueChanged(item, ItemHighlightChanged);
        UpdateItem(item, false);
    }
    private static void ItemUnloaded(object sender, RoutedEventArgs e)
    {
        HighlightDescriptor.RemoveValueChanged(sender, ItemHighlightChanged);
        ItemUp(sender, e);
    }
    private static void ItemHighlightChanged(object sender, System.EventArgs e) => UpdateItem((ComboBoxItem)sender);
    private static void ItemHoverChanged(object sender, MouseEventArgs e) => UpdateItem((ComboBoxItem)sender);
    private static void UpdateItem(ComboBoxItem item, bool animate = true) => VisualStateManager.GoToState(item,
        item.IsEnabled && (item.IsHighlighted || item.IsMouseOver) ? "ItemHovered" : "ItemUnhovered",
        animate && SystemParameters.ClientAreaAnimation);
    private static void ItemDown(object sender, MouseButtonEventArgs e) => ((ComboBoxItem)sender).SetValue(IsItemPressedProperty, true);
    private static void ItemUp(object sender, RoutedEventArgs e) => ((ComboBoxItem)sender).SetValue(IsItemPressedProperty, false);
    private static void OnEnabled(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is ComboBoxItem item)
        {
            if ((bool)args.NewValue)
            {
                item.PreviewMouseLeftButtonDown += ItemDown; item.PreviewMouseLeftButtonUp += ItemUp;
                item.MouseLeave += ItemUp; item.Unloaded += ItemUnloaded; item.Loaded += ItemLoaded;
                item.MouseEnter += ItemHoverChanged; item.MouseLeave += ItemHoverChanged;
            }
            else
            {
                item.PreviewMouseLeftButtonDown -= ItemDown; item.PreviewMouseLeftButtonUp -= ItemUp;
                item.MouseLeave -= ItemUp; item.Unloaded -= ItemUnloaded; item.Loaded -= ItemLoaded;
                item.MouseEnter -= ItemHoverChanged; item.MouseLeave -= ItemHoverChanged;
                HighlightDescriptor.RemoveValueChanged(item, ItemHighlightChanged);
            }
            return;
        }
        if (value is not ComboBox combo) return;
        if ((bool)args.NewValue)
        {
            combo.Loaded += Loaded;
            combo.MouseEnter += Changed; combo.MouseLeave += Changed; combo.MouseMove += Changed;
            combo.GotKeyboardFocus += FocusChanged; combo.LostKeyboardFocus += FocusChanged;
            combo.PreviewMouseLeftButtonDown += MouseChanged; combo.PreviewMouseLeftButtonUp += MouseChanged;
            combo.DropDownOpened += OpenChanged; combo.DropDownClosed += OpenChanged;
            combo.IsEnabledChanged += EnabledChanged;
        }
        else
        {
            combo.Loaded -= Loaded;
            combo.MouseEnter -= Changed; combo.MouseLeave -= Changed; combo.MouseMove -= Changed;
            combo.GotKeyboardFocus -= FocusChanged; combo.LostKeyboardFocus -= FocusChanged;
            combo.PreviewMouseLeftButtonDown -= MouseChanged; combo.PreviewMouseLeftButtonUp -= MouseChanged;
            combo.DropDownOpened -= OpenChanged; combo.DropDownClosed -= OpenChanged;
            combo.IsEnabledChanged -= EnabledChanged;
        }
    }
    private static void Loaded(object sender, RoutedEventArgs e)
    {
        Update((ComboBox)sender, false);
    }
    private static void Changed(object sender, MouseEventArgs e) => Update((ComboBox)sender);
    private static void FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => Update((ComboBox)sender);
    private static void MouseChanged(object sender, MouseButtonEventArgs e) => Update((ComboBox)sender);
    private static void OpenChanged(object sender, System.EventArgs e) => Update((ComboBox)sender);
    private static void EnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Update((ComboBox)sender);
    internal static void Update(ComboBox combo, bool animate = true)
    {
        if (combo.Template?.FindName("InsetRoot", combo) is not FrameworkElement) return;
        bool enabled = combo.IsEnabled;
        bool hover = enabled && combo.IsMouseOver;
        bool open = enabled && combo.IsDropDownOpen;
        bool press = hover && Mouse.LeftButton == MouseButtonState.Pressed;
        bool focus = enabled && combo.IsKeyboardFocusWithin;
        void State(string name) => VisualStateManager.GoToState(combo, name,
            animate && SystemParameters.ClientAreaAnimation);
        State(hover ? "Hovered" : "Unhovered");
        State(open ? "Opened" : "Closed");
        State(press ? "Pressed" : "Released");
        State(focus ? "Focused" : "Unfocused");
        State(enabled && combo.Template.FindName("ArrowWell", combo) is FrameworkElement well && new Rect(well.RenderSize).Contains(Mouse.GetPosition(well))
            ? "ArrowHovered" : "ArrowUnhovered");
    }
}
