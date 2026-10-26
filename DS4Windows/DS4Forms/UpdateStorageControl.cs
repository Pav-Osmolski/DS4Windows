using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DS4Windows;
using IntegerUpDown = Xceed.Wpf.Toolkit.IntegerUpDown;

namespace DS4WinWPF.DS4Forms;

public sealed class UpdateStorageControl : StackPanel
{
    private readonly ComboBox retention = new() { MinWidth = 260 };
    private readonly IntegerUpDown count = new() { Minimum = 0, Maximum = 100, Value = 1, Width = 85, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox automatic = new() { Margin = new Thickness(0, 8, 0, 12) };
    private readonly TextBlock stored = new(), recoverable = new(), result = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button clean;
    private bool loading, busy;
    private int keep;

    public UpdateStorageControl()
    {
        Margin = new Thickness(0, 12, 0, 0);
        var divider = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 12), SnapsToDevicePixels = true };
        divider.SetResourceReference(Border.BackgroundProperty, "BorderColor");
        Children.Add(divider);
        Children.Add(Label(UiText.Get("Update storage"), "BridgeSectionTitleStyle"));
        Children.Add(Label(UiText.Get("Manage downloaded updates and older support files."), "BridgeSectionDescriptionStyle"));
        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = UiText.Get("Previous versions to keep"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
        foreach (string text in new[] { UiText.Get("None"), UiText.Get("1 previous version (recommended)"), UiText.Get("Choose a number"), UiText.Get("Keep all") }) retention.Items.Add(text);
        row.Children.Add(retention); row.Children.Add(count); Children.Add(row);
        Children.Add(Label(UiText.Get("Older files are removed after a successful start of the updated app."), "BridgeSectionDescriptionStyle"));
        automatic.Content = UiText.Get("Automatically clean up after updating");
        automatic.SetResourceReference(StyleProperty, "BridgeSettingsCheckBoxStyle");
        Children.Add(automatic);
        var totals = new WrapPanel();
        var left = new StackPanel { MinWidth = 250, Margin = new Thickness(0, 0, 20, 0) };
        left.Children.Add(Label(UiText.Get("Stored update files"), "BridgeSectionDescriptionStyle"));
        stored.FontSize = 20; stored.FontWeight = FontWeights.SemiBold; left.Children.Add(stored); totals.Children.Add(left);
        var right = new StackPanel();
        right.Children.Add(Label(UiText.Get("Can be cleaned up"), "BridgeSectionDescriptionStyle"));
        recoverable.FontSize = 20; recoverable.FontWeight = FontWeights.SemiBold; right.Children.Add(recoverable); totals.Children.Add(right);
        var card = new Border { Child = totals, Margin = new Thickness(0, 0, 0, 8) };
        card.SetResourceReference(StyleProperty, "BridgeCardStyle"); Children.Add(card);
        Children.Add(Label(UiText.Get("Includes recognised DS4Windows, VIIPER, USB/IP and updater downloads."), "BridgeSectionDescriptionStyle"));
        var actions = new WrapPanel();
        clean = Button(UiText.Get("Clean up now…"), async (_, _) => await CleanAsync()); actions.Children.Add(clean);
        actions.Children.Add(Button(UiText.Get("View stored files"), async (_, _) => await ViewAsync()));
        actions.Children.Add(Button(UiText.Get("Refresh"), async (_, _) => await RefreshAsync()));
        Children.Add(actions);
        var footer = Label(UiText.Get("Keeps current files and the selected number of previous versions for each component. Profiles and settings are preserved."), "BridgeSectionDescriptionStyle");
        footer.Margin = new Thickness(0, 0, 0, 8);
        Children.Add(footer);
        var statusStyle = new Style(typeof(TextBlock));
        statusStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, 8)));
        statusStyle.Triggers.Add(new DataTrigger
        {
            Binding = new System.Windows.Data.Binding("Text") { RelativeSource = System.Windows.Data.RelativeSource.Self }, Value = "",
            Setters = { new Setter(VisibilityProperty, Visibility.Collapsed) }
        });
        result.Style = statusStyle;
        Children.Add(result);
        keep = 1;
        retention.SelectedIndex = 1;
        automatic.IsChecked = true;
        count.Visibility = Visibility.Collapsed;
        stored.Text = recoverable.Text = Size(0);
        retention.SelectionChanged += (_, _) => Save();
        count.ValueChanged += (_, _) => Save();
        automatic.Click += (_, _) => Save();
        Loaded += async (_, _) =>
        {
            if (PortableLabContext.IsActive) { Visibility = Visibility.Collapsed; return; }
            loading = true;
            try
            {
                var policy = SettingsStorage.Current.UpdateRetention;
                keep = policy.Keep;
                count.Value = Math.Max(0, keep);
                retention.SelectedIndex = keep switch { -1 => 3, 0 => 0, 1 => 1, _ => 2 };
                automatic.IsChecked = policy.Automatic;
            }
            catch (Exception error) when (Expected(error)) { result.Text = error.Message; }
            finally { loading = false; }
            count.Visibility = retention.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            await RefreshAsync();
        };
    }

    private static TextBlock Label(string text, string style)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(StyleProperty, style); return label;
    }
    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
        button.SetResourceReference(StyleProperty, "BridgeSecondaryButtonStyle"); button.Click += handler; return button;
    }
    private async void Save()
    {
        if (loading || PortableLabContext.IsActive) return;
        count.Visibility = retention.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        keep = retention.SelectedIndex switch { 0 => 0, 1 => 1, 3 => -1, _ => count.Value ?? 1 };
        try { SettingsStorage.Current.SetUpdateRetention(keep, automatic.IsChecked == true); await RefreshAsync(); }
        catch (Exception error) when (Expected(error)) { result.Text = UiText.Get("Update storage could not be accessed.") + " " + error.Message; }
    }
    internal async Task RefreshAsync()
    {
        if (busy || PortableLabContext.IsActive || string.IsNullOrWhiteSpace(Global.exedirpath)) return;
        busy = true; clean.IsEnabled = false;
        int selected = keep;
        try
        {
            var snapshot = await Task.Run(() => UpdateStorage.Current.Scan(selected));
            ShowSnapshot(snapshot);
        }
        catch (Exception error) when (Expected(error)) { result.Text = UiText.Get("Update storage could not be accessed.") + " " + error.Message; }
        finally { busy = false; }
        if (keep != selected) await RefreshAsync();
    }
    internal void ShowSnapshot(UpdateStorage.Snapshot snapshot)
    {
        stored.Text = Size(snapshot.TotalBytes); recoverable.Text = Size(snapshot.RecoverableBytes);
        clean.IsEnabled = snapshot.Removable.Length > 0;
        if (snapshot.Warnings.Length > 0) result.Text = UiText.Get("Some folders could not be scanned. Refresh to retry.");
    }
    private async Task CleanAsync()
    {
        if (busy || PortableLabContext.IsActive) return;
        busy = true; clean.IsEnabled = false;
        try
        {
            var storage = UpdateStorage.Current;
            var preview = await Task.Run(() => storage.Scan(keep));
            if (preview.Removable.Length == 0) { result.Text = UiText.Get("There are no old update files to clean up."); return; }
            var window = new Window { Owner = Window.GetWindow(this), Title = UiText.Get("Clean up stored updates"),
                Width = 720, Height = 480, MinWidth = 450, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            window.SetResourceReference(Control.BackgroundProperty, "SurfaceBackgroundColor");
            window.SetResourceReference(Control.ForegroundProperty, "ForegroundColor");
            bool approved = false;
            var panel = new DockPanel { Margin = new Thickness(20) };
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(Button(UiText.Get("Cancel"), (_, _) => window.Close()));
            buttons.Children.Add(Button(UiText.Get("Clean up now…"), (_, _) => { approved = true; window.Close(); }));
            DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            var heading = Label(UiText.Format("Remove {0} old update files and free {1}?", preview.Removable.Length, Size(preview.RecoverableBytes)), "BridgeSectionTitleStyle");
            DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
            panel.Children.Add(new TextBox { Text = string.Join(Environment.NewLine, preview.Removable.Select(file => file.Path)),
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            window.Content = panel; window.ShowDialog();
            if (!approved) return;
            var outcome = await Task.Run(() => storage.Clean(preview, keep));
            result.Text = UiText.Format("Removed {0} files and freed {1}. Skipped {2} files that were in use, changed or could not be removed.", outcome.Deleted, Size(outcome.Bytes), outcome.Skipped);
        }
        catch (Exception error) when (Expected(error)) { result.Text = UiText.Get("Update cleanup could not be completed.") + " " + error.Message; }
        finally { busy = false; await RefreshAsync(); }
    }
    private async Task ViewAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => UpdateStorage.Current.Scan(keep));
            var folders = snapshot.Files.Select(file => Path.GetDirectoryName(file.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (folders.Length == 0) { result.Text = UiText.Get("There are no stored update files."); return; }
            foreach (string folder in folders)
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true });
        }
        catch (Exception error) when (Expected(error)) { result.Text = error.Message; }
    }
    internal static string Size(long bytes) => bytes >= 1024L * 1024 * 1024 ? (bytes / (1024d * 1024 * 1024)).ToString("0.00") + " GB" : (bytes / (1024d * 1024)).ToString("0.00") + " MB";
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception;
}
