using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms.Themes;
using DS4WinWPF.DS4Forms.ViewModels;
using Xceed.Wpf.Toolkit;

namespace DS4WindowsTests;

internal static class DS3PressureEditorValidation
{
    internal static void Validate(string directory, List<string> images)
    {
        var storeField = typeof(Global).GetField("m_Config", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = Global.store;
        storeField.SetValue(null, new BackingStore());
        try
        {
            foreach (string theme in new[] { "DefaultTheme", "DarkTheme", "Graphite", "Paper", "Midnight", "HighContrast" })
            {
                string basis = theme is "DefaultTheme" or "Paper" ? "DefaultTheme" : "DarkTheme";
                Grid root = ProfileAdvancedLayoutTests.Load(basis, "AxisConfig");
                if (theme is not "DefaultTheme" and not "DarkTheme")
                    root.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(Enum.Parse<AppThemeChoice>(theme), highContrast: false));
                var vm = new ProfileSettingsViewModel(8, InputDeviceType.DS3);
                root.DataContext = vm;
                var category = (RadioButton)root.FindName("profileAxis4Nav");
                var start = (IntegerUpDown)root.FindName("ds3PressureStart");
                var full = (IntegerUpDown)root.FindName("ds3PressureFull");
                // The layout fixture strips VM bindings; restore the actual page's four range bindings.
                start.SetBinding(IntegerUpDown.ValueProperty, new Binding(nameof(vm.DS3PressureStartPercent)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                start.SetBinding(IntegerUpDown.MaximumProperty, new Binding(nameof(vm.DS3PressureStartMaximum)));
                full.SetBinding(IntegerUpDown.ValueProperty, new Binding(nameof(vm.DS3PressureFullPercent)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                full.SetBinding(IntegerUpDown.MinimumProperty, new Binding(nameof(vm.DS3PressureFullMinimum)));
                vm.DS3PressureStartPercent = 0; vm.DS3PressureFullPercent = 100;
                category.IsChecked = true;
                ThemeConsistencyValidation.Layout(root, 980, 620);
                full.Value = 80; start.Value = 10;
                root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.AreEqual(10, vm.DS3PressureStartPercent); Assert.AreEqual(80, vm.DS3PressureFullPercent);
                Assert.AreEqual(79, start.Maximum); Assert.AreEqual(11, full.Minimum);
                var dto = new ProfileDTO { DeviceIndex = 8 }; dto.MapFrom(Global.store);
                Assert.AreEqual(10, dto.DS3PressureStartPercent); Assert.AreEqual(80, dto.DS3PressureFullPercent);
                vm.DS3PressureStartPercent = 999; Assert.AreEqual(79, vm.DS3PressureStartPercent);
                vm.DS3PressureFullPercent = 0; Assert.AreEqual(80, vm.DS3PressureFullPercent);
                vm.DS3PressureStartPercent = 10;
                foreach (int width in new[] { 480, 760, 980 })
                {
                    ThemeConsistencyValidation.Layout(root, width, 620);
                    Assert.IsTrue(category.IsChecked == true);
                    Assert.AreEqual(Visibility.Visible, ((ScrollViewer)root.FindName("ds3PressureScroll")).Visibility);
                    Assert.IsGreaterThan(0d, start.ActualWidth); Assert.IsGreaterThan(0d, full.ActualWidth);
                    var startBounds = new Rect(start.TranslatePoint(new Point(), root), start.RenderSize);
                    var fullBounds = new Rect(full.TranslatePoint(new Point(), root), full.RenderSize);
                    Assert.AreEqual(startBounds.Left, fullBounds.Left, 0.1, "Align both percentage fields.");
                    Assert.IsTrue(startBounds.Right <= width && fullBounds.Right <= width);
                    Assert.IsTrue(startBounds.Bottom < fullBounds.Top, "Keep a consistent field gap.");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(start.ToolTip?.ToString()));
                    Assert.IsFalse(string.IsNullOrWhiteSpace(full.ToolTip?.ToString()));
                    var image = new RenderTargetBitmap(width, 620, 96, 96, PixelFormats.Pbgra32); image.Render(root);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                    string path = Path.Combine(directory, $"ds3-pressure-editor-{theme}-{width}.png");
                    using (var stream = File.Create(path)) png.Save(stream);
                    images.Add(path);
                }
            }
        }
        finally { storeField.SetValue(null, previous); }
    }
}
