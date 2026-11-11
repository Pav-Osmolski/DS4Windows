using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

internal sealed class HidHideRecoveryWindow : Window
{
    private readonly List<(string Id, CheckBox Choice)> choices = new();
    private readonly Button restore;
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };

    internal HidHideRecoveryWindow(HidHideRecoveryAudit audit)
    {
        Title = UiText.Get("Recover hidden controllers");
        Width = 720; MinWidth = 460; Height = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(StyleProperty, "WindowStyle");
        SetResourceReference(BackgroundProperty, "SurfaceBackgroundColor");
        SetResourceReference(ForegroundProperty, "ForegroundColor");
        var layout = new DockPanel { Margin = new Thickness(22) };
        Content = layout;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 22,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        heading.Children.Add(new TextBlock { Text = UiText.Get("Select only the physical controller you want to use outside DS4Windows. Older hiding rules may have been added manually or by another app. Nothing is selected automatically."), TextWrapping = TextWrapping.Wrap });
        var bottom = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        message.Margin = new Thickness(0, 0, 0, 12);
        bottom.Children.Add(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bottom.Children.Add(buttons);
        var close = new Button { Content = UiText.Get("Close"), IsCancel = true, MinWidth = 95, Margin = new Thickness(0, 0, 10, 0) };
        close.SetResourceReference(StyleProperty, "BridgeSecondaryButtonStyle");
        close.Click += (_, _) => Close(); buttons.Children.Add(close);
        restore = new Button { Content = UiText.Get("Restore selected controllers"), IsEnabled = false };
        restore.SetResourceReference(StyleProperty, "BridgePrimaryButtonStyle");
        buttons.Children.Add(restore);
        var rows = new StackPanel();
        foreach (string id in audit.SelectableIds)
        {
            var check = new CheckBox { Content = new TextBlock { Text = id,
                TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(10),
                VerticalContentAlignment = VerticalAlignment.Center };
            check.SetResourceReference(StyleProperty, "CheckBoxStyle");
            check.Checked += (_, _) => UpdateButton(); check.Unchecked += (_, _) => UpdateButton();
            choices.Add((id, check));
            var card = new Border { Margin = new Thickness(0, 0, 0, 8), Child = check };
            card.SetResourceReference(StyleProperty, "BridgeCardStyle");
            rows.Children.Add(card);
        }
        if (choices.Count == 0)
            rows.Children.Add(new TextBlock { Text = UiText.Get("No recoverable hiding rules were found. Rules in use by another DS4Windows session are protected."), TextWrapping = TextWrapping.Wrap });
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(scroll);
        message.Text = UiText.Get("A backup is saved before changes. Unselected rules and HidHide application permissions are preserved. Your controller will be hidden again if you start DS4Windows with hiding enabled.");
        restore.Click += async (_, _) =>
        {
            string[] selected = choices.Where(c => c.Choice.IsChecked == true).Select(c => c.Id).ToArray();
            restore.IsEnabled = false; close.IsEnabled = false; scroll.IsEnabled = false;
            try
            {
                string backup = await Task.Run(() => App.rootHub.RecoverSelectedHiddenControllers(audit, selected));
                message.Text = UiText.Get("Selected hiding rules were removed. Reconnect your controller if Windows has not refreshed it yet. Backup:") + " " + backup;
            }
            catch (Exception ex) { message.Text = UiText.Get(ex.Message); }
            finally { close.IsEnabled = true; }
        };
    }

    private void UpdateButton() => restore.IsEnabled = choices.Any(c => c.Choice.IsChecked == true);
}
