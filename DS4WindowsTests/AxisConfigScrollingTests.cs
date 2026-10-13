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
    [DataRow(480d, 1, 2)]
    [DataRow(980d, 0, 1)]
    public void DockingReadingsKeepsSettingsReadableAndReturnsTheSameLiveView(double width, int row, int span)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            Grid root = ProfileAdvancedLayoutTests.Load("DarkTheme", "AxisConfig");
            var area = (Grid)root.FindName("axisContentArea");
            var dock = (ContentControl)root.FindName("axisReadingsHost");
            Grid readings = ProfileAdvancedLayoutTests.Load("DarkTheme", "ControllerReadings");
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
