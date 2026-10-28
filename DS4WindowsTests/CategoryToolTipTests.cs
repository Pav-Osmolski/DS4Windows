using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using DS4WinWPF.DS4Forms.Themes;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class CategoryToolTipTests
{
    [TestMethod]
    [DataRow("MainWindow.xaml", "SettingsCategory")]
    [DataRow("ProfileEditor.xaml", "ProfileAdvancedCategory")]
    public void WpfDiscoversDistinctHelpForEveryCategoryAndCanReturnToInputAndConnection(string file, string group)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var source = LocalizedSource.Load(Source(file));
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var nodes = source.Descendants(wpf + "RadioButton").Where(e => (string)e.Attribute("GroupName") == group).ToArray();
            var serviceType = typeof(FrameworkElement).Assembly.GetType("System.Windows.Controls.PopupControlService", true);
            var service = serviceType.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var findOwner = serviceType.GetMethod("FindToolTipOwner", BindingFlags.Instance | BindingFlags.NonPublic);
            var action = Enum.Parse(findOwner.GetParameters()[1].ParameterType, "Mouse");
            var begin = serviceType.GetMethod("BeginShowToolTip", BindingFlags.Instance | BindingFlags.NonPublic);
            var dismiss = serviceType.GetMethod("DismissToolTips", BindingFlags.Instance | BindingFlags.NonPublic);
            var pending = serviceType.GetProperty("PendingToolTip", BindingFlags.Instance | BindingFlags.NonPublic);
            var ownerProperty = (DependencyProperty)serviceType.GetField("OwnerProperty", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (var choice in new[] { AppThemeChoice.Light, AppThemeChoice.Dark, AppThemeChoice.Graphite, AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(choice, highContrast: false));
                row.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                foreach (var node in nodes)
                {
                    var markup = new XElement(node);
                    foreach (string remove in new[] { "Click", "Checked", "IsEnabled" }) markup.Attribute(remove)?.Remove();
                    markup.Attribute("Style")?.Remove();
                    var button = (RadioButton)XamlReader.Parse(LocalizedSource.Markup(markup));
                    button.SetResourceReference(FrameworkElement.StyleProperty, "BridgeSettingsCategoryStyle");
                    ToolTipService.SetInitialShowDelay(button, int.MaxValue); // exercise native pending ownership without opening desktop windows
                    row.Children.Add(button);
                }
                var tab = new TabItem { ToolTip = "Unrelated parent profile/settings tooltip", Content = row };
                var tabs = new TabControl { SelectedIndex = 0 };
                tabs.Resources = row.Resources;
                tabs.SetResourceReference(FrameworkElement.StyleProperty, "BridgeProfileTabControlStyle");
                tabs.Items.Add(tab);
                tabs.Measure(new Size(900, 150)); tabs.Arrange(new Rect(0, 0, 900, 150)); tabs.UpdateLayout();
                var buttons = row.Children.OfType<RadioButton>().ToArray();
                try
                {
                    dismiss.Invoke(service, null);
                    foreach (var button in buttons.Concat(buttons.Reverse()).Concat(buttons))
                    {
                        var chrome = (FrameworkElement)button.Template.FindName("CategoryChrome", button);
                        var pointer = Descendants(chrome).OfType<TextBlock>().FirstOrDefault() ?? (System.Windows.IInputElement)chrome;
                        var owner = (DependencyObject)findOwner.Invoke(service, new object[] { pointer, action });
                        Assert.AreSame(button, owner, choice + " " + button.Content + " must own its help instead of falling back to the containing TabItem.");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(button.ToolTip?.ToString()));
                        Assert.AreNotEqual(tab.ToolTip, button.ToolTip);
                        begin.Invoke(service, new object[] { owner, action });
                        var tip = (ToolTip)pending.GetValue(service);
                        Assert.IsNotNull(tip, "Returning to a category must still schedule help.");
                        Assert.AreSame(button, tip.GetValue(ownerProperty));
                    }
                }
                finally { dismiss.Invoke(service, null); }
            }
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static string Source(string file, [CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "..", "DS4Windows", "DS4Forms", file);
}
