using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS4Windows;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class SliderStyleTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void SharedSlidersFollowLiveThemesAndKeepRangeCommandsTicksAndGeometry()
    {
        string imagePath = Path.Combine(TestContext.TestResultsDirectory, "slider-themes.png");
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var gallery = new StackPanel { Width = 660 };
            // Keep one implicit-style slider alive through all theme switches.
            var host = new Border { Width = 620, Padding = new Thickness(20) };
            var slider = new Slider { Minimum = -10, Maximum = 10, Value = 2,
                TickFrequency = 2, TickPlacement = TickPlacement.Both, LargeChange = 2, SmallChange = 1 };
            host.Child = slider;
            foreach (var choice in AppThemeCatalog.Choices.Where(c => c.Value > 0))
            {
                var theme = AppThemeCatalog.Create((AppThemeChoice)choice.Value, highContrast: false);
                host.Resources.MergedDictionaries.Clear();
                host.Resources.MergedDictionaries.Add(theme);
                host.SetResourceReference(Border.BackgroundProperty, "CardBackgroundColor");
                ThemeConsistencyValidation.Layout(host, 620, 80);
                var track = (Track)slider.Template.FindName("PART_Track", slider);
                Assert.IsNotNull(track, choice.SourceName);
                Assert.AreEqual(slider.Minimum, track.Minimum);
                Assert.AreEqual(slider.Maximum, track.Maximum);
                Assert.AreEqual(slider.Value, track.Value);
                Assert.AreEqual(20d, track.Thumb.ActualWidth);
                Assert.AreEqual(20d, track.Thumb.ActualHeight);
                Color Colour(string key) => ((SolidColorBrush)theme[key]).Color;
                Assert.AreEqual(Colour("AccentColor"), ((SolidColorBrush)track.DecreaseRepeatButton.Background).Color);
                var knob = (Border)track.Thumb.Template.FindName("Knob", track.Thumb);
                Assert.AreEqual(Colour("SliderThumbColor"), ((SolidColorBrush)knob.Background).Color);
                Assert.AreEqual(Colour("AccentColor"), ((SolidColorBrush)knob.BorderBrush).Color);
                // Routed commands used by track clicks and keyboard navigation.
                Slider.IncreaseLarge.Execute(null, slider);
                Assert.AreEqual(4d, slider.Value);
                Slider.DecreaseSmall.Execute(null, slider);
                Assert.AreEqual(3d, slider.Value);
                slider.Value = 2;
                foreach (string name in new[] { "BeforeTicks", "AfterTicks" })
                {
                    var ticks = (TickBar)slider.Template.FindName(name, slider);
                    Assert.AreEqual(Visibility.Visible, ticks.Visibility);
                    Assert.AreEqual(2d, ticks.TickFrequency);
                }
                slider.IsDirectionReversed = true;
                Assert.IsTrue(track.IsDirectionReversed);
                slider.IsDirectionReversed = false;
                slider.IsEnabled = false;
                Assert.AreEqual(Colour("MutedForegroundColor"), ((SolidColorBrush)knob.BorderBrush).Color);
                Assert.IsFalse(track.Thumb.IsEnabled);
                Assert.IsFalse(track.DecreaseRepeatButton.IsEnabled);
                slider.IsEnabled = true;
                Assert.AreEqual(Colour("AccentColor"), ((SolidColorBrush)knob.BorderBrush).Color);
                var row = new StackPanel { Margin = new Thickness(12) };
                row.Resources.MergedDictionaries.Add(theme);
                var label = new TextBlock { Text = choice.SourceName, Margin = new Thickness(0, 0, 0, 6) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundColor");
                row.Children.Add(label);
                row.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = 72 });
                row.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = 50, IsEnabled = false });
                var surface = new Border { Child = row };
                surface.SetResourceReference(Border.BackgroundProperty, "CardBackgroundColor");
                surface.Resources.MergedDictionaries.Add(theme);
                gallery.Children.Add(surface);
            }
            // A custom accent must propagate without recreating the thumb.
            host.Resources.MergedDictionaries[0] = AppThemeCatalog.Create(AppThemeChoice.Paper, "#A78BFA", highContrast: false);
            ThemeConsistencyValidation.Layout(host, 620, 80);
            var liveTrack = (Track)slider.Template.FindName("PART_Track", slider);
            var liveKnob = (Border)liveTrack.Thumb.Template.FindName("Knob", liveTrack.Thumb);
            Assert.AreEqual(((SolidColorBrush)host.FindResource("AccentColor")).Color,
                ((SolidColorBrush)liveKnob.BorderBrush).Color);
            slider.Orientation = Orientation.Vertical;
            ThemeConsistencyValidation.Layout(host, 620, 160);
            Assert.AreEqual(Orientation.Vertical, liveTrack.Orientation);
            Assert.AreEqual(20d, liveTrack.ActualWidth);
            ThemeConsistencyValidation.Layout(gallery, 660, 780);
            var bitmap = new RenderTargetBitmap(660, 780, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(gallery);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(imagePath); encoder.Save(stream);
        });
        TestContext.AddResultFile(imagePath);
    }
}
