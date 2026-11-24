using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class OutputSlotsLayoutTests
{
    [TestMethod]
    public void ColumnPreferencesRoundTripAndOldFilesDefaultToAutoFit()
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var old = new StringReader("<Profile />");
        var legacy = (AppSettingsDTO)serializer.Deserialize(old);
        Assert.IsTrue(legacy.OutputSlotsAutoFit);
        Assert.AreEqual(0, legacy.OutputSlotsColumnWidths.Length);
        foreach (bool automatic in new[] { true, false })
        {
            double[] widths = [70, 480, 170, 180, 120];
            var dto = new AppSettingsDTO();
            dto.MapFrom(new BackingStore { outputSlotsAutoFit = automatic, outputSlotsColumnWidths = widths });
            using var writer = new StringWriter();
            serializer.Serialize(writer, dto);
            using var reader = new StringReader(writer.ToString());
            var restored = (AppSettingsDTO)serializer.Deserialize(reader);
            var destination = new BackingStore();
            restored.MapTo(destination);
            Assert.AreEqual(automatic, destination.outputSlotsAutoFit);
            CollectionAssert.AreEqual(widths, destination.outputSlotsColumnWidths);
            destination.outputSlotsColumnWidths[0] = 90;
            Assert.AreEqual(70d, restored.OutputSlotsColumnWidths[0], "Settings copies must not share mutable width arrays.");
        }
    }

    [TestMethod]
    public void InvalidOrNarrowWidthsRecoverToAReadableLayout()
    {
        foreach (double[] invalid in new[] { Array.Empty<double>(), new[] { 10d }, new[] { 60d, double.NaN, 140, 140, 110 }, new[] { 60d, 220, 140, double.PositiveInfinity, 110 }, new[] { 60d, -1, 140, 140, 110 } })
            Assert.AreEqual(0, OutputSlotsColumnLayout.Normalize(invalid).Length);
        CollectionAssert.AreEqual(new double[] { 60, 220, 140, 140, 110 }, OutputSlotsColumnLayout.Normalize([1, 1, 1, 1, 1]));
    }

    [TestMethod]
    [DataRow("DarkTheme", 480d)]
    [DataRow("DefaultTheme", 480d)]
    [DataRow("DarkTheme", 980d)]
    [DataRow("DefaultTheme", 980d)]
    public void LongControllerNamesWrapAndManualWidthsSurviveWindowResizing(string theme, double width)
    {
        Exception failure = null;
        Action thread = () =>
        {
            try
            {
                Grid root = ProfileAdvancedLayoutTests.Load(theme, "OutputSlots");
                var table = (DataGrid)root.FindName("currentOutDevLV");
                string[] properties = ["SlotNumber", "InputSlotDisplayString", "CurrentType", "DesiredType"];
                for (int i = 0; i < properties.Length; i++) Assert.AreEqual(properties[i], ((Binding)((DataGridTextColumn)table.Columns[i]).Binding).Path.Path);
                table.ItemsSource = Enumerable.Range(1, 8).Select(i => new { SlotNumber = i,
                    InputSlotDisplayString = i == 1 ? "1 (DualSense Edge wireless controller [BC:C7:46:60:57:DE])" : "",
                    CurrentType = i == 1 ? "DualSense Edge" : "Empty", DesiredType = "Dynamic", BoundInput = i == 1 }).ToArray();
                Layout(root, width);
                Assert.AreEqual(8, table.Items.Count);
                Assert.IsFalse(table.CanUserSortColumns, "Row order must continue to match output-manager slot indices.");
                table.SelectedIndex = 0;
                Assert.AreEqual(0, table.SelectedIndex);
                double[] manual = OutputSlotsColumnLayout.Normalize([70, 300, 160, 170, 120]);
                for (int i = 0; i < manual.Length; i++) table.Columns[i].Width = new DataGridLength(manual[i]);
                Layout(root, width + 200);
                for (int i = 0; i < manual.Length; i++) Assert.AreEqual(manual[i], table.Columns[i].Width.Value);
                Layout(root, width);
                Assert.IsTrue(Visuals(table).OfType<TextBlock>().Any(t => t.Text.Contains("BC:C7") && t.TextWrapping == TextWrapping.Wrap));
                string directory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
                if (!string.IsNullOrEmpty(directory))
                {
                    var bitmap = new RenderTargetBitmap((int)width, 740, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(directory, $"OutputSlots-{theme}-{width}.png")); encoder.Save(file);
                }
            }
            catch (Exception error) { failure = error; }
        };
HeadlessWpfDispatcher.Invoke(thread);         if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void RealColumnResizeAndAutoFitToggleUpdateTheRememberedLayoutWithoutDiskWrites()
    {
        Exception failure = null;
        HeadlessWpfDispatcher.Invoke(() =>
        {
            bool previousAutomatic = Global.OutputSlotsAutoFit;
            double[] previousWidths = Global.OutputSlotsColumnWidths;
            OutputSlotManagerControl control = null;
            DispatcherTimer timer = null;
            try
            {
                Global.OutputSlotsAutoFit = false;
                Global.OutputSlotsColumnWidths = [70, 300, 160, 170, 120];
                control = new OutputSlotManagerControl();
                control.Resources.MergedDictionaries.Add(ProfileAdvancedLayoutTests.Load("DarkTheme", "OutputSlots").Resources);
                timer = (DispatcherTimer)typeof(OutputSlotManagerControl).GetField("layoutSaveTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(control);
                // Exercise actual controls and events, but never run the app-settings disk writer.
                timer.Interval = TimeSpan.FromDays(1);
                var root = (Grid)control.Content;
                root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Layout(control, 980);
                var table = (DataGrid)control.FindName("currentOutDevLV");
                var automatic = (CheckBox)control.FindName("autoFitCheck");
                Assert.AreEqual(300d, table.Columns[1].Width.Value);
                table.Columns[1].Width = new DataGridLength(375);
                var header = Visuals(table).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().Single(h => h.Column == table.Columns[1]);
                var grip = Visuals(header).OfType<System.Windows.Controls.Primitives.Thumb>().Single(t => t.Name == "PART_RightHeaderGripper");
                grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(75, 0, false));
                Assert.IsFalse(Global.OutputSlotsAutoFit);
                Assert.AreEqual(375d, Global.OutputSlotsColumnWidths[1]);
                Layout(control, 800);
                Assert.AreEqual(375d, table.Columns[1].Width.Value);
                automatic.IsChecked = true;
                Assert.IsTrue(Global.OutputSlotsAutoFit);
                Assert.IsTrue(table.Columns[1].Width.IsStar);
                automatic.IsChecked = false;
                Assert.AreEqual(375d, table.Columns[1].Width.Value, "Leaving auto-fit must restore the saved manual widths.");
                automatic.IsChecked = true;
                Layout(control, 980);
                grip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
                grip.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-40, 0));
                Layout(control, 980);
                grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(-40, 0, false));
                Assert.IsFalse(Global.OutputSlotsAutoFit, "A native drag of the flexible column must switch to a remembered manual layout.");
                Assert.IsTrue(table.Columns[1].Width.IsAbsolute);
                Assert.AreEqual(table.Columns[1].Width.Value, Global.OutputSlotsColumnWidths[1]);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                timer?.Stop();
                if (control != null) ((Grid)control.Content).RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Global.OutputSlotsAutoFit = previousAutomatic;
                Global.OutputSlotsColumnWidths = previousWidths;
            }
        });
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    [DataRow("DarkTheme", 1330, false)]
    [DataRow("DefaultTheme", 1160, false)]
    [DataRow("DefaultTheme", 650, true)]
    public void FirstTabVisitWaitsUntilTheInputColumnFillsTheViewport(string theme, int finalWidth, bool expectsOverflow)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            bool previousAutomatic = Global.OutputSlotsAutoFit;
            double[] previousWidths = Global.OutputSlotsColumnWidths;
            Global.OutputSlotsAutoFit = true;
            Global.OutputSlotsColumnWidths = [];
            try
            {
                var control = new OutputSlotManagerControl();
                var tabs = new TabControl { Width = 1100 };
                var shell = new ResourceDictionary
                    { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) };
                tabs.Resources.MergedDictionaries.Add(ProfileAdvancedLayoutTests.Load(theme, "OutputSlots").Resources);
                tabs.Style = (Style)shell["BridgeMainTabControlStyle"];
                tabs.Items.Add(new TabItem { Header = "Overview", Content = new Grid() });
                tabs.Items.Add(new TabItem { Header = "Output Slots", Content = control });
                tabs.SelectedIndex = 0;
                using var source = new HwndSource(new HwndSourceParameters("Output Slots first visit")
                    { Width = 1400, Height = 800, WindowStyle = unchecked((int)0x80000000) });
                source.RootVisual = tabs;
                tabs.UpdateLayout();

                var table = (DataGrid)control.FindName("currentOutDevLV");
                table.ItemsSource = Enumerable.Range(1, 8).Select(i => new
                {
                    SlotNumber = i, InputSlotDisplayString = "",
                    CurrentType = "Empty", DesiredType = "Dynamic", BoundInput = false,
                }).ToArray();
                // The themed ContentPresenter attaches the page first. Exercise
                // a further viewport layout queued at WPF's own viewport priority.
                table.Loaded += (_, _) => tabs.Dispatcher.InvokeAsync(() =>
                {
                    // Hold the input column at its provisional startup width for one dispatcher cycle.
                    table.Columns[1].Width = new DataGridLength(table.Columns[1].MinWidth);
                    tabs.Width = finalWidth;
                    tabs.UpdateLayout();
                }, DispatcherPriority.Loaded);

                var opacity = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty, table.GetType());
                double[] firstVisibleWidths = null;
                EventHandler capture = (_, _) =>
                {
                    if (table.Opacity == 1) firstVisibleWidths ??= table.Columns.Select(column => column.ActualWidth).ToArray();
                };
                opacity.AddValueChanged(table, capture);
                try
                {
                    tabs.SelectedIndex = 1;
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.AreEqual(table.Columns[1].MinWidth, table.Columns[1].ActualWidth, 0.5);
                    var scrollViewer = UtilMethods.FindVisualChild<ScrollViewer>(table);
                    double columnsWidth = table.Columns.Sum(column => column.ActualWidth);
                    double viewportWidth = scrollViewer.ViewportWidth - table.CellsPanelHorizontalOffset;
                    Assert.AreEqual(expectsOverflow, columnsWidth > viewportWidth,
                        $"Provisional column widths total {columnsWidth} with a viewport of {viewportWidth}.");
                    Assert.AreEqual(expectsOverflow ? 1d : 0d, table.Opacity,
                        $"Unexpected first-visit table visibility at a viewport width of {viewportWidth}.");

                    table.Columns[1].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    tabs.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.IsNotNull(firstVisibleWidths);
                    Assert.AreEqual(expectsOverflow, firstVisibleWidths[1] <= table.Columns[1].MinWidth + 0.5);
                    for (int i = 0; i < firstVisibleWidths.Length; i++)
                        Assert.AreEqual(table.Columns[i].ActualWidth, firstVisibleWidths[i], 0.5,
                            $"Column {i} changed after the table first became visible.");
                }
                finally { opacity.RemoveValueChanged(table, capture); }
            }
            finally
            {
                Global.OutputSlotsAutoFit = previousAutomatic;
                Global.OutputSlotsColumnWidths = previousWidths;
            }
        });
    }

    [TestMethod]
    [DataRow("DarkTheme", true)]
    [DataRow("DefaultTheme", true)]
    [DataRow("DefaultTheme", false)]
    public void TableFirstBecomesVisibleWithSettledColumnWidths(string theme, bool automatic)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            bool previousAutomatic = Global.OutputSlotsAutoFit;
            double[] previousWidths = Global.OutputSlotsColumnWidths;
            try
            {
                Global.OutputSlotsAutoFit = automatic;
                Global.OutputSlotsColumnWidths = [70, 300, 160, 170, 120];
                var control = new OutputSlotManagerControl();
                control.Resources.MergedDictionaries.Add(ProfileAdvancedLayoutTests.Load(theme, "OutputSlots").Resources);
                var root = (Grid)control.Content;
                var table = (DataGrid)control.FindName("currentOutDevLV");
                table.ItemsSource = new[] { new { SlotNumber = 1, InputSlotDisplayString = "1 (DualSense)",
                    CurrentType = "DualSense", DesiredType = "Dynamic", BoundInput = true } };
                root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                control.Measure(new Size(1100, 740));
                control.Arrange(new Rect(0, 0, 1100, 740));
                control.UpdateLayout();
                Assert.AreEqual(0d, table.Opacity, "The first layout pass must keep provisional column widths out of view.");
                root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.AreEqual(0d, table.Opacity, "Leaving the page cancels its pending reveal.");

                var opacity = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty, table.GetType());
                double firstVisibleWidth = 0;
                EventHandler capture = (_, _) => firstVisibleWidth = table.Columns[1].ActualWidth;
                opacity.AddValueChanged(table, capture);
                try
                {
                    root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    Layout(control, 1100);
                    Assert.AreEqual(1d, table.Opacity);
                    Assert.AreEqual(table.Columns[1].ActualWidth, firstVisibleWidth, 0.5);
                    if (automatic)
                    {
                        Assert.IsTrue(firstVisibleWidth > 400);
                        Assert.AreEqual(ScrollBarVisibility.Disabled, table.HorizontalScrollBarVisibility);
                    }
                    else Assert.AreEqual(300d, firstVisibleWidth, 0.5);

                    root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    Assert.AreEqual(1d, table.Opacity, "Revisiting the settled table should not hide it again.");
                }
                finally
                {
                    opacity.RemoveValueChanged(table, capture);
                    root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                }
            }
            finally
            {
                Global.OutputSlotsAutoFit = previousAutomatic;
                Global.OutputSlotsColumnWidths = previousWidths;
            }
        });
    }

    [TestMethod]
    [DataRow("DarkTheme")]
    [DataRow("DefaultTheme")]
    public void AutoFitResizeDoesNotFlashScrollbarAndNarrowOrManualLayoutsRemainScrollable(string theme)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            bool previousAutomatic = Global.OutputSlotsAutoFit;
            double[] previousWidths = Global.OutputSlotsColumnWidths;
            var control = new OutputSlotManagerControl();
            var timer = (DispatcherTimer)typeof(OutputSlotManagerControl).GetField("layoutSaveTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(control);
            timer.Interval = TimeSpan.FromDays(1);
            try
            {
                Global.OutputSlotsAutoFit = true;
                Global.OutputSlotsColumnWidths = [70, 300, 160, 170, 120];
                control.Resources.MergedDictionaries.Add(ProfileAdvancedLayoutTests.Load(theme, "OutputSlots").Resources);
                ((Grid)control.Content).RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var table = (DataGrid)control.FindName("currentOutDevLV");
                table.ItemsSource = Enumerable.Range(1, 8).Select(i => new { SlotNumber = i,
                    InputSlotDisplayString = i == 1 ? "1 (DualSense [BC:C7:46:60:57:DE])" : "",
                    CurrentType = i == 1 ? "Xbox 360" : "Empty", DesiredType = "Dynamic", BoundInput = i == 1 }).ToArray();
                Layout(control, 1100);
                var scrollbar = Visuals(table).OfType<System.Windows.Controls.Primitives.ScrollBar>()
                    .Single(bar => bar.Orientation == Orientation.Horizontal);
                var visibilityDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                    UIElement.VisibilityProperty, typeof(System.Windows.Controls.Primitives.ScrollBar));
                int flashes = 0;
                EventHandler countVisible = (_, _) => { if (scrollbar.Visibility == Visibility.Visible) flashes++; };
                visibilityDescriptor.AddValueChanged(scrollbar, countVisible);
                for (double width = 1100; width >= 760; width -= 2)
                {
                    Layout(control, width);
                    Assert.AreEqual(ScrollBarVisibility.Disabled, table.HorizontalScrollBarVisibility);
                    Assert.AreNotEqual(Visibility.Visible, scrollbar.Visibility);
                }
                Assert.AreEqual(0, flashes, "A fitting auto-fit table must never briefly reveal the horizontal bar during a shrink.");
                visibilityDescriptor.RemoveValueChanged(scrollbar, countVisible);
                Layout(control, 480);
                Assert.AreEqual(ScrollBarVisibility.Auto, table.HorizontalScrollBarVisibility);
                Assert.AreEqual(Visibility.Visible, scrollbar.Visibility, "Minimum column widths still require horizontal scrolling in a narrow window.");
                Layout(control, 980);
                Assert.AreNotEqual(Visibility.Visible, scrollbar.Visibility);
                ((CheckBox)control.FindName("autoFitCheck")).IsChecked = false;
                Layout(control, 760);
                Assert.AreEqual(ScrollBarVisibility.Auto, table.HorizontalScrollBarVisibility);
                Assert.AreEqual(Visibility.Visible, scrollbar.Visibility, "Remembered manual widths must remain reachable.");
            }
            finally
            {
                timer.Stop();
                ((Grid)control.Content).RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Global.OutputSlotsAutoFit = previousAutomatic;
                Global.OutputSlotsColumnWidths = previousWidths;
            }
        });
    }

    private static void Layout(FrameworkElement root, double width)
    {
        root.Measure(new Size(width, 740)); root.Arrange(new Rect(0, 0, width, 740)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); root.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (DependencyObject child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
