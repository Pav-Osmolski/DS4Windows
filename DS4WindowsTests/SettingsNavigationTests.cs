using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace DS4WindowsTests;

[TestClass]
public sealed class SettingsNavigationTests
{
    [TestMethod]
    [DataRow("DarkTheme", 640d)]
    [DataRow("DefaultTheme", 640d)]
    [DataRow("DarkTheme", 980d)]
    [DataRow("DefaultTheme", 980d)]
    [DataRow("DarkTheme", 1800d)]
    [DataRow("DefaultTheme", 1800d)]
    public void ActualSettingsNavigationPreservesEditsAndKeepsEverySectionReachable(string theme, double width)
    {
        Exception failure = null;
        Action thread = () =>
        {
            try
            {
                Grid root = LoadSettings(theme);
                Layout(root, width);
                Assert.AreEqual(width, root.ActualWidth, 1d,
                    "Settings must fill the available width, including a maximized window.");
                var startup = (CheckBox)root.FindName("runAtStartCk");
                var steam = (TextBox)root.FindName("customSteamTxt");
                startup.IsChecked = false;
                steam.Text = @"C:\Games\Steam";
                var navigation = Visuals(root).OfType<RadioButton>()
                    .Where(control => control.GroupName == "SettingsCategory").ToArray();
                Assert.IsTrue(navigation.Length > 1);
                foreach (RadioButton category in navigation)
                {
                    category.IsChecked = true;
                    Layout(root, width);
                    var active = Visuals(root).OfType<StackPanel>()
                        .Where(panel => panel.Name.StartsWith("settings") && panel.Name.EndsWith("Page") && panel.Visibility == Visibility.Visible).ToArray();
                    Assert.AreEqual(1, active.Length, "Exactly one settings section must be displayed.");
                    Assert.AreEqual(category.Name.Replace("Nav", "Page"), active[0].Name);
                    foreach (Expander expander in Visuals(active[0]).OfType<Expander>().ToArray())
                        expander.IsExpanded = true;
                    Layout(root, width);
                    foreach (Control control in Visuals(active[0]).OfType<Control>().Where(control => control.ActualWidth > 0 && control.Visibility == Visibility.Visible))
                    {
                        Point origin = control.TranslatePoint(new Point(), active[0]);
                        Assert.IsTrue(origin.X >= -1 && origin.X + control.ActualWidth <= active[0].ActualWidth + 1,
                            $"{theme}/{width}: {control.Name} overflows {active[0].Name}.");
                    }
                    Assert.IsFalse(startup.IsChecked.Value, "Section changes must not reset checkbox edits.");
                    Assert.AreEqual(@"C:\Games\Steam", steam.Text, "Section changes must not recreate edited fields.");
                }
                var navigator = new DS4WinWPF.DS4Forms.SettingsNavigator((Grid)root.FindName("settingsPages"), navigation);
                navigator.RestoreSection("Controllers");
                Assert.IsTrue(((RadioButton)root.FindName("settingsControllersNav")).IsChecked.Value);
                Assert.IsTrue(navigator.SetSearch("startup") > 0, "Search must find settings outside the selected section.");
                Assert.AreEqual(Visibility.Visible, ((StackPanel)root.FindName("settingsGeneralPage")).Visibility);
                Assert.IsTrue(navigator.SetSearch("controller") > 1);
                Layout(root, width);
                var visibleResults = ((Grid)root.FindName("settingsPages")).Children.OfType<StackPanel>()
                    .Where(page => page.Visibility == Visibility.Visible).OrderBy(Grid.GetRow).ToArray();
                for (int index = 1; index < visibleResults.Length; index++)
                    Assert.IsTrue(visibleResults[index].TranslatePoint(new Point(), visibleResults[index - 1]).Y
                        >= visibleResults[index - 1].ActualHeight - 1, "Search sections must stack without overlapping.");
                Assert.AreEqual(0, navigator.SetSearch("zzzz-no-such-setting"));
                navigator.SetSearch("");
                Layout(root, width);
                Assert.AreEqual(Visibility.Visible, ((StackPanel)root.FindName("settingsControllersPage")).Visibility);
                Assert.AreEqual(Visibility.Collapsed, ((StackPanel)root.FindName("settingsGeneralPage")).Visibility);
                Assert.IsFalse(startup.IsChecked.Value);
                Assert.AreEqual(@"C:\Games\Steam", steam.Text);
            }
            catch (Exception error) { failure = error; }
        };
        HeadlessWpfDispatcher.Invoke(thread);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    [DataRow("DarkTheme", 640d)]
    [DataRow("DefaultTheme", 640d)]
    [DataRow("DarkTheme", 980d)]
    [DataRow("DefaultTheme", 980d)]
    public void EverySettingsSectionStartsAtTheSamePosition(string theme, double width)
    {
        Exception failure = null;
        Action thread = () =>
        {
            try
            {
                Grid root = LoadSettings(theme);
                root.UseLayoutRounding = true;
                Layout(root, width);
                var navigation = Visuals(root).OfType<RadioButton>()
                    .Where(control => control.GroupName == "SettingsCategory").ToArray();
                var pages = (Grid)root.FindName("settingsPages");
                var navigator = new DS4WinWPF.DS4Forms.SettingsNavigator(pages, navigation);
                Point? expectedDescription = null;
                double? expectedCard = null;
                foreach (bool afterSearch in new[] { false, true })
                {
                    if (afterSearch)
                    {
                        navigator.SetSearch("controller");
                        Layout(root, width);
                        navigator.SetSearch("");
                    }
                    foreach (var category in navigation)
                    {
                        category.IsChecked = true;
                        ((ScrollViewer)root.FindName("settingsScrollViewer")).ScrollToTop();
                        Layout(root, width);
                        var page = (StackPanel)root.FindName(category.Name.Replace("Nav", "Page"));
                        var description = page.Children.OfType<TextBlock>().First(t => t.Visibility == Visibility.Visible);
                        var card = page.Children.OfType<Border>().First();
                        Point start = description.TranslatePoint(new Point(), root);
                        double cardTop = card.TranslatePoint(new Point(), root).Y;
                        expectedDescription ??= start;
                        expectedCard ??= cardTop;
                        Assert.AreEqual(expectedDescription.Value.X, start.X, 0.1, "Section left edges must match.");
                        Assert.AreEqual(expectedDescription.Value.Y, start.Y, 0.1, "Section descriptions must start at the same height.");
                        Assert.AreEqual(expectedCard.Value, cardTop, 0.1, "First settings cards must start at the same height.");
                        string directory = Environment.GetEnvironmentVariable("DS4W_SETTINGS_LAYOUT_ARTIFACTS");
                        if (!afterSearch && !string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, 620, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(root);
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(directory, $"Settings-{theme}-{width}-{category.Tag}.png"));
                            encoder.Save(file);
                        }
                    }
                }
            }
            catch (Exception error) { failure = error; }
        };
HeadlessWpfDispatcher.Invoke(thread);         if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static Grid LoadSettings(string theme)
    {
        // Exercise the production layout without constructing the main window,
        // starting controller discovery or executing setting-change handlers.
        System.Reflection.Assembly.Load("DotNetProjects.Wpf.Extended.Toolkit");
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement settings = LocalizedSource.Load(SourcePath()).Descendants(wpf + "TabItem")
            .Single(element => (string)element.Attribute(x + "Name") == "settingsTab");
        var markup = new XElement(settings.Element(wpf + "Grid"));
        markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        markup.SetAttributeValue(XNamespace.Xmlns + "xctk", "http://schemas.xceed.com/wpf/xaml/toolkit");
        foreach (XElement element in markup.DescendantsAndSelf().ToArray())
        {
            if (element.Name.LocalName == "LanguagePackControl")
            {
                element.ReplaceWith(new XElement(wpf + "TextBlock", new XAttribute("Text", "Language selector")));
                continue;
            }
            if (element.Name.LocalName == "BatteryAlertsControl")
            {
                element.ReplaceWith(new XElement(wpf + "ContentControl", new XAttribute(x + "Name", "batteryAlertsTestHost")));
                continue;
            }
            foreach (XAttribute attribute in element.Attributes().ToArray())
            {
                if (attribute.Name.LocalName is "Click" or "Checked" or "SelectionChanged" or "TextChanged" or "PreviewKeyDown" ||
                    attribute.Name.NamespaceName == "http://schemas.microsoft.com/expression/blend/2008") attribute.Remove();
                else if (attribute.Value.StartsWith("{lex:Loc ")) attribute.Value = LocalizedSource.Resolve(attribute.Value);
                else if (attribute.Value.StartsWith("{Binding") && !attribute.Value.Contains("ElementName=settings"))
                    attribute.Remove();
            }
        }
        // Give the dictionary its own base theme instead of relying on a
        // process-wide Application (which may belong to another STA test).
        XElement bridge = LocalizedSource.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, "Themes", "BridgeShellStyles.xaml")).Root;
        bridge.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
            new XElement(wpf + "ResourceDictionary", new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))));
        var resources = (ResourceDictionary)XamlReader.Parse(LocalizedSource.Markup(bridge));
        var root = (Grid)XamlReader.Parse(LocalizedSource.Markup(markup));
        root.Resources.MergedDictionaries.Add(resources);
        ((ContentControl)root.FindName("batteryAlertsTestHost")).Content =
            new DS4WinWPF.DS4Forms.BatteryAlertsControl(false, new DS4Windows.BatteryAlertSettings());
        var trayChoices = (ComboBox)root.FindName("useIconChoiceCom");
        Assert.AreEqual(5, trayChoices.Items.Count);
        foreach (ComboBoxItem choice in trayChoices.Items)
        {
            var row = (StackPanel)choice.Content;
            var image = (Image)row.Children.OfType<Border>().Single().Child;
            Assert.IsNotNull(image.Source, "Every tray-icon choice must have an actual embedded icon preview.");
            Assert.IsTrue(((System.Windows.Media.Imaging.BitmapSource)image.Source).PixelWidth > 0);
            Assert.IsTrue(row.Children.OfType<TextBlock>().Any());
        }
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
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "DS4Windows", "DS4Forms", "MainWindow.xaml"));
}
