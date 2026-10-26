using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

public partial class ControllerInfoWindow : Window
{
    private readonly DS4Device device;
    private readonly Func<bool> stillCurrent;
    private readonly Func<OutputDevice> outputProvider;
    private readonly DispatcherTimer statusTimer;
    private bool closed;
    private bool refreshing;
    private ControllerInfoSnapshot snapshot;
    private OutputDevice observedOutput;

    public ControllerInfoWindow()
    {
        InitializeComponent();
        Title = heading.Text = UiText.Get("Controller info");
        physicalTab.Header = UiText.Get("Physical controller");
        emulatedTab.Header = UiText.Get("Emulated controller");
        refreshButton.Content = UiText.Get("Refresh");
        copyButton.Content = UiText.Get("Copy info");
        closeButton.Content = UiText.Get("Close");
        copyButton.IsEnabled = false;
        Closed += (_, _) => { closed = true; statusTimer?.Stop(); };
    }

    internal ControllerInfoWindow(DS4Device device, Func<bool> stillCurrent, Func<OutputDevice> outputProvider) : this()
    {
        this.device = device ?? throw new ArgumentNullException(nameof(device));
        this.stillCurrent = stillCurrent ?? throw new ArgumentNullException(nameof(stillCurrent));
        this.outputProvider = outputProvider ?? throw new ArgumentNullException(nameof(outputProvider));
        controllerName.Text = device.DisplayName;
        statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, CheckCurrent, Dispatcher);
        Loaded += async (_, _) => await RefreshAsync();
    }

    internal void SetPreviewSnapshot(ControllerInfoSnapshot value)
    {
        snapshot = value;
        DataContext = snapshot;
        copyButton.IsEnabled = value != null;
    }

    private void CheckCurrent(object sender, EventArgs e)
    {
        if (closed || refreshing || stillCurrent == null) return;
        if (!stillCurrent())
        {
            statusTimer.Stop();
            refreshButton.IsEnabled = false;
            statusText.Text = UiText.Get("Controller disconnected. Showing the last snapshot.");
        }
        else if (!ReferenceEquals(observedOutput, outputProvider()))
        {
            observedOutput = outputProvider();
            if (snapshot != null)
                SetPreviewSnapshot(snapshot with { Emulated = ControllerInfo.Unavailable(UiText.Get("Virtual output changed. Refresh to read the current controller.")) });
            statusText.Text = UiText.Get("Virtual output changed. Refresh to read the current controller.");
        }
    }

    private async Task RefreshAsync()
    {
        if (closed || refreshing || device == null || !stillCurrent()) return;
        refreshing = true;
        refreshButton.IsEnabled = copyButton.IsEnabled = false;
        statusText.Text = UiText.Get("Loading controller info...");
        try
        {
            var physical = ControllerInfo.Physical(device);
            OutputDevice output = outputProvider();
            ControllerInfoPane emulated = ControllerInfo.Unavailable(UiText.Get("Virtual output is not active."));
            if (output is ViiperOutDevice viiper && viiper.IsRuntimeConnected)
            {
                try
                {
                    var report = await Task.Run(viiper.ReadControllerInfo);
                    emulated = ControllerInfo.Emulated(report);
                }
                catch (Exception error) when (error is IOException or JsonException or ObjectDisposedException)
                {
                    emulated = ControllerInfo.Unavailable(UiText.Get("Emulated controller details could not be read. Refresh to try again."));
                }
            }
            if (closed) return;
            if (!stillCurrent())
            {
                statusText.Text = UiText.Get("Controller disconnected. Showing the last snapshot.");
                return;
            }
            if (!ReferenceEquals(output, outputProvider()))
                emulated = ControllerInfo.Unavailable(UiText.Get("Virtual output changed. Refresh to read the current controller."));
            observedOutput = outputProvider();
            SetPreviewSnapshot(new(physical, emulated));
            statusText.Text = UiText.Format("Snapshot updated at {0}.", DateTime.Now.ToString("T"));
        }
        finally
        {
            refreshing = false;
            if (!closed)
            {
                refreshButton.IsEnabled = stillCurrent();
                copyButton.IsEnabled = snapshot != null;
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (snapshot == null) return;
        try { Clipboard.SetText(ControllerInfo.CopyText(snapshot)); statusText.Text = UiText.Get("Controller info copied."); }
        catch (System.Runtime.InteropServices.COMException) { statusText.Text = UiText.Get("The clipboard is busy. Please try again."); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
