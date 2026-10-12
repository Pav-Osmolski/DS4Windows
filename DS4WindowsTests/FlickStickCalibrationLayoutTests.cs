using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public class FlickStickCalibrationLayoutTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow("DefaultTheme", 360, 96)]
    [DataRow("DefaultTheme", 360, 144)]
    [DataRow("DefaultTheme", 640, 96)]
    [DataRow("DefaultTheme", 640, 144)]
    [DataRow("DarkTheme", 360, 96)]
    [DataRow("DarkTheme", 360, 144)]
    [DataRow("DarkTheme", 640, 96)]
    [DataRow("DarkTheme", 640, 144)]
    public void AxisCalibrationPanelsFitTheirSelectorsAndWrappedGuidanceOffscreen(
        string theme, int width, int dpi)
    {
        string renderedPath = null;
        RunSta(() =>
        {
            // Load the production Axis Config panels without constructing an
            // Application, ProfileEditor, Window, ControlService, or device.
            Border host = CreateProductionPanelHost(theme, new CalibrationLayoutModel());
            host.Measure(new Size(width, double.PositiveInfinity));
            int height = (int)Math.Ceiling(host.DesiredSize.Height);
            Assert.IsTrue(height > 160 && height <= 460,
                $"Calibration content should remain compact at {width} DIPs: {height}.");
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();
            Assert.IsNull(PresentationSource.FromVisual(host),
                "Rendering must not connect to a live window or desktop surface.");

            StackPanel[] panels = VisualDescendants<StackPanel>(host)
                .Where(panel => panel.Name.EndsWith("FlickCalibrationPanel", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(2, panels.Length);
            foreach (StackPanel panel in panels)
            {
                AssertInside(host, panel);
                ComboBox selector = VisualDescendants<ComboBox>(panel).Single();
                AssertInside(panel, selector);
                Assert.IsTrue(selector.ActualWidth >= 280 && selector.ActualHeight >= 28,
                    $"{selector.Name} needs space for a controller button label.");
                Assert.AreEqual(DS4Controls.Switch2JoyConRightPaddle1, selector.SelectedValue);
                TextBlock selectedLabel = VisualDescendants<TextBlock>(selector).Single(text =>
                    text.Text == CalibrationLayoutModel.LongButtonLabel);
                AssertInside(selector, selectedLabel);
                AssertUncroppedText(selectedLabel);

                Label label = VisualDescendants<Label>(panel).Single();
                Assert.AreEqual("360° test button", label.Content);
                AssertInside(panel, label);
                Assert.IsTrue(BoundsIn(panel, label).Bottom <= BoundsIn(panel, selector).Top);

                TextBlock[] guidance = panel.Children.OfType<TextBlock>().ToArray();
                Assert.AreEqual(2, guidance.Length);
                foreach (TextBlock text in guidance)
                {
                    AssertInside(panel, text);
                    Assert.AreEqual(TextWrapping.Wrap, text.TextWrapping);
                    Assert.AreEqual(TextTrimming.None, text.TextTrimming);
                    AssertUncroppedText(text);
                    Assert.IsTrue(BoundsIn(panel, text).Top >= BoundsIn(panel, selector).Bottom,
                        "Calibration guidance must not overlap the selector.");
                }
                Assert.IsTrue(BoundsIn(panel, guidance[0]).Bottom <= BoundsIn(panel, guidance[1]).Top);
            }
            Rect left = BoundsIn(host, panels[0]);
            Rect right = BoundsIn(host, panels[1]);
            Assert.IsTrue(left.Bottom <= right.Top,
                "The left and right calibration settings must remain separate.");

            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96d),
                (int)Math.Ceiling(height * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(host);
            // Match the existing artwork render tests' optional durable
            // evidence directory. Normal test runs do not write image files.
            string directory = Environment.GetEnvironmentVariable("DS4W_ARTWORK_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                renderedPath = Path.Combine(directory,
                    $"flick-calibration-axis-{theme}-{width}-{dpi}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(renderedPath)) encoder.Save(stream);
            }
        });
        if (renderedPath != null)
            TestContext.AddResultFile(renderedPath);
    }

    [TestMethod]
    [DataRow(true, 0)]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    [DataRow(false, 0)]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    public void EachAxisCalibrationSelectorIsEnabledOnlyForItsOwnFlickStickMode(bool left, int mode)
    {
        RunSta(() =>
        {
            var model = new CalibrationLayoutModel();
            if (left) model.LSOutputIndex = mode;
            else model.RSOutputIndex = mode;
            Border host = CreateProductionPanelHost("DefaultTheme", model);
            host.Measure(new Size(360, double.PositiveInfinity));
            host.Arrange(new Rect(new Point(), host.DesiredSize));
            host.UpdateLayout();
            ComboBox[] selectors = VisualDescendants<ComboBox>(host).ToArray();
            ComboBox selected = selectors.Single(combo => combo.Name.StartsWith(left ? "ls" : "rs"));
            ComboBox other = selectors.Single(combo => combo != selected);
            Assert.AreEqual(mode == (int)StickMode.FlickStick, selected.IsEnabled);
            Assert.IsTrue(other.IsEnabled, "The other stick keeps its independent Flick Stick mode.");
            if (selected.IsEnabled)
            {
                selected.SetCurrentValue(ComboBox.SelectedValueProperty, DS4Controls.None);
                selected.GetBindingExpression(ComboBox.SelectedValueProperty).UpdateSource();
                Assert.AreEqual(DS4Controls.None,
                    left ? model.LSFlickCalibrationTrigger : model.RSFlickCalibrationTrigger);
                Assert.AreEqual(DS4Controls.Switch2JoyConRightPaddle1,
                    left ? model.RSFlickCalibrationTrigger : model.LSFlickCalibrationTrigger);
            }
            Assert.IsNull(PresentationSource.FromVisual(host));
        });
    }

    [TestMethod]
    public void CalibrationSelectorsLiveBesideEachSticksCalibrationInAxisConfig()
    {
        XDocument document = XDocument.Load(FormPath("ProfileEditor.xaml"));
        foreach (string stick in new[] { "LS", "RS" })
        {
            XElement panel = CalibrationPanel(document, stick);
            Assert.AreEqual("4", (string)panel.Attribute("Grid.Row"));
            Assert.AreEqual("2", (string)panel.Attribute("Grid.ColumnSpan"));
            XElement grid = panel.Parent;
            Assert.AreEqual(Wpf + "Grid", grid.Name);
            XElement[] rows = grid.Element(Wpf + "Grid.RowDefinitions").Elements().ToArray();
            Assert.AreEqual(5, rows.Length);
            Assert.AreEqual("Auto", (string)rows[4].Attribute("Height"));
            Assert.IsTrue(grid.Descendants().Attributes("Value").Any(attribute =>
                attribute.Value.StartsWith("{Binding " + stick + "FlickRWC", StringComparison.Ordinal)));
            Assert.AreEqual("{lex:Loc FlickStick}",
                (string)panel.Ancestors(Wpf + "TabItem").First().Attribute("Header"));
            Assert.AreEqual("{lex:Loc AxisConfig}",
                (string)panel.Ancestors(Wpf + "TabItem").Last().Attribute("Header"));

            XElement selector = panel.Element(Wpf + "ComboBox");
            Assert.AreEqual("{Binding FlickCalibrationTriggerChoices}", (string)selector.Attribute("ItemsSource"));
            Assert.AreEqual("Label", (string)selector.Attribute("DisplayMemberPath"));
            Assert.AreEqual("Control", (string)selector.Attribute("SelectedValuePath"));
            Assert.AreEqual("{Binding " + stick + "FlickCalibrationTrigger}", (string)selector.Attribute("SelectedValue"));
            string guidance = string.Join(" ", panel.Elements(Wpf + "TextBlock")
                .Select(text => (string)text.Attribute("Text")));
            StringAssert.Contains(guidance, "Save the profile");
            StringAssert.Contains(guidance, "in-game");
            StringAssert.Contains(guidance, "Increase Real World Calibration");
            StringAssert.Contains(guidance, "decrease it");
            StringAssert.Contains(guidance, "Not assigned to release the button");
        }

        XDocument remapper = XDocument.Load(FormPath("BindingWindow.xaml"));
        Assert.IsFalse(remapper.Descendants().Attributes().Any(attribute =>
            attribute.Value.Contains("flickStickCalibr", StringComparison.OrdinalIgnoreCase)),
            "Calibration settings must not reappear as remapping actions.");
    }

    [TestMethod]
    public void AxisTriggerTuningKeepsItsNormalRowsAndLeavesAdaptiveEffectsInTriggerLab()
    {
        XDocument document = XDocument.Load(FormPath("ProfileEditor.xaml"));
        XElement triggers = document.Descendants(Wpf + "Grid").Single(element =>
            element.Elements().Any(control => (string)control.Attribute("Value") == "{Binding L2DeadZone}") &&
            element.Elements().Any(control => (string)control.Attribute("Value") == "{Binding R2DeadZone}"));
        Assert.AreEqual(8, triggers.Element(Wpf + "Grid.RowDefinitions").Elements().Count());
        Assert.IsTrue(triggers.Elements().Attributes("Grid.Row").All(attribute => int.Parse(attribute.Value) < 8));
        string markup = document.ToString();
        foreach (string stick in new[] { "L2", "R2" })
        {
            Assert.IsFalse(markup.Contains("{Binding " + stick + "TriggerEffect", StringComparison.Ordinal));
            foreach (string setting in new[] { "DeadZone", "MaxZone", "AntiDeadZone", "MaxOutput", "Sens", "OutputCurveIndex", "CustomCurve" })
                StringAssert.Contains(triggers.ToString(), "{Binding " + stick + setting);
        }
        Assert.IsFalse(markup.Contains("TriggerEffectChoices", StringComparison.Ordinal));
        Assert.IsTrue(document.Descendants().Any(element =>
            (string)element.Attribute(Xaml + "Name") == "profileTriggerLabControl"));
    }

    private static Border CreateProductionPanelHost(string theme, CalibrationLayoutModel model)
    {
        var document = XDocument.Load(FormPath("ProfileEditor.xaml"));
        var markup = new XElement(Wpf + "Border",
            new XAttribute(XNamespace.Xmlns + "x", Xaml.NamespaceName),
            new XElement(Wpf + "StackPanel", new[] { "LS", "RS" }.Select(stick =>
                new XElement(Wpf + "GroupBox", new XAttribute("Header", stick),
                    new XElement(CalibrationPanel(document, stick))))));
        var host = (Border)XamlReader.Parse(markup.ToString());
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative),
        });
        host.Background = (Brush)host.FindResource("BackgroundColor");
        TextElement.SetForeground(host, theme == "DefaultTheme" ? SystemColors.WindowTextBrush :
            (Brush)host.FindResource("ForegroundColor"));
        host.DataContext = model;
        return host;
    }

    private static XElement CalibrationPanel(XDocument document, string stick) =>
        document.Descendants(Wpf + "StackPanel").Single(element =>
            (string)element.Attribute(Xaml + "Name") == stick.ToLowerInvariant() + "FlickCalibrationPanel");

    public sealed class CalibrationLayoutModel
    {
        public const string LongButtonLabel = "Switch 2 Joy-Con Right Paddle 1";
        public int LSOutputIndex { get; set; } = (int)StickMode.FlickStick;
        public int RSOutputIndex { get; set; } = (int)StickMode.FlickStick;
        public DS4Controls LSFlickCalibrationTrigger { get; set; } = DS4Controls.Switch2JoyConRightPaddle1;
        public DS4Controls RSFlickCalibrationTrigger { get; set; } = DS4Controls.Switch2JoyConRightPaddle1;
        public CalibrationChoice[] FlickCalibrationTriggerChoices { get; } = new CalibrationChoice[]
        {
            new() { Control = DS4Controls.None, Label = "Not assigned" },
            new() { Control = DS4Controls.Switch2JoyConRightPaddle1, Label = LongButtonLabel },
        };
    }

    public sealed class CalibrationChoice
    {
        public DS4Controls Control { get; set; }
        public string Label { get; set; }
    }

    private static void AssertUncroppedText(TextBlock actual)
    {
        var probe = new TextBlock
        {
            Text = actual.Text,
            FontFamily = actual.FontFamily,
            FontSize = actual.FontSize,
            FontStyle = actual.FontStyle,
            FontWeight = actual.FontWeight,
            FontStretch = actual.FontStretch,
            TextWrapping = actual.TextWrapping,
            FlowDirection = actual.FlowDirection,
            Padding = actual.Padding,
        };
        probe.Measure(new Size(actual.ActualWidth, double.PositiveInfinity));
        Assert.IsTrue(probe.DesiredSize.Height <= actual.ActualHeight + 1,
            $"Text is cropped vertically: {actual.Text}");
        if (actual.TextWrapping == TextWrapping.NoWrap)
        {
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.IsTrue(probe.DesiredSize.Width <= actual.ActualWidth + 1,
                $"Text is cropped horizontally: {actual.Text}");
        }
    }

    private static void AssertInside(FrameworkElement host, FrameworkElement child)
    {
        Assert.IsTrue(child.ActualWidth > 0 && child.ActualHeight > 0);
        Rect bounds = BoundsIn(host, child);
        Assert.IsTrue(bounds.Left >= -0.1 && bounds.Top >= -0.1 &&
            bounds.Right <= host.ActualWidth + 0.1 && bounds.Bottom <= host.ActualHeight + 0.1,
            $"{child.Name} ({bounds}) exceeds {host.RenderSize}.");
    }

    private static Rect BoundsIn(FrameworkElement host, FrameworkElement child) =>
        child.TransformToAncestor(host).TransformBounds(new Rect(child.RenderSize));

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (T descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static string FormPath(string name, [CallerFilePath] string caller = "") =>
        Path.Combine(Path.GetDirectoryName(caller)!, "..", "DS4Windows", "DS4Forms", name);

    private static void RunSta(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Calibration render timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
