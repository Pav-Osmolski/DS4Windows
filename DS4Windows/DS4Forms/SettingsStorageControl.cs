using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

public sealed class SettingsStorageControl : Border
{
    private readonly ComboBox location = new() { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock path = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
    private readonly Button migrate, cancel;
    private readonly StackPanel details = new();
    private readonly TextBlock summary = new();
    // Reuse the shared circular Expander indicator used elsewhere in the app.
    // The toggle receives pointer input so its hover/pressed feedback matches
    // the other expanders. It sits beside, not inside, the header button.
    private readonly Expander chevron = new() { MinWidth = 28, MinHeight = 28,
        Focusable = false, IsTabStop = false, HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly DisclosurePreferenceWriter disclosureWriter;
    internal Action<bool> PersistExpanded;
    internal Task DisclosureSaved => disclosureWriter.Saved;
    private string locationSummary;
    private bool expanded, initialized, synchronizingIndicator;
    internal Func<SettingsStorage> Storage = () => SettingsStorage.Current;
    internal bool IsExpanded => expanded;
    internal Func<bool> BeforeChange;
    internal Action CloseRequested;

    public SettingsStorageControl()
    {
        disclosureWriter = new(value =>
        {
            if (PersistExpanded != null) PersistExpanded(value);
            else Storage().SetStorageExpanded(value);
        }, error => NLog.LogManager.GetLogger("SettingsStorage").Warn(error, "Could not remember Settings & profiles expansion state."));
        SetResourceReference(StyleProperty, "BridgeCardStyle");
        Padding = new Thickness(18, 14, 18, 14);
        Margin = new Thickness(0, 0, 0, 14);
        var panel = new StackPanel();
        Child = panel;
        var heading = new Grid { Height = 42 };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = UiText.Get("Settings & profiles"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        summary.Margin = new Thickness(0, 3, 0, 0);
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundColor");
        labels.Children.Add(summary);
        heading.Children.Add(labels);
        var indicatorSpace = new Border { Width = 44 };
        Grid.SetColumn(indicatorSpace, 1);
        heading.Children.Add(indicatorSpace);
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(chevron, System.Windows.Input.KeyboardNavigationMode.None);
        // A real button makes the entire header keyboard and screen-reader accessible.
        var disclosure = new Button { Content = heading };
        disclosure.SetResourceReference(StyleProperty, "BridgeDisclosureHeaderButtonStyle");
        disclosure.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, UiText.Get("Settings & profiles"));
        disclosure.Click += (_, _) => SetExpanded(!expanded);
        chevron.Expanded += IndicatorChanged;
        chevron.Collapsed += IndicatorChanged;
        var header = new Grid();
        header.Children.Add(disclosure);
        header.Children.Add(chevron);
        panel.Children.Add(header);
        panel.Children.Add(details);
        panel = details;
        details.Margin = new Thickness(0, 12, 0, 0);
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = UiText.Get("Save location"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 6) });
        location.Items.Add(UiText.Get("AppData — recommended"));
        location.Items.Add(UiText.Get("Program folder"));
        location.Margin = new Thickness(0, 0, 10, 6);
        row.Children.Add(location);
        row.Children.Add(Button(UiText.Get("Change location…"), ChangeLocation));
        panel.Children.Add(row);
        path.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundColor");
        panel.Children.Add(path);
        panel.Children.Add(status);
        var actions = new WrapPanel();
        actions.Children.Add(Button(UiText.Get("Open profiles folder"), OpenFolder));
        actions.Children.Add(Button(UiText.Get("Back up all…"), BackUp));
        actions.Children.Add(Button(UiText.Get("Restore backup…"), Restore));
        migrate = Button(UiText.Get("Migrate to AppData…"), Migrate);
        actions.Children.Add(migrate);
        cancel = Button(UiText.Get("Cancel pending change"), Cancel);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        Loaded += (_, _) => Refresh();
    }

