using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DS4WindowsTests;

[TestClass]
public sealed class ProfileSettingsPagesLayoutTests
{
    [TestMethod]
    [DataRow("AxisConfig", "DarkTheme", 480d)]
    [DataRow("AxisConfig", "DefaultTheme", 480d)]
    [DataRow("AxisConfig", "DarkTheme", 980d)]
    [DataRow("AxisConfig", "DefaultTheme", 980d)]
    [DataRow("Lightbar", "DarkTheme", 480d)]
    [DataRow("Lightbar", "DefaultTheme", 480d)]
    [DataRow("Lightbar", "DarkTheme", 980d)]
    [DataRow("Lightbar", "DefaultTheme", 980d)]
    [DataRow("Touchpad", "DarkTheme", 480d)]
    [DataRow("Touchpad", "DefaultTheme", 480d)]
    [DataRow("Touchpad", "DarkTheme", 980d)]
    [DataRow("Touchpad", "DefaultTheme", 980d)]
    [DataRow("Gyro", "DarkTheme", 480d)]
    [DataRow("Gyro", "DefaultTheme", 480d)]
    [DataRow("Gyro", "DarkTheme", 980d)]
    [DataRow("Gyro", "DefaultTheme", 980d)]
    public void EveryModeRemainsReachableAndPreservesUnsavedEdits(string page, string theme, double width)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Grid root = ProfileAdvancedLayoutTests.Load(theme, page);
                var all = Logical(root).ToArray();
                CheckBox edit = all.OfType<CheckBox>().FirstOrDefault();
                if (edit != null) edit.IsChecked = true;
                RadioButton[] navigation = all.OfType<RadioButton>().Where(r => r.GroupName == "ProfileAxisCategory").ToArray();
                foreach (RadioButton category in navigation) category.IsChecked = true;
                TabControl[] modes = all.OfType<TabControl>().ToArray();
                Assert.IsTrue(modes.Length > 0);
                int rendered = 0;
                foreach (TabControl mode in modes)
                {
                    for (int index = 0; index < mode.Items.Count; index++)
                    {
                        // Select ancestor modes/categories before a nested mode.
                        DependencyObject parent = LogicalTreeHelper.GetParent(mode);
                        while (parent != null)
                        {
                            if (parent is TabItem item && LogicalTreeHelper.GetParent(item) is TabControl owner) owner.SelectedItem = item;
                            if (parent is ScrollViewer scroll)
                            {
                                foreach (RadioButton category in navigation)
                                {
                                    var binding = System.Windows.Data.BindingOperations.GetBinding(scroll, UIElement.VisibilityProperty);
                                    if (binding?.ElementName == category.Name) category.IsChecked = true;
                                }
                            }
                            parent = LogicalTreeHelper.GetParent(parent);
                        }
                        mode.SelectedIndex = index;
                        Layout(root, width);
                        Assert.AreEqual(index, mode.SelectedIndex);
                        Assert.IsNotNull(mode.Template.FindName("SelectedModeContent", mode), "Mode content needs a presenter without hidden-header decoration.");
                        foreach (Expander expander in Logical((DependencyObject)((TabItem)mode.SelectedItem).Content).OfType<Expander>()) expander.IsExpanded = true;
                        Layout(root, width);
                        foreach (Control control in Visuals(root).OfType<Control>().Where(c => c.IsVisible && c.ActualWidth > 0))
                        {
                            Point point = control.TranslatePoint(new Point(), root);
                            Assert.IsTrue(point.X >= -2 && point.X + control.ActualWidth <= width + 2,
                                $"{page}/{theme}/{width}/mode {index}: {control.GetType().Name} {control.Name} overflows the page.");
                        }
                        if (edit != null) Assert.IsTrue(edit.IsChecked.Value, "Switching output pages must not recreate edited controls.");
                        string directory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                            var bitmap = new RenderTargetBitmap((int)width, 620, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(root);
                            var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(directory, $"{page}-{theme}-{width}-{rendered}.png"));encoder.Save(file);
                        }
                        rendered++;
                    }
                }
                Assert.IsTrue(rendered >= 2, "Exercise alternate output modes, not just the first page.");
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        if (root == null) yield break;
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependency)
                foreach (DependencyObject item in Logical(dependency)) yield return item;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Visuals(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Layout(FrameworkElement root, double width)
    {
        root.Measure(new Size(width, 620));root.Arrange(new Rect(0, 0, width, 620));root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);root.UpdateLayout();
    }
}
