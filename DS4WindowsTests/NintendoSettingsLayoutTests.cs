using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DS4WindowsTests;

[TestClass]
public class NintendoSettingsLayoutTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow("DarkTheme", 480d)]
    [DataRow("DefaultTheme", 480d)]
    [DataRow("DarkTheme", 980d)]
    [DataRow("DefaultTheme", 980d)]
    public void NintendoPagesKeepPopulatedControlsReachable(string theme, double width)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Grid setup = ProfileAdvancedLayoutTests.Load(theme, "NintendoSetup");
                var candidates = (ListBox)setup.FindName("switch2AssociationCandidatesList");
                candidates.Items.Add(new Choice { Label = "Joy-Con 2 (Left) â€” already associated; reconnect available" });
                candidates.Items.Add(new Choice { Label = "Switch 2 Pro Controller" });
                Layout(setup, width, 840);
                FrameworkElement content = (FrameworkElement)setup.FindName("switch2SetupContent");
                Assert.AreEqual(content.TranslatePoint(new Point(), setup).X,
                    candidates.TranslatePoint(new Point(), setup).X, 1d, "Discovery results must align with the section's left edge.");
                var join = (Button)setup.FindName("switch2CreateJoyConPairBtn");
                var right = (ComboBox)setup.FindName("switch2JoyConRightCandidates");
                double pairGap = join.TranslatePoint(new Point(), setup).Y - right.TranslatePoint(new Point(0, right.ActualHeight), setup).Y;
                Assert.IsTrue(pairGap >= 12 && pairGap <= 18, $"Pairing button gap must be 12–18px, got {pairGap}.");
                CheckWidth(setup, width);
                Save(setup, theme, width, "Nintendo-setup");

                Grid editor = ProfileAdvancedLayoutTests.Load(theme, "Nintendo Options");
                foreach (Expander expander in Logical(editor).OfType<Expander>()) expander.IsExpanded = true;
                string[] labels = ["X / West", "Y / North", "A / South", "B / East", "Minus / Back", "Plus / Start", "Home", "Capture", "Left Stick", "Right Stick", "L", "R", "ZL", "ZR", "Down", "Up", "Right", "Left", "C / Chat", "GL 1", "GL 2", "GR 1", "GR 2", "Left SL (rail)", "Left SR (rail)", "Right SL (rail)", "Right SR (rail)", "Left IR Sensor", "Right IR Sensor"];
                foreach (ItemsControl choices in Logical(editor).OfType<ItemsControl>().Where(i => i is not ComboBox))
                    choices.ItemsSource = labels.Select(label => new Choice { Label = label }).ToArray();
                foreach (ComboBox combo in Logical(editor).OfType<ComboBox>().Where(c => c.Items.Count > 0)) combo.SelectedIndex = 0;
                Layout(editor, width, 840);
                CheckWidth(editor, width);
                var dynamicFeel = (CheckBox)editor.FindName("Switch2XboxImpulseDynamicFrequencyCheckBox");
                var feedback = (Expander)editor.FindName("switch2FeedbackCard");
                var fixedFeel = Logical(feedback).OfType<TextBlock>().Single(t => t.Text == "Fixed rumble feel:");
                var fixedRow = (WrapPanel)LogicalTreeHelper.GetParent(fixedFeel);
                dynamicFeel.IsChecked = true;
                Layout(editor, width, 840);
                Assert.AreEqual(Visibility.Collapsed, fixedRow.Visibility, "Unused rumble options must not reserve space.");
                dynamicFeel.IsChecked = false;
                Layout(editor, width, 840);
                Assert.AreEqual(Visibility.Visible, fixedRow.Visibility, "Fixed rumble tuning must return when selected.");
                TextBlock title = Logical(editor).OfType<TextBlock>().Single(t => t.Text == "Mouse on a desk");
                Assert.AreEqual(((SolidColorBrush)editor.FindResource("ForegroundColor")).Color,
                    ((SolidColorBrush)title.Foreground).Color, "Section headings must remain readable in either theme.");
                var layer = (Expander)editor.FindName("switch2ButtonLayerCard");
                var scope = Logical(layer).OfType<ComboBox>().First();
                scope.ItemsSource = new[] { "Standard / mouse aiming", "Stick aiming", "Motion steering" };
                scope.SelectedIndex = 0;
                var action = (ComboBox)editor.FindName("Switch2SecondActionControlCombo");
                action.Items.Add(new { ControlName = "Right Stick" }); action.SelectedIndex = 0;
                var picker = Logical(layer).OfType<ItemsControl>().First(i => i is not ComboBox);
                var first = (Choice)picker.Items[0];
                var checkbox = Visuals(picker).OfType<CheckBox>().First();
                checkbox.IsChecked = true;
                Assert.IsTrue(first.IsSelected, "Button tiles must remain editable through their original two-way binding.");
                layer.IsExpanded = false;
                layer.IsExpanded = true;
                Layout(editor, width, 840);
                Assert.IsTrue(first.IsSelected, "Collapsing a section must preserve unsaved selections.");
                // Render all cards after taking the page out of the viewport.
                // ScrollViewer intentionally clips offscreen descendants, which
                // would make individual card captures blank or incomplete.
                var panel = (StackPanel)editor.FindName("switch2SettingsPanel");
                ((Panel)LogicalTreeHelper.GetParent(panel)).Children.Remove(panel);
                editor.Children.Clear(); editor.Children.Add(panel);
                editor.Measure(new Size(width, double.PositiveInfinity));
                Layout(editor, width, editor.DesiredSize.Height);
                foreach (string name in new[] { "switch2JoyConCard", "switch2MotionCard", "switch2PairCard", "switch2FeedbackCard", "switch2UsbHeadsetCard", "switch2ButtonLayerCard", "switch2StickCard", "switch2ConnectionCard", "switch2CalibrationCard" })
                {
                    var card = (Expander)editor.FindName(name);
                    DependencyObject parent = LogicalTreeHelper.GetParent(card);
                    while (parent != null && parent is not Border) parent = LogicalTreeHelper.GetParent(parent);
                    var border = parent as Border;
                    if (border == null) continue;
                    panel.Children.Remove(border);
                    var preview = new Grid { Background = editor.Background };
                    preview.Resources.MergedDictionaries.Add(editor.Resources);
                    preview.Resources.MergedDictionaries.Add(panel.Resources);
                    preview.Children.Add(border);
                    preview.Measure(new Size(width, double.PositiveInfinity));
                    Layout(preview, width, preview.DesiredSize.Height);
                    CheckSpacing(preview);
                    Save(preview, theme, width, name);
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public sealed class Choice
    {
        public string Label { get; set; }
        public bool IsSelected { get; set; }
    }

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); root.UpdateLayout();
    }

    private static void CheckWidth(FrameworkElement root, double width)
    {
        foreach (Control control in Visuals(root).OfType<Control>().Where(c => c.IsVisible && c.ActualWidth > 0 && c is not ScrollBar))
        {
            Point at = control.TranslatePoint(new Point(), root);
            Assert.IsTrue(at.X >= -2 && at.X + control.ActualWidth <= width + 2,
                $"{control.GetType().Name} {control.Name} extends beyond the {width}px page: {at.X}, {control.ActualWidth}.");
        }
    }

    private static void CheckSpacing(FrameworkElement root)
    {
        foreach (StackPanel stack in Logical(root).OfType<StackPanel>().Where(s => s.Orientation == Orientation.Vertical))
        {
            var groups = stack.Children.OfType<FrameworkElement>().Select(child =>
                Visuals(child).OfType<FrameworkElement>().Where(e => e.IsVisible && e.ActualHeight > 0 &&
                    (e is TextBlock or Image or CheckBox or Button or ComboBox || e.GetType().Name.EndsWith("UpDown")))
                .Select(e => new Rect(e.TranslatePoint(new Point(), root), e.RenderSize)).ToArray())
                .Where(rects => rects.Length > 0).ToArray();
            for (int i = 1; i < groups.Length; i++)
            {
                double gap = groups[i].Min(r => r.Top) - groups[i - 1].Max(r => r.Bottom);
                Assert.IsTrue(gap <= 24.5, $"Nintendo vertical content gap exceeds 24px: {gap}, in {stack.Name}.");
                var preceding = stack.Children.OfType<FrameworkElement>().Where(c => c.IsVisible && c.ActualHeight > 0).ToArray();
                if (preceding.Length == groups.Length && preceding[i - 1] is TextBlock &&
                    (preceding[i] is Control || Visuals(preceding[i]).OfType<Control>().Any()))
                    Assert.IsTrue(gap <= 18.5, $"Text-to-control gap exceeds 18px: {gap}, after {((TextBlock)preceding[i - 1]).Text}.");
            }
        }
    }

    private void Save(FrameworkElement element, string theme, double width, string name)
    {
        string directory = Environment.GetEnvironmentVariable("DS4W_NINTENDO_LAYOUT_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)), height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(directory, $"{name}-{theme}-{width}.png");
        using (var file = File.Create(path)) encoder.Save(file);
        TestContext?.AddResultFile(path);
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependency)
                foreach (var item in Logical(dependency)) yield return item;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
