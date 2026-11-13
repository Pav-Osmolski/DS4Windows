using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xceed.Wpf.Toolkit;

namespace DS4WindowsTests;

[TestClass]
public class AxisConfigAlignmentTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow("DefaultTheme")]
    [DataRow("DarkTheme")]
    public void StickFieldsAlignAndUseEqualRowGapsWithTrailingCheckboxes(string theme)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            Grid root = ProfileAdvancedLayoutTests.Load(theme, "AxisConfig");
            root.Resources["spaceMargin"] = new Thickness(0, 0, 0, 10);
            foreach (string name in new[] { "profileAxis0Nav", "profileAxis1Nav" })
            {
                ((RadioButton)root.FindName(name)).IsChecked = true;
                Layout(root);
                var categories = ((Grid)root.FindName("axisContentArea")).Children.OfType<Grid>().Single();
                var category = categories.Children.OfType<ScrollViewer>().Single(v => v.Visibility == Visibility.Visible);
                foreach (TabControl tab in Visuals(category).OfType<TabControl>().Where(t => t.Items.Count == 3).ToArray())
                    tab.SelectedIndex = 1;
                Layout(root);
                var cards = Visuals(category).OfType<Border>().Where(b => b.Child is StackPanel).ToArray();
                Border Card(string title) => cards.Single(b => ((StackPanel)b.Child).Children.OfType<TextBlock>().Any(t => t.Text == title));
                var dead = Card("Dead zones and sensitivity");
                var deadScope = (StackPanel)dead.Child;
                deadScope.Children.OfType<TabControl>().Single().SelectedIndex = 0;
                Layout(root);
                var selector = deadScope.Children.OfType<Grid>().Single().Children.OfType<ComboBox>().Single();
                var radial = (Grid)((TabItem)deadScope.Children.OfType<TabControl>().Single().Items[0]).Content;
                var fields = radial.Children.OfType<DoubleUpDown>().OrderBy(Grid.GetRow).ToArray();
                double x = selector.TranslatePoint(new Point(), dead).X;
                foreach (Control field in fields)
                    Assert.AreEqual(x, field.TranslatePoint(new Point(), dead).X, 0.1, "Radial fields must start at the selector's left edge.");
                var allRows = radial.Children.OfType<FrameworkElement>().Where(c => Grid.GetColumn(c) == 1)
                    .OrderBy(Grid.GetRow).ToArray();
                double[] top = allRows.Select(c => c.TranslatePoint(new Point(), radial).Y).ToArray();
                for (int i = 2; i < top.Length; i++)
                    Assert.AreEqual(top[1] - top[0], top[i] - top[i - 1], 0.1, "Dead-zone row spacing must remain equal.");

                var shape = Card("Response curve and shaping");
                Grid shapeGrid = ((StackPanel)shape.Child).Children.OfType<Grid>().Single();
                var rows = shapeGrid.Children.OfType<FrameworkElement>().Where(c => Grid.GetColumn(c) == 1 && Grid.GetRow(c) >= 2)
                    .OrderBy(Grid.GetRow).ToArray();
                top = rows.Select(c => c.TranslatePoint(new Point(), shapeGrid).Y).ToArray();
                for (int i = 2; i < top.Length; i++)
                    Assert.AreEqual(top[1] - top[0], top[i] - top[i - 1], 0.1, "Shape rows must use the standard equal spacing.");
                foreach (var row in rows.OfType<StackPanel>().Where(p => p.Children.OfType<CheckBox>().Any()))
                {
                    var field = row.Children.OfType<DoubleUpDown>().Single();
                    var check = row.Children.OfType<CheckBox>().Single();
                    Assert.IsTrue(check.TranslatePoint(new Point(), row).X >= field.TranslatePoint(new Point(), row).X + field.ActualWidth + 7);
                }
                var delta = Card("Delta Acceleration");
                var toggle = Visuals(delta).OfType<CheckBox>().Single();
                Assert.IsTrue(toggle.TranslatePoint(new Point(), delta).X < 180, "Enabled must remain beside its label, not at the page's right edge.");
                Save(dead, $"axis-dead-{theme}-{name}.png");
                Save(shape, $"axis-shape-{theme}-{name}.png");
            }
        });
    }

    [TestMethod]
    [DataRow("DefaultTheme")]
    [DataRow("DarkTheme")]
    [DataRow("Graphite")]
    [DataRow("Paper")]
    [DataRow("Midnight")]
    [DataRow("HighContrast")]
    public void PairedCurvesHaveGuttersAndReadingsCardsHaveMatchingWidths(string theme)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            string basis = theme == "DefaultTheme" || theme == "Paper" ? "DefaultTheme" : "DarkTheme";
            Grid root = ProfileAdvancedLayoutTests.Load(basis, "AxisConfig");
            if (theme is not "DefaultTheme" and not "DarkTheme")
                root.Resources.MergedDictionaries.Add(DS4WinWPF.DS4Forms.Themes.AppThemeCatalog.Create(
                    Enum.Parse<DS4Windows.AppThemeChoice>(theme), highContrast: false));
            root.Resources["spaceMargin"] = new Thickness(0, 0, 0, 10);
            foreach (string name in new[] { "profileAxis2Nav", "profileAxis3Nav" })
            {
                ((RadioButton)root.FindName(name)).IsChecked = true;
                Layout(root);
                var categories = ((Grid)root.FindName("axisContentArea")).Children.OfType<Grid>().Single();
                var category = categories.Children.OfType<ScrollViewer>().Single(v => v.Visibility == Visibility.Visible);
                var selector = Visuals(category).OfType<ComboBox>().Single(c => Grid.GetColumn(c) == 1);
                var pair = (Grid)selector.Parent;
                var right = pair.Children.OfType<ComboBox>().Single(c => Grid.GetColumn(c) == 2);
                Assert.IsTrue(right.TranslatePoint(new Point(), pair).X -
                    selector.TranslatePoint(new Point(selector.ActualWidth, 0), pair).X >= 11.9);
                var leftCurve = pair.Children.OfType<DockPanel>().Single(c => Grid.GetColumn(c) == 1);
                var rightCurve = pair.Children.OfType<DockPanel>().Single(c => Grid.GetColumn(c) == 2);
                Assert.IsTrue(rightCurve.TranslatePoint(new Point(), pair).X -
                    leftCurve.TranslatePoint(new Point(leftCurve.ActualWidth, 0), pair).X >= 11.9);
                Save(category, $"paired-curves-{theme}-{name}.png");
            }
            Grid readings = ProfileAdvancedLayoutTests.Load(basis, "ControllerReadings");
            if (theme is not "DefaultTheme" and not "DarkTheme")
                readings.Resources.MergedDictionaries.Add(DS4WinWPF.DS4Forms.Themes.AppThemeCatalog.Create(
                    Enum.Parse<DS4Windows.AppThemeChoice>(theme), highContrast: false));
            readings.Measure(new Size(360, 1200));
            readings.Arrange(new Rect(0, 0, 360, 1200));
            readings.UpdateLayout();
            var cards = Visuals(readings).OfType<Border>().Where(b => b.Child is StackPanel && b.Width == 292).ToArray();
            Assert.IsGreaterThan(3, cards.Length);
            var status = (Border)readings.FindName("controllerStatusCard");
            foreach (var card in cards)
                Assert.AreEqual(status.ActualWidth, card.ActualWidth, 0.1, "Controller status and plots must share their width in every theme.");
            Save(status, $"readings-status-{theme}.png");
        });
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(850, 1100));
        root.Arrange(new Rect(0, 0, 850, 1100));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
    }

    private void Save(FrameworkElement card, string name)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(card), null, new Rect(0, 0, card.ActualWidth, card.ActualHeight));
        var image = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        string path = Path.Combine(TestContext.TestResultsDirectory, name);
        using (var file = File.Create(path)) encoder.Save(file);
        TestContext.AddResultFile(path);
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
}
