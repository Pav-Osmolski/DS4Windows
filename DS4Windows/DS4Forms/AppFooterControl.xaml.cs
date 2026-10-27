using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

public partial class AppFooterControl : UserControl
{
    private bool running, warning;
    private int controllerCount;
    public event EventHandler HotkeysRequested, AboutRequested, ServiceToggleRequested, LogRequested;
    internal Button ServiceButton => serviceButton;
    internal bool IsActivityExpanded
    {
        get => activityPanel.Visibility == Visibility.Visible;
        set => activityPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public AppFooterControl()
    {
        InitializeComponent();
        System.Windows.Automation.AutomationProperties.SetName(hotkeysButton, UiText.Get("_Hotkeys").Replace("_", ""));
        System.Windows.Automation.AutomationProperties.SetName(aboutButton, UiText.Get("_About").Replace("_", ""));
        UpdateService(false);
    }

    internal void UpdateService(bool value)
    {
        running = value;
        serviceButton.Content = running ? Translations.Strings.StopText : Translations.Strings.StartText;
        serviceButton.SetResourceReference(BackgroundProperty, running ? "RaisedBackgroundColor" : "AccentColor");
        serviceButton.SetResourceReference(ForegroundProperty, running ? "ForegroundColor" : "AccentForegroundColor");
        UpdateSummary();
    }

    internal void UpdateControllerCount(int value)
    {
        controllerCount = Math.Max(0, value);
        UpdateSummary();
    }

    internal void UpdateActivity(string message, bool isWarning, DateTime timestamp)
    {
        warning = isWarning;
        activityMessage.Text = string.IsNullOrWhiteSpace(message) ? "—" : message;
        activityMessage.SetResourceReference(ForegroundProperty, warning ? "DangerColor" : "ForegroundColor");
        activityTime.Text = timestamp.ToString("T");
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        serviceStatus.Text = warning ? UiText.Get("Needs attention") : running ? UiText.Get("Running") : UiText.Get("Stopped");
        serviceStatus.SetResourceReference(TextBlock.ForegroundProperty, warning ? "DangerColor" : "ForegroundColor");
        serviceDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, warning ? "DangerColor" : running ? "SuccessColor" : "MutedForegroundColor");
        // Running describes the service only; it must not imply that a virtual
        // output is ready simply because physical controllers were discovered.
        controllerSummary.Text = warning ? activityMessage.Text : Translations.Strings.Controllers + ": " + controllerCount;
        controllerSummary.SetResourceReference(TextBlock.ForegroundProperty, warning ? "DangerColor" : "MutedForegroundColor");
        System.Windows.Automation.AutomationProperties.SetName(statusButton, serviceStatus.Text + ". " + controllerSummary.Text);
    }

    private void ToggleActivity_Click(object sender, RoutedEventArgs e) => IsActivityExpanded = !IsActivityExpanded;
    private void Hotkeys_Click(object sender, RoutedEventArgs e) => HotkeysRequested?.Invoke(this, EventArgs.Empty);
    private void About_Click(object sender, RoutedEventArgs e) => AboutRequested?.Invoke(this, EventArgs.Empty);
    private void ToggleService_Click(object sender, RoutedEventArgs e) => ServiceToggleRequested?.Invoke(this, EventArgs.Empty);
    private void OpenLog_Click(object sender, RoutedEventArgs e) => LogRequested?.Invoke(this, EventArgs.Empty);
}
