using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class GenericJoystickLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme", 480d)]
    [DataRow("DefaultTheme", 980d)]
    public void JoystickCardFitsAndChangingModeClearsIncompatibleInversions(string theme, double width)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
            const int slot = 8;
            var previous = Global.GenericJoystickSettings[slot];
            try
            {
                Global.GenericJoystickSettings[slot] = new GenericJoystickProfileSettings();
                var model = new ProfileSettingsViewModel(slot);
                model.JoystickInvertLeft = true;
                XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../DS4Windows/DS4Forms/ProfileEditor.xaml"));
                var source = XDocument.Load(path);
                var card = new XElement(source.Descendants(wpf + "Border").Single(e => (string)e.Attribute(x + "Name") == "genericJoystickPanel"));
                card.SetAttributeValue("Visibility", "Visible");
                var markup = new XElement(wpf + "Grid",
                    new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "xctk", "http://schemas.xceed.com/wpf/xaml/toolkit"),
                    new XElement(wpf + "Grid.Resources", new XElement(wpf + "ResourceDictionary",
                        new XElement(wpf + "ResourceDictionary.MergedDictionaries",
                            new XElement(wpf + "ResourceDictionary", new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))))), card);
                var root = (Grid)XamlReader.Parse(markup.ToString());
                root.Background = (Brush)root.FindResource("BackgroundColor");
                root.DataContext = model;
                root.Measure(new Size(width, 620)); root.Arrange(new Rect(0, 0, width, 620)); root.UpdateLayout();
                var combos = Logical(root).OfType<ComboBox>().ToArray();
                Assert.AreEqual(2, combos.Length);
                Assert.AreEqual(0, combos[0].SelectedIndex);
                combos[1].SelectedIndex = 2;
                root.UpdateLayout();
                Assert.AreEqual(2, model.JoystickTriggerModeIndex);
                Assert.IsFalse(model.JoystickInvertLeft);
                Assert.IsFalse(Logical(root).OfType<CheckBox>().First().IsEnabled);
                foreach (Control control in Logical(root).OfType<Control>())
                {
                    Point point = control.TranslatePoint(new Point(), root);
                    Assert.IsTrue(point.X >= 0 && point.X + control.ActualWidth <= width + 1, "Joystick control extends beyond the card.");
                }
                var assignments = Logical(root).OfType<Expander>().Single();
                assignments.IsExpanded = true;
                root.Measure(new Size(width, double.PositiveInfinity));
                root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height)); root.UpdateLayout();
                var numbered = Visual(root).OfType<ComboBox>().Where(c => c.DataContext is JoystickButtonAssignment).ToArray();
                Assert.AreEqual(16, numbered.Length, "Every numbered output needs an editable source.");
                foreach (var combo in numbered)
                {
                    Assert.AreEqual(15, combo.Items.Count);
                    Point position = combo.TranslatePoint(new Point(), root);
                    Assert.IsTrue(position.X >= 0 && position.X + combo.ActualWidth <= width + 1);
                }
                numbered[15].SelectedIndex = 13; root.UpdateLayout();
                Assert.AreEqual(13, Global.GenericJoystickSettings[slot].Options.SourceFor(15));
                string output = Environment.GetEnvironmentVariable("DS4W_JOYSTICK_UI_ARTIFACTS");
                if (!string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    var bitmap = new RenderTargetBitmap((int)width, (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"joystick-{theme}-{width}.png")); encoder.Save(file);
                }
            }
            finally { Global.GenericJoystickSettings[slot] = previous; }
        });
    }

    private static IEnumerable<DependencyObject> Visual(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var item in Visual(child)) yield return item;
        }
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Logical(child)) yield return item;
    }
}
