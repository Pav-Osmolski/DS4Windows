using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels.SpecialActions;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class MultiActionEditorLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme", 320d)] [DataRow("DefaultTheme", 320d)]
    [DataRow("DarkTheme", 620d)] [DataRow("DefaultTheme", 620d)]
    public void GestureControlsWrapAndRemainAccessible(string theme, double width)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            System.Reflection.Assembly.Load("DotNetProjects.Wpf.Extended.Toolkit");
            XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            string forms = Path.Combine(Root(), "DS4Windows", "DS4Forms");
            var source = LocalizedSource.Load(Path.Combine(forms, "SpecialActionEditor.xaml"));
            var panel = new XElement(source.Descendants(w + "TabItem").Single(e => (string)e.Attribute(x + "Name") == "multiActTab").Element(w + "ScrollViewer"));
            foreach (var node in panel.DescendantsAndSelf())
                foreach (var attribute in node.Attributes().ToArray())
                    if (attribute.Name.LocalName == "Click") attribute.Remove();
                    else if (attribute.Value.StartsWith("{lex:Loc ")) attribute.Value = LocalizedSource.Resolve(attribute.Value);
                    else if (attribute.Value.StartsWith("{Binding ") && attribute.Value.Contains(",FallbackValue="))
                        attribute.Value = attribute.Value[..attribute.Value.IndexOf(",FallbackValue=")] + "}";
            var rootXml = new XElement(w + "Grid", new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xctk", "http://schemas.xceed.com/wpf/xaml/toolkit"), panel);
            var dictionary = LocalizedSource.Load(Path.Combine(forms, "Themes", "BridgeShellStyles.xaml")).Root;
            dictionary.AddFirst(new XElement(w + "ResourceDictionary.MergedDictionaries",
                new XElement(w + "ResourceDictionary", new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))));
            dictionary.Add(new XElement(w + "Thickness", new XAttribute(x + "Key", "spaceMargin"), "0,0,0,8"));
            rootXml.AddFirst(new XElement(w + "Grid.Resources", dictionary));
            rootXml.SetAttributeValue("Background", "{DynamicResource BackgroundColor}");
            rootXml.SetAttributeValue("TextElement.Foreground", "{DynamicResource ForegroundColor}");
            var root = (Grid)XamlReader.Parse(rootXml.ToString());
            var model = new MultiActButtonViewModel();
            model.LoadAction(new SpecialAction("Test", "Cross", "MultiAction", "65/65,66/66,67/67,68/68,750,200"));
            root.DataContext = model;
            root.Measure(new Size(width, 480)); root.Arrange(new Rect(0, 0, width, 480)); root.UpdateLayout();
            var viewer = root.Children.OfType<ScrollViewer>().Single();
            var content = (StackPanel)viewer.Content;
            var buttons = content.Children.OfType<Button>().ToArray();
            Assert.AreEqual(4, buttons.Length);
            foreach (var button in buttons) Assert.IsTrue(button.ActualWidth <= width && button.ActualWidth > 0);
            var inputs = content.Children.OfType<WrapPanel>().Single().Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Xceed.Wpf.Toolkit.DecimalUpDown>()).ToArray();
            Assert.AreEqual(.75m, inputs[0].Value); Assert.AreEqual(.2m, inputs[1].Value);
            foreach (var control in inputs)
            {
                var position = control.TranslatePoint(new Point(), content);
                Assert.IsTrue(position.X >= 0 && position.X + control.ActualWidth <= width);
            }
            string directory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)width, 480, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, $"MultiAction-{theme}-{width}.png")); encoder.Save(output);
            }
        });
    }
    private static string Root([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, ".."));
}
