using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DS4WinWPF.DS4Forms.Themes;

// Detached tray popups can retain brush lookups from the theme in which their
// template was first realised. Refresh the small popup palette before painting
// each opening, including nested menu surfaces and retained item containers.
public static class PopupTheme
{
    public static readonly DependencyProperty SynchronizeProperty = DependencyProperty.RegisterAttached(
        "Synchronize", typeof(bool), typeof(PopupTheme), new PropertyMetadata(false, Changed));
    public static bool GetSynchronize(DependencyObject target) => (bool)target.GetValue(SynchronizeProperty);
    public static void SetSynchronize(DependencyObject target, bool value) => target.SetValue(SynchronizeProperty, value);
    private static readonly string[] Palette =
    {
        "CardBackgroundColor", "RaisedBackgroundColor", "BorderColor", "ForegroundColor", "MutedForegroundColor",
        "AccentColor", "AccentForegroundColor", "NavigationSelectionColor", "DisclosureHoverColor", "DisclosureActiveGlyphColor"
    };
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ContextMenu menu) return;
        if ((bool)args.NewValue) menu.Opened += Opened;
        else menu.Opened -= Opened;
    }
    private static void Opened(object sender, RoutedEventArgs args) => Refresh((ContextMenu)sender);
    internal static void Refresh(ContextMenu menu)
    {
        if (Application.Current == null) return;
        foreach (string key in Palette)
            if (Application.Current.TryFindResource(key) is Brush brush) menu.Resources[key] = brush;
    }
}