    private static Button Button(string text, RoutedEventHandler action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6) };
        button.SetResourceReference(StyleProperty, "BridgeSecondaryButtonStyle");
        button.Click += action;
        return button;
    }

    internal void Refresh()
    {
        Visibility = PortableLabContext.IsActive ? Visibility.Collapsed : Visibility.Visible;
        if (PortableLabContext.IsActive || string.IsNullOrWhiteSpace(Global.appdatapath)) return;
        Run(() =>
        {
            var storage = Storage();
            var active = storage.FolderFor(Global.appdatapath);
            location.SelectedIndex = (int)active;
            path.Text = Path.Combine(Global.appdatapath, "Profiles");
            bool writable = SettingsStorage.CanWrite(Global.appdatapath, out string reason);
            bool pending = storage.HasPending;
            if (!initialized) { expanded = storage.StorageExpanded; disclosureWriter.Initialize(expanded); initialized = true; }
            // Warnings and pending operations must remain discoverable. Do not overwrite
            // the user's remembered preference when expanding for a warning.
            if (!writable || pending) expanded = true;
            locationSummary = (active == SettingsFolder.AppData ? UiText.Get("AppData — recommended") : UiText.Get("Program folder")) +
                " · " + (writable ? UiText.Get("Writable") : UiText.Get("Read only"));
            summary.SetResourceReference(TextBlock.ForegroundProperty, writable ? "MutedForegroundColor" : "DangerColor");
            UpdateDisclosure();
            status.Text = pending ? UiText.Get("A settings change is pending. Fully close DS4Windows and open it again to apply it. The current location remains active until then.") :
                writable ? UiText.Get("Writable. Changing location requires a restart; original files are kept.") :
                    UiText.Get("Read only. Profile changes cannot be saved. Choose AppData or adjust this folder's permissions.");
            status.ToolTip = writable ? null : reason;
            status.SetResourceReference(TextBlock.ForegroundProperty, writable ? "MutedForegroundColor" : "DangerColor");
            migrate.Visibility = active == SettingsFolder.Program && InstallationModeInfo.IsInstalled && storage.HasSettings(SettingsFolder.Program)
                ? Visibility.Visible : Visibility.Collapsed;
            migrate.IsEnabled = !pending;
            cancel.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            location.IsEnabled = !pending;
        });
    }

    private void UpdateDisclosure()
    {
        details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        summary.Text = expanded ? UiText.Get("Choose where profiles and application settings are kept.") : locationSummary;
        summary.ToolTip = summary.Text;
        synchronizingIndicator = true;
        try { chevron.IsExpanded = expanded; }
        finally { synchronizingIndicator = false; }
    }

    private void IndicatorChanged(object sender, RoutedEventArgs e)
    {
        if (synchronizingIndicator) return;
        SetExpanded(chevron.IsExpanded);
    }

    private void SetExpanded(bool value)
    {
        expanded = value;
        UpdateDisclosure();
        disclosureWriter.Queue(value);
    }

    private bool CanChange() => BeforeChange?.Invoke() != false;
    private void ChangeLocation(object sender, RoutedEventArgs e)
    {
        if (!CanChange()) return;
        Run(() => Queue((SettingsFolder)location.SelectedIndex));
    }
    private void Migrate(object sender, RoutedEventArgs e)
    {
        if (!CanChange()) return;
        Run(() => Queue(SettingsFolder.AppData));
    }
    private void Queue(SettingsFolder target)
    {
        var storage = SettingsStorage.Current;
        var source = storage.FolderFor(Global.appdatapath);
        if (source == target) return;
        if (!SettingsStorage.CanWrite(storage.Root(target), out string reason)) throw new IOException(UiText.Get("The destination is not writable.") + "\n" + reason);
        bool existing = storage.HasSettings(target);
        var answer = MessageBox.Show(Window.GetWindow(this),
            UiText.Get("Copy your current settings and profiles to this location? Choose No to use the settings already there. The current folder will be kept.") +
            "\n\n" + storage.Root(target), "DS4Windows", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return;
        bool copy = answer == MessageBoxResult.Yes;
        if (!copy && !existing) throw new IOException(UiText.Get("There are no settings in the selected location. Choose Copy current settings instead."));
        if (copy && existing && MessageBox.Show(Window.GetWindow(this),
            UiText.Get("The destination already contains settings. Copying replaces its complete settings set after a verified backup. Files will not be merged. Continue?"),
            "DS4Windows", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        storage.Queue(source, target, copy, replaceExisting: copy && existing);
        Queued();
    }
    private void Queued()
    {
        Refresh();
        if (MessageBox.Show(Window.GetWindow(this), UiText.Get("The change will be applied the next time DS4Windows starts. Close DS4Windows now? Reopen it afterwards to continue."),
            "DS4Windows", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) CloseRequested?.Invoke();
    }
    private void Cancel(object sender, RoutedEventArgs e) => Run(() => { SettingsStorage.Current.CancelPending(); Refresh(); });
    private void OpenFolder(object sender, RoutedEventArgs e) => Run(() =>
    {
        string folder = Path.Combine(Global.appdatapath, "Profiles");
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true });
    });
    private void BackUp(object sender, RoutedEventArgs e)
    {
        if (!CanChange()) return;
        Run(() =>
        {
            var dialog = new SaveFileDialog { Filter = UiText.FileFilter("DS4Windows settings backup (*.zip)|*.zip"),
                FileName = "DS4Windows-settings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            string temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { SettingsStorage.Current.BackUp(Global.appdatapath, temporary); File.Move(temporary, dialog.FileName, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            MessageBox.Show(Window.GetWindow(this), UiText.Get("Settings backup saved.") + "\n\n" + dialog.FileName, "DS4Windows");
        });
    }
    private void Restore(object sender, RoutedEventArgs e)
    {
        if (!CanChange()) return;
        Run(() =>
        {
            var dialog = new OpenFileDialog { Filter = UiText.FileFilter("DS4Windows settings backup (*.zip)|*.zip") };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            if (MessageBox.Show(Window.GetWindow(this), UiText.Get("Restore this complete settings backup on the next start? Current settings will be backed up before replacement. Files will not be merged."),
                "DS4Windows", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var storage = SettingsStorage.Current;
            storage.QueueRestore(dialog.FileName, storage.FolderFor(Global.appdatapath));
            Queued();
        });
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.Xml.XmlException)
        {
            AppLogger.LogToGui("Settings storage: " + error.Message, false);
            MessageBox.Show(Window.GetWindow(this), UiText.Get("The settings operation could not be completed. Your current location remains active.") + "\n\n" + error.Message,
                "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
