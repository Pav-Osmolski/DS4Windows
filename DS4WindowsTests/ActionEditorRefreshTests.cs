using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4WinWPF;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class ActionEditorRefreshTests
{
    [TestMethod]
    [DataRow(AppThemeChoice.Light)] [DataRow(AppThemeChoice.Dark)] [DataRow(AppThemeChoice.Graphite)]
    [DataRow(AppThemeChoice.Paper)] [DataRow(AppThemeChoice.Midnight)] [DataRow(AppThemeChoice.HighContrast)]
    public void EditorUsesThemeCardsAndReachableOptionsWithoutLegacyTabStrip(AppThemeChoice theme)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (string type in new[] { "DisconnectBT", "MultiAction" })
            foreach (double width in new[] { 720d, 1100d })
            {
                var action = new SpecialAction("Controller shortcut", "PS", type,
                    type == "MultiAction" ? "49/49,52/52,50/50,51/51" : "0");
                var editor = new SpecialActionEditor(Global.TEST_PROFILE_INDEX, new ProfileList(), action);
                editor.Resources.MergedDictionaries.Add(ThemeResources(theme));
                editor.Background = (Brush)editor.FindResource("BackgroundColor");
                editor.Measure(new Size(width, 680)); editor.Arrange(new Rect(0, 0, width, 680)); editor.UpdateLayout();
                var tabs = (TabControl)editor.FindName("actionTypeTabControl");
                Assert.AreEqual(0, tabs.BorderThickness.Bottom);
                Assert.IsTrue(tabs.ActualHeight > 300);
                var save = (Button)editor.FindName("saveBtn");
                var cancel = (Button)editor.FindName("cancelBtn");
                Assert.AreEqual(save.ActualHeight, cancel.ActualHeight);
                Assert.IsTrue(save.ActualHeight >= 36);
                var position = save.TranslatePoint(new Point(), editor);
                Assert.IsTrue(position.Y + save.ActualHeight <= 680);
                Assert.IsTrue(position.X + save.ActualWidth <= width);
                var viewer = (ScrollViewer)editor.FindName("triggersListView");
                Assert.AreEqual(0, viewer.VerticalOffset);
                Assert.AreEqual("Cross", editor.TriggerCheckBoxes[0].Tag);
                Assert.IsTrue(viewer.ScrollableHeight > 0);
                foreach (var numeric in Visuals(tabs).OfType<Xceed.Wpf.Toolkit.DecimalUpDown>())
                    Assert.AreEqual(36d, numeric.ActualHeight, "Numeric fields must not stretch to fill the options panel.");
                if (type == "MultiAction")
                {
                    var triple = (Button)editor.FindName("multiTripleTapTrigBtn");
                    Assert.IsTrue(triple.ActualWidth > 100);
                    var optionScroll = (ScrollViewer)((TabItem)editor.FindName("multiActTab")).Content;
                    optionScroll.ScrollToEnd(); editor.UpdateLayout();
                    Assert.IsTrue(optionScroll.VerticalOffset > 0);
                    optionScroll.ScrollToTop(); editor.UpdateLayout();
                }
                var selector = (ComboBox)editor.FindName("actionTypeCombo");
                int selected = selector.SelectedIndex;
                selector.SelectedIndex = 0;
                selector.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty).UpdateSource();
                editor.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.AreEqual(0, tabs.SelectedIndex); Assert.IsFalse(save.IsEnabled);
                selector.SelectedIndex = selected;
                selector.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty).UpdateSource();
                editor.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.AreEqual(selected, tabs.SelectedIndex); Assert.IsTrue(save.IsEnabled);
                editor.UpdateLayout();
                string directory = Environment.GetEnvironmentVariable("DS4W_PROFILE_LAYOUT_ARTIFACTS");
                if (string.IsNullOrEmpty(directory)) continue;
                Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)width, 680, 96, 96, PixelFormats.Pbgra32); bitmap.Render(editor);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, $"EditAction-{theme}-{type}-{width}.png")); encoder.Save(file);
            }
        });
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    internal static ResourceDictionary ThemeResources(AppThemeChoice theme, [CallerFilePath] string sourceFile = "")
    {
        XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile), "..", "DS4Windows", "DS4Forms", "Themes", "BridgeShellStyles.xaml"));
        var dictionary = LocalizedSource.Load(path).Root;
        dictionary.AddFirst(new XElement(w + "ResourceDictionary.MergedDictionaries",
            new XElement(w + "ResourceDictionary", new XAttribute("Source", "/DS4Windows;component/DS4Forms/Themes/DefaultTheme.xaml"))));
        var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        resources.MergedDictionaries.Add(AppThemeCatalog.Create(theme, "Theme"));
        return resources;
    }

    [TestMethod]
    public void InlineRenameCanSwitchBackToDuplicateAndRetainsCollisionProtection()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            string oldRoot = Global.appdatapath;
            string root = Directory.CreateTempSubdirectory("ds4w-inline-rename-").FullName;
            try
            {
                Global.appdatapath = root;
                Directory.CreateDirectory(Path.Combine(root, "Profiles"));
                string original = Path.Combine(root, "Profiles", "Source.xml");
                File.WriteAllText(original, "<DS4Windows/>");
                var entity = new ProfileEntity { Name = "Source" };
                var prompt = new DupBox();
                int renamed = 0, duplicated = 0;
                prompt.Renamed += (_, _) => renamed++;
                prompt.Save += (_, _) => duplicated++;
                var field = (TextBox)prompt.FindName("profileTxt");
                var button = (Button)prompt.FindName("saveBtn");
                prompt.BeginRename(entity);
                Assert.IsTrue(prompt.IsRenameMode); Assert.AreEqual("Source", field.Text);
                prompt.OldFilename = "Source";
                Assert.IsFalse(prompt.IsRenameMode); Assert.AreEqual("", field.Text);
                prompt.BeginRename(entity);
                File.WriteAllText(Path.Combine(root, "Profiles", "Taken.xml"), "taken");
                field.Text = "Taken";
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.AreEqual(0, renamed); Assert.IsTrue(File.Exists(original));
                Assert.AreEqual(Visibility.Visible, ((TextBlock)prompt.FindName("validationMessage")).Visibility);
                field.Text = "Renamed";
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.AreEqual(1, renamed); Assert.AreEqual(0, duplicated);
                Assert.AreEqual("Renamed", entity.Name); Assert.IsFalse(File.Exists(original));
                Assert.AreEqual("<DS4Windows/>", File.ReadAllText(Path.Combine(root, "Profiles", "Renamed.xml")));
                prompt.OldFilename = "Renamed"; field.Text = "Copy";
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.AreEqual(1, duplicated); Assert.IsTrue(File.Exists(Path.Combine(root, "Profiles", "Renamed.xml")));
            }
            finally { Global.appdatapath = oldRoot; Directory.Delete(root, true); }
        });
    }
}
