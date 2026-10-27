using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DS4WinWPF.DS4Forms.Themes;

public static class MenuItemInteraction
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(MenuItemInteraction), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static readonly DependencyProperty PressedProperty = DependencyProperty.RegisterAttached(
        "Pressed", typeof(bool), typeof(MenuItemInteraction), new PropertyMetadata(false));
    public static bool GetPressed(DependencyObject value) => (bool)value.GetValue(PressedProperty);
    private static readonly DependencyPropertyDescriptor Highlight = DependencyPropertyDescriptor.FromProperty(MenuItem.IsHighlightedProperty, typeof(MenuItem));
    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs e)
    {
        if (value is not MenuItem item) return;
        if ((bool)e.NewValue)
        {
            item.Loaded += Loaded; item.Unloaded += Unloaded;
            item.PreviewMouseLeftButtonDown += Down; item.PreviewMouseLeftButtonUp += Up;
            item.MouseLeave += Up; item.IsEnabledChanged += EnabledChanged;
        }
        else
        {
            item.Loaded -= Loaded; item.Unloaded -= Unloaded;
            item.PreviewMouseLeftButtonDown -= Down; item.PreviewMouseLeftButtonUp -= Up;
            item.MouseLeave -= Up; item.IsEnabledChanged -= EnabledChanged;
            Highlight.RemoveValueChanged(item, HighlightChanged);
        }
    }
    private static void Loaded(object sender, RoutedEventArgs e)
    {
        Highlight.RemoveValueChanged(sender, HighlightChanged); Highlight.AddValueChanged(sender, HighlightChanged);
        Update((MenuItem)sender, false);
    }
    private static void Unloaded(object sender, RoutedEventArgs e)
    { Highlight.RemoveValueChanged(sender, HighlightChanged); ((MenuItem)sender).SetValue(PressedProperty, false); }
    private static void Down(object sender, MouseButtonEventArgs e) { ((MenuItem)sender).SetValue(PressedProperty, true); Update((MenuItem)sender); }
    private static void Up(object sender, RoutedEventArgs e) { ((MenuItem)sender).SetValue(PressedProperty, false); Update((MenuItem)sender); }
    private static void EnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Update((MenuItem)sender);
    private static void HighlightChanged(object sender, System.EventArgs e) => Update((MenuItem)sender);
    private static void Update(MenuItem item, bool animate = true)
    {
        bool transitions = animate && SystemParameters.ClientAreaAnimation;
        VisualStateManager.GoToState(item, item.IsEnabled && item.IsHighlighted ? "MenuHovered" : "MenuNormal", transitions);
        VisualStateManager.GoToState(item, item.IsEnabled && GetPressed(item) ? "MenuPressed" : "MenuReleased", transitions);
    }
}
