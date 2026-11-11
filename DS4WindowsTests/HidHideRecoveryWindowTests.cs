using DS4Windows;
using DS4WinWPF.DS4Control;
using DS4WinWPF.DS4Forms;
using System.Windows;
using System.Windows.Controls;

namespace DS4WindowsTests;

public sealed class HidHideRecoveryWindowTests
{
    internal static void ValidateExplicitSelection()
    {
        var resources = Application.Current.Resources.MergedDictionaries;
        var original = resources[0];
        foreach (string theme in new[] { "DefaultTheme", "DarkTheme" })
        {
            resources[0] = new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml", UriKind.Relative) };
            const string id = @"HID\VID_054C&PID_0CE6\EXACT-PHYSICAL-INSTANCE";
            var snapshot = new HidHideConfigurationSnapshot(Array.Empty<string>(), new[] { id }, false, true);
            var window = new HidHideRecoveryWindow(new(snapshot, new[] { id }));
            try
            {
                var layout = (FrameworkElement)window.Content;
                layout.Measure(new Size(676, 430));
                layout.Arrange(new Rect(0, 0, 676, 430));
                var check = Descendants(layout).OfType<CheckBox>().Single();
                var action = Descendants(layout).OfType<Button>().Last();
                Assert.AreNotEqual(true, check.IsChecked);
                Assert.IsFalse(action.IsEnabled);
                Assert.AreEqual(id, ((TextBlock)check.Content).Text);
                check.IsChecked = true;
                Assert.IsTrue(action.IsEnabled);
                check.IsChecked = false;
                Assert.IsFalse(action.IsEnabled);
            }
            finally { resources[0] = original; }
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependency) continue;
            yield return dependency;
            foreach (var nested in Descendants(dependency)) yield return nested;
        }
    }
}
