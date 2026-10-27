using DS4Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class SharedMenuLayoutTests
{
    [TestMethod]
    public void CheckedProfilesKeepTheirLabelColumnAcrossThemes()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var theme in new[] { AppThemeChoice.Light, AppThemeChoice.Dark, AppThemeChoice.Graphite, AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast })
            {
                var menu = new ContextMenu();
                menu.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, highContrast: false));
                menu.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                var first = new MenuItem { Header = "Default" };
                var second = new MenuItem { Header = "Windows Control", IsChecked = true };
                var parent = new MenuItem { Header = "Controller 1" };
                parent.Items.Add(new MenuItem { Header = "MAME" });
                menu.Items.Add(first); menu.Items.Add(second); menu.Items.Add(new Separator()); menu.Items.Add(parent);
                Layout(menu);
                var a = (ContentPresenter)first.Template.FindName("MenuHeader", first);
                var b = (ContentPresenter)second.Template.FindName("MenuHeader", second);
                Assert.IsNotNull(a, theme.ToString()); Assert.IsNotNull(b);
                Assert.AreEqual(a.TranslatePoint(new Point(), first).X, b.TranslatePoint(new Point(), second).X, .01);
                var tick = (System.Windows.Shapes.Path)second.Template.FindName("MenuCheck", second);
                Assert.AreEqual(Visibility.Visible, tick.Visibility);
                second.IsChecked = false; menu.UpdateLayout();
                Assert.AreEqual(Visibility.Hidden, tick.Visibility);
                Assert.AreEqual(a.TranslatePoint(new Point(), first).X, b.TranslatePoint(new Point(), second).X, .01);
                Assert.AreEqual(Visibility.Visible, ((FrameworkElement)parent.Template.FindName("SubmenuArrow", parent)).Visibility);
                var popup = (Popup)parent.Template.FindName("PART_Popup", parent);
                Assert.AreEqual(PlacementMode.Right, popup.Placement);
                Assert.AreEqual(7d, ((Border)popup.Child).CornerRadius.TopLeft);
                var split = new Xceed.Wpf.Toolkit.SplitButton { Content = "Edit", Height = 34, Width = 100, Resources = menu.Resources, DropDownContent = new Button { Content = "New profile" } };
                Layout(split);
                Assert.IsNotNull(split.Template.FindName("PART_ActionButton", split));
                Assert.IsNotNull(split.Template.FindName("PART_ToggleButton", split));
                Assert.AreEqual(34d, split.ActualHeight);
                second.IsChecked = true; menu.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)menu.ActualWidth, (int)menu.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(menu);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory("menu-renders");
                using var output = File.Create($"menu-renders/{theme}.png"); encoder.Save(output);
            }
        });
    }

    [TestMethod]
    public void MappingCardsKeepTheirInsetsAcrossThemes()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../DS4Windows/DS4Forms/ProfileEditor.xaml"));
            var source = LocalizedSource.Load(path);
            foreach (string theme in new[] { "DefaultTheme", "DarkTheme" })
            {
                var listMarkup = new XElement(source.Descendants(wpf + "ListBox").Single(e => (string)e.Attribute(x + "Name") == "mappingListBox"));
                listMarkup.Attribute("MouseDoubleClick")?.Remove(); listMarkup.Attribute("ItemsSource")?.Remove(); listMarkup.Attribute("SelectedIndex")?.Remove();
                var dictionary = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x),
                    new XElement(wpf + "ResourceDictionary.MergedDictionaries",
                        new XElement(wpf + "ResourceDictionary", new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml")),
                        new XElement(wpf + "ResourceDictionary", new XAttribute("Source", "/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml"))),
                    new XElement(wpf + "BooleanToVisibilityConverter", new XAttribute(x + "Key", "BooleanToVisibilityConverter")));
                var wrapper = new XElement(wpf + "Border", new XAttribute("Width", 500), new XAttribute("Height", 200),
                    new XElement(wpf + "Border.Resources", dictionary), listMarkup);
                var host = (Border)XamlReader.Parse(LocalizedSource.Markup(wrapper));
                var list = (ListBox)host.Child;
                for (int i = 0; i < 20; i++) list.Items.Add(new { ControlName = "Cross", MappingName = "A Button", IsAvailableOnPhysicalController = true });
                Layout(host);
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsNotNull(item);
                Point topLeft = item.TranslatePoint(new Point(), list);
                Assert.IsTrue(topLeft.X >= 9 && topLeft.X <= 10, theme + " left inset: " + topLeft.X);
                Assert.IsTrue(topLeft.Y >= 9 && topLeft.Y <= 10, theme + " top inset: " + topLeft.Y);
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(list));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
            }
        });
    }

    private static void Layout(FrameworkElement element)
    {
        element.ApplyTemplate(); element.Measure(new Size(500, 500));
        element.Arrange(new Rect(new Point(), element.DesiredSize)); element.UpdateLayout();
    }
}
