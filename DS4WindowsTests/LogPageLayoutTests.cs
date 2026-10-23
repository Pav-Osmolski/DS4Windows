using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using DS4WinWPF;

namespace DS4WindowsTests;

[TestClass]
public sealed class LogPageLayoutTests
{
    [TestMethod]
    [DataRow("DarkTheme", 600d)]
    [DataRow("DefaultTheme", 600d)]
    [DataRow("DarkTheme", 900d)]
    [DataRow("DefaultTheme", 900d)]
    public void LogToolsRemainReachableWithoutSqueezingTheVirtualizedList(string theme, double width)
    {
        Exception failure = null;
        Action thread = () =>
        {
            try
            {
                DockPanel root = Load(theme);
                var list = (ListView)root.FindName("logListView");
                list.ItemsSource = Enumerable.Range(0, 40).Select(i => new LogItem
                {
                    Datetime = new DateTime(2026, 10, 18, 12, 0, 0).AddSeconds(i),
                    Message = "DS4Windows diagnostic log example " + i
                }).ToArray();
                var retention = (ComboBox)root.FindName("logRetentionCombo");
                retention.ItemsSource = new[] { 1, 7, 14, 30, 90 };
                retention.SelectedItem = 30;
                root.Measure(new Size(width, 480));
                root.Arrange(new Rect(0, 0, width, 480));
                root.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                root.UpdateLayout();
                Assert.IsTrue(list.ActualHeight >= 200, $"{theme}/{width}: only {list.ActualHeight} pixels remain for log entries.");
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(list));
                foreach (string name in new[] { "openLogFolderBtn", "reportIssueBtn", "clearStoredLogsBtn", "copyLogBtn", "exportLogBtn", "clearLogBtn", "logVerboseCk", "logRetentionCombo", "viiperDebugLoggingCk", "viiperDebugHelpBtn" })
                {
                    var element = (FrameworkElement)root.FindName(name);
                    Point point = element.TranslatePoint(new Point(), root);
                    Assert.IsTrue(element.ActualHeight >= 20 && point.X >= -1 && point.X + element.ActualWidth <= width + 1 &&
                        point.Y >= -1 && point.Y + element.ActualHeight <= 481, $"{name} is clipped at {theme}/{width}.");
                }
                string directory = Environment.GetEnvironmentVariable("DS4W_LOG_LAYOUT_ARTIFACTS");
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    var bitmap = new RenderTargetBitmap((int)width, 480, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"Log-{theme}-{width}.png"));
                    encoder.Save(stream);
                }
                list.ItemsSource = null;
            }
            catch (Exception exception) { failure = exception; }

        };
        HeadlessWpfDispatcher.Invoke(thread);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static DockPanel Load(string theme, [CallerFilePath] string source = "")
    {
        string forms = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "DS4Windows", "DS4Forms"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement tab = LocalizedSource.Load(Path.Combine(forms, "MainWindow.xaml")).Descendants(wpf + "TabItem")
            .Single(element => (string)element.Attribute(x + "Name") == "logTab");
        var markup = new XElement(tab.Element(wpf + "DockPanel")!);
        markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        foreach (XAttribute attribute in markup.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attribute.Name.LocalName is "Click" or "MouseDoubleClick" ||
                attribute.Value.StartsWith("{Binding") && attribute.Name.LocalName != "Text") attribute.Remove();
            else if (attribute.Value.StartsWith("{lex:Loc ")) attribute.Value = LocalizedSource.Resolve(attribute.Value);
        }
        XElement bridge = LocalizedSource.Load(Path.Combine(forms, "Themes", "BridgeShellStyles.xaml")).Root!;
        bridge.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
            new XElement(wpf + "ResourceDictionary", new XAttribute("Source", $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))));
        var root = (DockPanel)XamlReader.Parse(LocalizedSource.Markup(markup));
        root.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(LocalizedSource.Markup(bridge)));
        var list = (ListView)root.FindName("logListView");
        if (root.TryFindResource("ListViewStyle") is Style listStyle) list.Style = listStyle;
        if (root.TryFindResource("ListViewItemStyle") is Style itemStyle) list.ItemContainerStyle = itemStyle;
        if (root.TryFindResource("GridViewColumnHeaderStyle") is Style headerStyle)
            ((GridView)list.View).ColumnHeaderContainerStyle = headerStyle;
        list.Foreground = (Brush)root.FindResource("ForegroundColor");
        root.Background = (Brush)root.FindResource("BackgroundColor");
        TextElement.SetForeground(root, (Brush)root.FindResource("ForegroundColor"));
        TextElement.SetFontFamily(root, new FontFamily("Segoe UI"));
        TextElement.SetFontSize(root, 14);
        return root;
    }
}
