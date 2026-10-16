using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace DS4WindowsTests;

[TestClass]
public sealed class ProfileAdvancedLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme", 480d)]
    [DataRow("DefaultTheme", 480d)]
    [DataRow("DarkTheme", 980d)]
    [DataRow("DefaultTheme", 980d)]
    public void CategoriesKeepEditsAndControlsInsideTheAvailableWidth(string theme, double width)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Grid root = Load(theme);
                Layout(root, width);
                var rumble = (Xceed.Wpf.Toolkit.IntegerUpDown)root.FindName("RumbleBoostIUD");
                rumble.Value = 135;
                var navigation = root.Children.OfType<WrapPanel>().Single().Children.OfType<RadioButton>().ToArray();
                Assert.AreEqual(5, navigation.Length);
                foreach (RadioButton category in navigation)
                {
                    category.IsChecked = true;
                    Layout(root, width);
                    Grid pages = root.Children.OfType<Grid>().Single();
                    var visible = pages.Children.OfType<ScrollViewer>().Where(page => page.Visibility == Visibility.Visible).ToArray();
                    Assert.AreEqual(1, visible.Length);
                    ScrollViewer page = visible[0];
                    foreach (Expander expander in Visuals(page).OfType<Expander>().ToArray()) expander.IsExpanded = true;
                    Layout(root, width);
                    foreach (Control control in Visuals(page).OfType<Control>().Where(c => c.IsVisible && c.ActualWidth > 0 && !string.IsNullOrEmpty(c.Name)))
                    {
                        Point point = control.TranslatePoint(new Point(), root);
                        Assert.IsTrue(point.X >= -2 && point.X + control.ActualWidth <= width + 2,
                            $"{theme}/{width}/{category.Content}: {control.Name} overflows the page.");
                    }
                    Assert.AreEqual(135, rumble.Value, "Changing categories must preserve unsaved edits.");
                    string directory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                        var bitmap = new RenderTargetBitmap((int)width, 620, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(root);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(directory, $"{theme}-{width}-{category.Content}.png"));
                        encoder.Save(file);
                    }
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal static Grid Load(string theme, string page = "Advanced")
    {
        // Render the actual layout without a window, controller discovery,
        // change handlers or writes to saved profiles. Reveal all conditional
        // hardware panels to exercise the largest possible geometry.
        System.Reflection.Assembly.Load("DotNetProjects.Wpf.Extended.Toolkit");
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument source = XDocument.Load(SourcePath());
        XElement selected = page == "NintendoSetup"
            ? XDocument.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, "MainWindow.xaml")).Descendants(wpf + "Expander").Single(e => (string)e.Attribute("Header") == "Nintendo Switch 2 controllers")
            : page is "ControllerReadings" or "OutputSlots"
            ? XDocument.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, page == "OutputSlots" ? "OutputSlotManagerControl.xaml" : "ControllerReadingsControl.xaml")).Root
            : source.Descendants(wpf + "TabItem")
                .Single(e => (string)e.Attribute("Header") == (page is "Advanced" or "Special Actions" or "Nintendo Options" ? page : "{lex:Loc " + page + "}"));
        XElement content = selected.Elements().First(e => !e.Name.LocalName.EndsWith(".Resources"));
        var markup = content.Name == wpf + "Grid" ? new XElement(content) : new XElement(wpf + "Grid", new XElement(content));
        // Render the actual shared axial layout without its controller wiring.
        foreach (XElement axial in markup.Descendants().Where(e => e.Name.LocalName == "AxialStickUserControl").ToArray())
        {
            XElement axialGrid = new XElement(XDocument.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, "AxialStickUserControl.xaml")).Root.Element(wpf + "Grid"));
            axialGrid.SetAttributeValue(x + "Name", (string)axial.Attribute(x + "Name"));
            axial.ReplaceWith(axialGrid);
        }
        markup.SetAttributeValue("TextElement.Foreground", "{DynamicResource ForegroundColor}");
        markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        markup.SetAttributeValue(XNamespace.Xmlns + "xctk", "http://schemas.xceed.com/wpf/xaml/toolkit");
        markup.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:DS4WinWPF.DS4Forms;assembly=DS4Windows");
        var strings = XDocument.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "Translations", "Strings.resx"))
            .Descendants("data").ToDictionary(e => (string)e.Attribute("name"), e => e.Element("value")?.Value ?? "");
        foreach (XElement element in markup.DescendantsAndSelf())
        foreach (XAttribute attribute in element.Attributes().ToArray())
        {
            if (attribute.Name.LocalName is "Click" or "SelectionChanged" or "ValueChanged" or "Loaded" or "Unloaded" or "SizeChanged" or "Checked" or "Unchecked" || attribute.Name.NamespaceName == "http://schemas.microsoft.com/expression/blend/2008") attribute.Remove();
            else if (attribute.Value.StartsWith("{lex:Loc "))
            {
                string key = attribute.Value[9..^1];
                attribute.Value = strings.TryGetValue(key, out string value) ? value : key;
            }
            else if (attribute.Value.StartsWith("{Binding") && element.Name.LocalName != "DataTrigger" && attribute.Value != "{Binding}" && !(page is "Nintendo Options" or "NintendoSetup" && attribute.Value is "{Binding Label}" or "{Binding IsSelected}" or "{Binding ActualWidth, ElementName=switch2SetupContent}") && !attribute.Value.Contains("ElementName=profileAdvanced") && !attribute.Value.Contains("ElementName=profileAxis") && !((page == "OutputSlots" || page == "Special Actions") && (attribute.Name.LocalName == "Binding" || attribute.Value.Contains("BoundInput") || attribute.Value.Contains("Active"))) && !(page == "ControllerReadings" && (attribute.Value.Contains("ElementName=") || attribute.Value.Contains("Source={StaticResource ForegroundColor}"))))
            {
                if (attribute.Name.LocalName == "Text" && attribute.Value.Contains("ControllerAudioHeader")) attribute.Value = "Controller audio";
                else attribute.Remove();
            }
        }
        XElement bridge = XDocument.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, "Themes", "BridgeShellStyles.xaml")).Root;
        bridge.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
            new XElement(wpf + "ResourceDictionary", new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))));
        XElement existingResources = markup.Element(wpf + "Grid.Resources");
        XElement[] existingEntries = existingResources?.Elements().Select(e => new XElement(e)).ToArray() ?? [];
        XElement modeStyle = source.Root.Element(wpf + "UserControl.Resources").Elements().Single(e => (string)e.Attribute(x + "Key") == "ProfileModeTabControlStyle");
        existingResources?.Remove();
        markup.AddFirst(new XElement(wpf + "Grid.Resources", new XElement(wpf + "ResourceDictionary",
            new XElement(wpf + "ResourceDictionary.MergedDictionaries", bridge),
            new XElement(wpf + "BooleanToVisibilityConverter", new XAttribute(x + "Key", "BooleanToVisibilityConverter")),
            new XElement(wpf + "Thickness", new XAttribute(x + "Key", "spaceMargin"), "0,0,0,8"),
            new XElement("{http://schemas.xceed.com/wpf/xaml/toolkit}InverseBoolConverter", new XAttribute(x + "Key", "InverseBoolConverter")),
            new XElement(modeStyle),
            new XElement(source.Root.Element(wpf + "UserControl.Resources").Elements().Single(e => (string)e.Attribute(x + "Key") == "ProfileRgbSliderStyle")), existingEntries,
            selected.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith(".Resources"))?.Elements().Where(e => (string)e.Attribute(x + "Key") != "spaceMargin").Select(e => new XElement(e)))));
        string artifactDirectory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
        if (!string.IsNullOrEmpty(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllText(Path.Combine(artifactDirectory, $"{page}-{theme}-markup.xaml"), markup.ToString());
        }
        var root = (Grid)XamlReader.Parse(markup.ToString());
        root.Background = (Brush)root.FindResource("SurfaceBackgroundColor");
        return root;
    }

    private static void Layout(FrameworkElement root, double width)
    {
        root.Measure(new Size(width, 620));
        root.Arrange(new Rect(0, 0, width, 620));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (DependencyObject descendant in Visuals(child)) yield return descendant;
        }
    }

    private static string SourcePath([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "DS4Windows", "DS4Forms", "ProfileEditor.xaml"));
}
