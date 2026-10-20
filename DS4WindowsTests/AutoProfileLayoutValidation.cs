using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DS4WinWPF;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

internal static class AutoProfileLayoutValidation
{
    internal static void Validate(Application application, string directory, List<string> renderedFiles)
    {
        var original = application.Resources.MergedDictionaries.ToArray();
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            foreach (string theme in new[] { "DarkTheme", "DefaultTheme" })
            {
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative) });
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                var page = new AutoProfiles(new AutoProfileHolder(false));
                page.SetupDataContext(new ProfileList());
                var vm = page.AutoProfVM;
                var saved = vm.AddProgramExeLocation(@"C:\Games\Zebra\game.exe");
                vm.CreateAutoProfileEntry(saved);
                var draft = vm.AddProgramExeLocation(@"C:\Games\Alpha\game.exe");
                var list = (ListView)page.FindName("programListLV");
                list.SelectedItem = draft;
                Layout(page);
                Assert.AreSame(saved, list.Items[0], "Alphabetical sorting must not override rule priority.");
                vm.MoveItemUpDown(draft, -1);
                Assert.AreSame(draft, list.Items[0]);
                Assert.IsFalse(((Button)page.FindName("moveRuleUpBtn")).IsEnabled);
                Assert.IsTrue(((Button)page.FindName("moveRuleDownBtn")).IsEnabled);
                Layout(page);
                var container = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(saved);
                container.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = container });
                Assert.AreSame(saved, list.SelectedItem, "Right-click actions must target the clicked row.");
                Assert.AreEqual(1, list.SelectedItems.Count, "The rule editor must have one unambiguous selected row.");
                list.SelectedItem = draft;
                var search = (TextBox)page.FindName("autoProfilesSearchTextBox");
                search.Text = "Alpha";
                Assert.AreEqual(1, list.Items.Count);
                Assert.AreEqual(2, vm.ProgramColl.Count, "Search must only filter, never delete rules.");
                Assert.IsFalse(((Button)page.FindName("moveRuleDownBtn")).IsEnabled, "Hidden rows must not make reordering appear ineffective.");
                search.Clear();
                Assert.IsTrue(((Button)page.FindName("removeAutoBtn")).IsEnabled, "Unsaved programs must be removable.");
                var sidebar = (ScrollViewer)page.FindName("sidebarScrollViewer");
                sidebar.ScrollToEnd(); Layout(page);
                var title = (TextBox)page.FindName("mainTxtBox");
                var divider = (Border)page.FindName("ruleOptionsDivider");
                var checkbox = (CheckBox)page.FindName("revertDefaultProfileOnUnknownCk");
                double above = divider.TranslatePoint(new Point(), page).Y - title.TranslatePoint(new Point(0, title.ActualHeight), page).Y;
                double below = checkbox.TranslatePoint(new Point(), page).Y - divider.TranslatePoint(new Point(0, divider.ActualHeight), page).Y;
                Assert.AreEqual(1d, divider.ActualHeight, 0.01, "At 200% scaling the divider must paint two physical pixels.");
                Assert.AreEqual(above, below, 0.01, "The divider must be centred between the input and checkbox.");
                Save(page, directory, $"auto-profiles-{theme}.png", renderedFiles);

                int before = vm.ProgramColl.Count;
                var pending = new TaskCompletionSource<ProgramScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                Func<CancellationToken, IProgress<ProgramScanProgress>, Task<ProgramScanResult>> scan = (token, _) => pending.Task.WaitAsync(token);
                var running = (Task)typeof(AutoProfiles).GetMethod("RunProgramScanAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { "Scanning programs", scan });
                Assert.IsTrue(page.IsEnabled, "Scanning must never dim and disable the entire page.");
                Assert.AreEqual(Visibility.Visible, ((Border)page.FindName("scanStatusPanel")).Visibility);
                Assert.IsFalse(((Button)page.FindName("addProgramsBtn")).IsEnabled);
                Assert.IsTrue(((Button)page.FindName("cancelScanBtn")).IsEnabled);
                Layout(page); Save(page, directory, $"auto-profiles-scanning-{theme}.png", renderedFiles);
                ((Button)page.FindName("cancelScanBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                page.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
                Assert.IsTrue(running.IsCompleted, "Cancel must restore the page without waiting for discovery to finish.");
                running.GetAwaiter().GetResult();
                Assert.AreEqual(before, vm.ProgramColl.Count);
                Assert.IsTrue(((Button)page.FindName("addProgramsBtn")).IsEnabled);
                StringAssert.Contains(((TextBlock)page.FindName("scanStatusText")).Text, "cancelled");
                pending.TrySetResult(new(Array.Empty<string>(), 0, 0));
            }
        }
        finally
        {
            application.Resources.MergedDictionaries.Clear();
            foreach (var item in original) application.Resources.MergedDictionaries.Add(item);
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    private static void Layout(FrameworkElement page)
    {
        page.Measure(new Size(1180, 860)); page.Arrange(new Rect(0, 0, 1180, 860)); page.UpdateLayout();
    }
    private static void Save(FrameworkElement page, string directory, string name, List<string> renderedFiles)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)page.FindResource("BackgroundColor"), null, new Rect(0, 0, 1180, 860));
        }
        var bitmap = new RenderTargetBitmap(1180, 860, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(directory, name);
        using var file = File.Create(path); encoder.Save(file); renderedFiles.Add(path);
    }
}
