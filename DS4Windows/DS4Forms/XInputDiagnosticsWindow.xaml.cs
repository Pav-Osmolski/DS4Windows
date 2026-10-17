using System;
using System.Windows;
using System.Windows.Threading;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WinWPF.DS4Forms;

public partial class XInputDiagnosticsWindow : Window
{
    private readonly XInputDiagnosticsViewModel viewModel = new();
    private readonly DispatcherTimer timer;

    public XInputDiagnosticsWindow()
    {
        InitializeComponent();
        // Keep the close button reachable on smaller displays; content can scroll.
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40));
        DataContext = viewModel;
        timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += Poll_Tick;
        Loaded += Loaded_Window;
        Closed += Closed_Window;
    }

    private async void Loaded_Window(object sender, RoutedEventArgs e)
    {
        timer.Start();
        await viewModel.PollAsync();
    }

    private async void Poll_Tick(object sender, EventArgs e) => await viewModel.PollAsync();
    private void Closed_Window(object sender, EventArgs e)
    {
        timer.Stop();
        timer.Tick -= Poll_Tick;
        Loaded -= Loaded_Window;
        viewModel.Close();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
