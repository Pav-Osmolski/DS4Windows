using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class SpecialActionsLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme", 480d)]
    [DataRow("DefaultTheme", 480d)]
    [DataRow("DarkTheme", 980d)]
    [DataRow("DefaultTheme", 980d)]
    public void LongActionsWrapAndEnabledCheckboxRemainsEditable(string theme, double width)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            Grid root = ProfileAdvancedLayoutTests.Load(theme, "Special Actions");
            var table = (DataGrid)root.FindName("specialActionsLV");
            var item = new ActionRow();
            table.ItemsSource = new[] { item };
            table.SelectedIndex = 0;
            root.Measure(new Size(width, 620));
            root.Arrange(new Rect(0, 0, width, 620));
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            root.UpdateLayout();
            Assert.IsFalse(table.CanUserSortColumns, "Selection indices must match the original action collection.");
            Assert.IsFalse(table.CanUserAddRows);
            Assert.AreSame(item, table.SelectedItem);
            var enabled = Visuals(table).OfType<CheckBox>().Single();
            enabled.IsChecked = true;
            enabled.GetBindingExpression(CheckBox.IsCheckedProperty).UpdateSource();
            Assert.IsTrue(item.Active, "The readonly table must retain its editable per-profile activation checkbox.");
            foreach (string text in new[] { item.ActionName, item.Controls, item.TypeName })
            {
                var cell = Visuals(table).OfType<TextBlock>().Single(t => t.Text == text);
                Assert.AreEqual(TextWrapping.Wrap, cell.TextWrapping);
                Assert.IsTrue(cell.ActualHeight > 0);
                Assert.IsTrue(cell.ActualWidth <= table.ActualWidth);
            }
            foreach (var button in Visuals(root).OfType<Button>().Where(b => b.Name.EndsWith("ActionBtn")))
            {
                Assert.IsTrue(button.ActualHeight >= 34);
                var point = button.TranslatePoint(new Point(), root);
                Assert.IsTrue(point.X >= 0 && point.X + button.ActualWidth <= width + 1, "Toolbar must wrap at narrow widths.");
            }
            string directory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)width, 620, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, $"SpecialActions-{theme}-{width}.png")); encoder.Save(file);
            }
        });
    }

    public class ActionRow
    {
        public bool Active { get; set; }
        public string ActionName => "Switch to the desktop navigation profile while holding the controller shortcut";
        public string Controls => "PS, Options, Left shoulder, Right shoulder";
        public string TypeName => "Load Profile";
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
}
