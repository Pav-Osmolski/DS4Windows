using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DS4Windows;
using Microsoft.Win32;

namespace DS4WinWPF.DS4Forms;

public partial class BatteryAlertsControl : UserControl
{
    public BatteryAlertSettings Settings { get; }
    private readonly DispatcherTimer saveTimer;
    private bool refreshing, subscribed;
    private CancellationTokenSource previewCancellation;
    private BatteryAlertService subscribedService;
    private readonly bool persist;

    public BatteryAlertsControl() : this(true) { }

    internal BatteryAlertsControl(bool persist, BatteryAlertSettings initial = null)
    {
        this.persist = persist;
        Settings = (initial ?? Global.BatteryAlerts).Clone();
        InitializeComponent();
        repeatIntervalCombo.ItemsSource = new[]
        {
            new KeyValuePair<int, string>(0, UiText.Get("Off")), new(30, global::DS4Windows.UiText.Get("30 seconds")), new(60, global::DS4Windows.UiText.Get("1 minute")),
            new(120, global::DS4Windows.UiText.Get("2 minutes")), new(300, global::DS4Windows.UiText.Get("5 minutes")), new(600, global::DS4Windows.UiText.Get("10 minutes")),
        };
        warningCountCombo.ItemsSource = Enumerable.Range(1, 10)
            .Select(i => new KeyValuePair<int, string>(i, i.ToString()))
            .Append(new(0, UiText.Get("Until charging"))).ToArray();
        DataContext = this;
        controllerCombo.ItemsSource = Enumerable.Range(1, 8).Select(i => UiText.Format("Controller {0}", i));
        controllerCombo.SelectedIndex = 0;
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        saveTimer.Tick += (_, _) => Save();
        Settings.PropertyChanged += Settings_Changed;
        foreach (var rule in new[] { Settings.Low, Settings.Full, Settings.Check }) rule.PropertyChanged += Settings_Changed;
    }

    private void Settings_Changed(object sender, PropertyChangedEventArgs args)
    {
        if (!persist) return;
        Global.BatteryAlerts = Settings;
        saveTimer.Stop(); saveTimer.Start();
    }

    private void Save()
    {
        saveTimer.Stop();
        if (!persist) return;
        if (!Global.Save()) statusText.Text = global::DS4Windows.UiText.Get("Applied for this session, but settings could not be saved. Check access to the settings folder.");
    }

    private async void Control_Loaded(object sender, RoutedEventArgs e)
    {
        if (!persist) return;
        subscribedService = DS4Windows.Program.rootHub?.BatteryAlerts;
        if (!subscribed && subscribedService != null) { subscribedService.StatusChanged += ShowStatus; subscribed = true; }
        await RefreshAsync();
    }

    private void Control_Unloaded(object sender, RoutedEventArgs e)
    {
        previewCancellation?.Cancel();
        if (subscribed) { subscribedService.StatusChanged -= ShowStatus; subscribed = false; }
        if (saveTimer.IsEnabled) Save();
    }

    private void ShowStatus(string message)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => statusText.Text = message);
    }

    private async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true; refreshBtn.IsEnabled = false;
        string voice = Settings.Voice, output = Settings.Endpoint;
        try
        {
            var discovered = await Task.Run(() => (Voices: BatteryAlertAudio.Voices().ToList(), Outputs: BatteryAlertAudio.Outputs().ToList()));
            if (discovered.Voices.All(v => v.Id != voice)) discovered.Voices.Add(new(voice, global::DS4Windows.UiText.Get("Unavailable voice (uses system default)")));
            if (discovered.Outputs.All(v => v.Id != output)) discovered.Outputs.Add(new(output, global::DS4Windows.UiText.Get("Unavailable audio device (select another output)")));
            voiceCombo.ItemsSource = discovered.Voices; voiceCombo.SelectedValue = voice;
            outputCombo.ItemsSource = discovered.Outputs; outputCombo.SelectedValue = output;
        }
        catch (Exception ex) { statusText.Text = global::DS4Windows.UiText.Get("Could not list voices or audio devices: ") + ex.Message; }
        finally { refreshing = false; refreshBtn.IsEnabled = true; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Voice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing && voiceCombo.SelectedValue is string id) Settings.Voice = id;
    }
    private void Output_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing && outputCombo.SelectedValue is string id) Settings.Endpoint = id;
    }
    private void ManageVoices_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:speech") { UseShellExecute = true }); }
        catch (Exception ex) { statusText.Text = global::DS4Windows.UiText.Get("Open Windows Settings > Time & language > Speech to manage voices. ") + ex.Message; }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not BatteryAlertRule rule) return;
        var picker = new OpenFileDialog { Title = global::DS4Windows.UiText.Get("Import a battery alert sound"), Filter = global::DS4Windows.UiText.FileFilter("Sound files|*.wav;*.mp3;*.aif;*.aiff"), CheckFileExists = true };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        button.IsEnabled = false;
        try
        {
            statusText.Text = global::DS4Windows.UiText.Get("Importing and preparing sound…");
            rule.SoundFile = await BatteryAlertAudio.ImportAsync(picker.FileName, BatteryAlertAudio.SoundDirectory, CancellationToken.None);
            rule.SoundName = Path.GetFileName(picker.FileName);
            if (rule.Mode == BatteryAlertMode.Speech || rule.Mode == BatteryAlertMode.Off) rule.Mode = BatteryAlertMode.Sound;
            statusText.Text = global::DS4Windows.UiText.Get("Sound imported. Use Preview to hear it.");
        }
        catch (Exception ex) { statusText.Text = global::DS4Windows.UiText.Get("Sound could not be imported: ") + ex.Message; }
        finally { button.IsEnabled = true; }
    }

    private void ClearSound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not BatteryAlertRule rule) return;
        rule.SoundFile = ""; rule.SoundName = "No sound selected";
        if (rule.Mode is BatteryAlertMode.Sound or BatteryAlertMode.Both) rule.Mode = BatteryAlertMode.Speech;
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (DS4Windows.Program.rootHub == null) { statusText.Text = global::DS4Windows.UiText.Get("Controller service is unavailable."); return; }
        if (sender is not Button button || !Enum.TryParse<BatteryAlertKind>(button.Tag?.ToString(), out var kind)) return;
        previewCancellation?.Cancel();
        using var cancellation = previewCancellation = new();
        // Keep the clicked button focused. Disabling its parent makes WPF move
        // focus to another setting and scroll that setting into view.
        // Starting another preview cancels the previous one instead.
        try { await DS4Windows.Program.rootHub.BatteryAlerts.PreviewAsync(controllerCombo.SelectedIndex, kind, cancellation.Token); }
        catch (OperationCanceledException) { statusText.Text = global::DS4Windows.UiText.Get("Preview stopped."); }
        finally { if (ReferenceEquals(previewCancellation, cancellation)) previewCancellation = null; }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => previewCancellation?.Cancel();
}
