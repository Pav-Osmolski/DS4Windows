using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class AxisConfigScrollingTests
{
    [TestMethod]
    [DataRow("DarkTheme", 480d)]
    [DataRow("DefaultTheme", 980d)]
    public void EveryAxisCategoryHasABoundedViewportAndCanReachItsEnd(string theme, double width)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            Grid root = ProfileAdvancedLayoutTests.Load(theme, "AxisConfig");
            foreach (TabControl modes in Logical(root).OfType<TabControl>())
            {
                TabItem controls = modes.Items.OfType<TabItem>()
                    .FirstOrDefault(item => (string)item.Header == "Controls");
                if (controls != null) modes.SelectedItem = controls;
            }
            foreach (Expander expander in Logical(root).OfType<Expander>()) expander.IsExpanded = true;
            foreach (RadioButton category in Logical(root).OfType<RadioButton>()
                .Where(button => button.GroupName == "ProfileAxisCategory"))
            {
                category.IsChecked = true;
                Layout(root, width, 360);
                ScrollViewer page = Visuals(root).OfType<ScrollViewer>()
                    .Single(view => view.Visibility == Visibility.Visible &&
                        System.Windows.Data.BindingOperations.GetBinding(view, UIElement.VisibilityProperty) != null);
                Assert.IsTrue(page.ViewportHeight > 0 && page.ViewportHeight < 360,
                    $"{category.Content}: the category header must leave a finite viewport.");
                Assert.IsTrue(page.ScrollableHeight > 0,
                    $"{category.Content}: overflowing settings must scroll instead of extending below the page.");
                page.ScrollToEnd();
                Layout(root, width, 360);
                Assert.IsTrue(page.VerticalOffset > 0);
                Assert.AreEqual(page.ScrollableHeight, page.VerticalOffset, 1d,
                    $"{category.Content}: the last settings must be reachable.");
            }
        });
    }

    [TestMethod]
    [DataRow("DarkTheme", 480d, 1, 2)]
    [DataRow("DarkTheme", 980d, 0, 1)]
    [DataRow("DarkTheme", 860d, 0, 1)]
    [DataRow("DefaultTheme", 480d, 1, 2)]
    [DataRow("DefaultTheme", 980d, 0, 1)]
    [DataRow("DefaultTheme", 860d, 0, 1)]
    public void DockingReadingsKeepsSettingsReadableAndReturnsTheSameLiveView(string theme, double width, int row, int span)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            Grid root = ProfileAdvancedLayoutTests.Load(theme, "AxisConfig");
            var area = (Grid)root.FindName("axisContentArea");
            var dock = (ContentControl)root.FindName("axisReadingsHost");
            Grid readings = ProfileAdvancedLayoutTests.Load(theme, "ControllerReadings");
            var page = new TabItem { Content = readings };
            ProfileEditor.MoveControllerReadings(page, dock, readings, true);
            ProfileEditor.ArrangeAxisReadingsDock(dock, true, width);
            Layout(root, width, 620);

            Assert.AreSame(readings, dock.Content);
            Assert.IsNull(page.Content);
            Assert.AreEqual(row, Grid.GetRow(dock));
            Assert.AreEqual(span, Grid.GetColumnSpan(dock));
            Assert.IsTrue(dock.ActualHeight > 0 && dock.ActualHeight <= 620);
            Assert.IsTrue(dock.ActualWidth >= 300 && dock.ActualWidth <= width);
            Grid settings = area.Children.OfType<Grid>().Single();
            Assert.IsTrue(settings.ActualHeight > 200 && settings.ActualWidth >= 480,
                "Readings must not squeeze the axis controls out of view.");
            foreach (Border card in Visuals(readings).OfType<Border>().Where(card => card.Width == 292))
            {
                var cards = (WrapPanel)LogicalTreeHelper.GetParent(card);
                Point position = card.TranslatePoint(new Point(), cards);
                Assert.IsTrue(position.X + card.ActualWidth + card.Margin.Right <= cards.ActualWidth + 1,
                    "Docked readings cards must fit inside the actual scroll viewport, including its gutter.");
            }
            string directory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
            if (!string.IsNullOrEmpty(directory))
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, 620, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = System.IO.File.Create(System.IO.Path.Combine(directory, $"AxisDock-{theme}-{width}.png"));
                encoder.Save(file);
            }

            ProfileEditor.MoveControllerReadings(page, dock, readings, false);
            ProfileEditor.ArrangeAxisReadingsDock(dock, false, width);
            Layout(root, width, 620);
            Assert.AreSame(readings, page.Content, "Return the same view rather than creating a second polling owner.");
            Assert.IsNull(dock.Content);
            Assert.AreEqual(Visibility.Collapsed, dock.Visibility);
            Assert.AreEqual(width, settings.ActualWidth, 1d);
        });
    }

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject element)
                foreach (DependencyObject descendant in Logical(element)) yield return descendant;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (DependencyObject descendant in Visuals(VisualTreeHelper.GetChild(root, i))) yield return descendant;
    }
}
