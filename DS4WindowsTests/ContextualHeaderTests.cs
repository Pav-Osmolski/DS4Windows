using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class ContextualHeaderTests
{
    public TestContext TestContext { get; set; }
    private static readonly AppThemeChoice[] Themes = [AppThemeChoice.Light, AppThemeChoice.Dark,
        AppThemeChoice.Graphite, AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast];

    [TestMethod]
    public void ContextualHeadersCentreRealContentAndGrowForWrappingAndLargerText()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var header = new PageHeaderControl { Title = "Overview", Description = "Your controller, active profile and output at a glance." };
                header.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, highContrast: false));
                foreach (double width in new[] { 800d, 320d })
                {
                    NaturalLayout(header, width);
                    Assert.IsTrue(header.ActualHeight >= 72);
                    AssertCentred(header);
                    var description = (TextBlock)header.FindName("headingDescription");
                    Assert.AreEqual(((SolidColorBrush)header.FindResource("MutedForegroundColor")).Color,
                        ((SolidColorBrush)description.Foreground).Color);
                    var border = (Border)header.FindName("headerSurface");
                    Assert.AreEqual(((SolidColorBrush)header.FindResource("BorderColor")).Color,
                        ((SolidColorBrush)border.BorderBrush).Color);
                    Save(header, $"header-{theme}-{width}", 2);
                }
                header.Description = null;
                NaturalLayout(header, 800);
                Assert.AreEqual(Visibility.Collapsed, ((TextBlock)header.FindName("headingDescription")).Visibility);
                AssertCentred(header);
                header.Description = "";
                NaturalLayout(header, 800);
                AssertCentred(header);
                header.Description = "Controller settings and profile configuration ".PadRight(170, 'W');
                ((TextBlock)header.FindName("headingDescription")).FontSize = 20;
                header.Title = "Long translated controller configuration heading";
                ((TextBlock)header.FindName("headingTitle")).FontSize = 30;
                header.FlowDirection = FlowDirection.RightToLeft;
                NaturalLayout(header, 320);
                Assert.IsTrue(header.ActualHeight > 72, "Larger or translated text must grow the header, not clip it.");
                AssertCentred(header);
                var group = (StackPanel)header.FindName("headingGroup");
                Assert.IsTrue(group.ActualHeight + 21 <= header.ActualHeight + 0.1);
            }
        });
    }

    [TestMethod]
    public void ShellSwitchesSectionAndEditorHeadersAndKeepsSidebarInsetsEqual()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var shell = new TabControl();
                shell.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, highContrast: false));
                shell.Resources.MergedDictionaries.Add(ShellStyles());
                shell.Style = (Style)shell.FindResource("BridgeMainTabControlStyle");
                var device = new { ControllerDisplayName = "DualSense", SelectedProfile = "Windows Control", BatteryState = "100%" };
                object State(bool editor) => new ShellState { ControllerCol = new object[] { device }, SelectedController = device,
                    ProfileEditorMode = editor, ProfileEditorSectionTitle = "Button mapping",
                    ProfileEditorSectionDescription = "Assign controller buttons and shortcuts." };
                shell.DataContext = State(false);
                var overview = new TabItem { Header = "Overview", Content = new Grid() };
                AppToolTips.SetHeaderHelp(overview, "Your controller, profile and output."); shell.Items.Add(overview);
                var profiles = new TabItem { Header = "Profiles", Content = new Grid() };
                AppToolTips.SetHeaderHelp(profiles, "Create and organise controller profiles."); shell.Items.Add(profiles);
                shell.SelectedIndex = 0;
                ThemeConsistencyValidation.Layout(shell, 1180, 680);
                var ordinary = (PageHeaderControl)shell.Template.FindName("sectionHeader", shell);
                var editorHeader = (PageHeaderControl)shell.Template.FindName("editorSectionHeader", shell);
                Assert.AreEqual("Overview", ordinary.Title);
                Assert.AreEqual("Your controller, profile and output.", ordinary.Description);
                Assert.AreEqual(Visibility.Visible, ordinary.Visibility);
                Assert.AreEqual(Visibility.Collapsed, editorHeader.Visibility);
                shell.SelectedIndex = 1;
                ThemeConsistencyValidation.Layout(shell, 1180, 680);
                Assert.AreEqual("Profiles", ordinary.Title);
                Assert.AreEqual("Create and organise controller profiles.", ordinary.Description);
                AssertCentred(ordinary);
                var list = (ListBox)shell.Template.FindName("SidebarControllerList", shell);
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsNotNull(item);
                var card = (Border)item.Template.FindName("ControllerSelectorCard", item);
                double left = card.TranslatePoint(new Point(), list).X;
                double right = list.ActualWidth - card.TranslatePoint(new Point(card.ActualWidth, 0), list).X;
                Assert.AreEqual(left, right, 0.1, $"{theme}: sidebar controller card must have equal visible left/right insets.");
                shell.DataContext = State(true);
                ThemeConsistencyValidation.Layout(shell, 1180, 680);
                Assert.AreEqual(Visibility.Collapsed, ordinary.Visibility);
                Assert.AreEqual(Visibility.Visible, editorHeader.Visibility);
                Assert.AreEqual("Button mapping", editorHeader.Title);
                AssertCentred(editorHeader);
                shell.DataContext = State(false);
                ThemeConsistencyValidation.Layout(shell, 1180, 680);
                DisclosureAnimationTests.Pump(250);
                shell.UpdateLayout();
                Save(shell, $"shell-contextual-{theme}", 1);
                var many = (ShellState)State(false);
                many.ControllerCol = Enumerable.Range(0, 8).Select(i => (object)new
                    { ControllerDisplayName = "Controller " + (i + 1), SelectedProfile = "Default", BatteryState = "100%" }).ToArray();
                many.SelectedController = many.ControllerCol[0];
                shell.DataContext = many;
                ThemeConsistencyValidation.Layout(shell, 1180, 680);
                list.ScrollIntoView(many.ControllerCol[^1]);
                ThemeConsistencyValidation.Layout(shell, 1180, 680);
                var scroller = ThemeConsistencyValidation.Visuals(list).OfType<ScrollViewer>().First();
                Assert.IsTrue(scroller.ScrollableHeight > 0, "Additional controllers must retain a scrollable viewport.");
                Assert.AreEqual(Visibility.Visible, scroller.ComputedVerticalScrollBarVisibility);
                Assert.IsNotNull(list.ItemContainerGenerator.ContainerFromIndex(7));
            }
        });
    }

    // Called by ThemeResourceTests after App.xaml's shared style dependencies
    // have been loaded into the process-lifetime application fixture.
    internal void ValidateProfileActions()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var theme in Themes)
            foreach (double width in new[] { 1400d, 650d })
            {
                var source = LocalizedSource.Load(Path.Combine(SourceDirectory(), "MainWindow.xaml"));
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var markup = new XElement(source.Descendants().Single(n => (string)n.Attribute(x + "Name") == "profOptsToolbar"));
                foreach (var attr in markup.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName == "Click").ToArray()) attr.Remove();
                markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                var host = new StackPanel();
                host.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, highContrast: false));
                host.Resources.MergedDictionaries.Add(ShellStyles());
                host.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackgroundColor");
                var toolbar = (Border)XamlReader.Parse(LocalizedSource.Markup(markup));
                var nextSection = new Border { Height = 74, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
                nextSection.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
                var page = new StackPanel { Margin = new Thickness(28, 22, 28, 22) };
                page.Children.Add(toolbar); page.Children.Add(nextSection); host.Children.Add(page);
                NaturalLayout(host, width);
                var buttons = ThemeConsistencyValidation.Visuals(toolbar).OfType<Button>().ToArray();
                Assert.AreEqual(7, buttons.Length);
                foreach (var button in buttons) Assert.AreEqual(buttons[0].ActualHeight, button.ActualHeight, 0.1);
                Assert.AreEqual(new Thickness(0), toolbar.BorderThickness);
                Assert.AreEqual(new Thickness(0), toolbar.Padding);
                Assert.AreEqual((byte)0, ((SolidColorBrush)toolbar.Background).Color.A);
                Assert.AreEqual(buttons[0].TranslatePoint(new Point(), host).X,
                    nextSection.TranslatePoint(new Point(), host).X, 0.1, "The first action must align with section outlines.");
                double top = buttons.Min(b => b.TranslatePoint(new Point(), host).Y);
                double bottom = buttons.Max(b => b.TranslatePoint(new Point(0, b.ActualHeight), host).Y);
                double gap = nextSection.TranslatePoint(new Point(), host).Y - bottom;
                Assert.AreEqual(22d, top, 0.1);
                Assert.AreEqual(top, gap, 0.1, "Space above and below visible actions must be equal.");
                if (width == 650) Assert.IsTrue(buttons.Select(b => b.TranslatePoint(new Point(), host).Y).Distinct().Count() > 1);
                Save(host, $"profile-actions-{theme}-{width}", 1);
            }
        });
    }

    private static ResourceDictionary ShellStyles() => new() { Source = new Uri(
        "/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) };

    private sealed class ShellState
    {
        public object[] ControllerCol { get; set; }
        public object SelectedController { get; set; }
        public bool ProfileEditorMode { get; set; }
        public string ProfileEditorSectionTitle { get; set; }
        public string ProfileEditorSectionDescription { get; set; }
    }

    private static void NaturalLayout(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
        element.UpdateLayout();
    }

    private static void AssertCentred(PageHeaderControl header)
    {
        var group = (StackPanel)header.FindName("headingGroup");
        double centre = group.TranslatePoint(new Point(0, group.ActualHeight / 2), header).Y;
        Assert.AreEqual((header.ActualHeight - 1) / 2, centre, 0.6, "Centre the complete title/description group above the divider.");
    }

    private void Save(FrameworkElement element, string name, int scale)
    {
        string path = Path.Combine(TestContext.TestResultsDirectory, name + ".png");
        Directory.CreateDirectory(TestContext.TestResultsDirectory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale),
            (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(path)) encoder.Save(output);
        TestContext.AddResultFile(path);
    }

    private static string SourceDirectory([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", "DS4Windows", "DS4Forms"));
}
