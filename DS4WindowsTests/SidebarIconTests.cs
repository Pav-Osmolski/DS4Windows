using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Xml.Linq;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class SidebarIconTests
{
    [TestMethod]
    [DataRow(SidebarIconChoice.BlueTile)]
    [DataRow(SidebarIconChoice.MidnightCircle)]
    [DataRow(SidebarIconChoice.BlueFrame)]
    [DataRow(SidebarIconChoice.Controller)]
    [DataRow(SidebarIconChoice.AngularBadge)]
    public void SelectionRoundTripsWithoutChangingTrayChoice(SidebarIconChoice choice)
    {
        var dto = new AppSettingsDTO();
        dto.MapFrom(new BackingStore { sidebarIcon = choice, useIconChoice = TrayIconChoice.White });
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var writer = new StringWriter();
        serializer.Serialize(writer, dto);
        StringAssert.Contains(writer.ToString(), $"<SidebarIcon>{choice}</SidebarIcon>");
        using var reader = new StringReader(writer.ToString());
        var destination = new BackingStore();
        ((AppSettingsDTO)serializer.Deserialize(reader)).MapTo(destination);
        Assert.AreEqual(choice, destination.sidebarIcon);
        Assert.AreEqual(TrayIconChoice.White, destination.useIconChoice);
    }

    [TestMethod]
    [DataRow("<Profile />")]
    [DataRow("<Profile><SidebarIcon /></Profile>")]
    [DataRow("<Profile><SidebarIcon>removed-icon</SidebarIcon></Profile>")]
    [DataRow("<Profile><SidebarIcon>999</SidebarIcon></Profile>")]
    [DataRow("<Profile><SidebarIcon>-1</SidebarIcon></Profile>")]
    public void OldAndInvalidSettingsFallBackToBlueTile(string xml)
    {
        using var reader = new StringReader(xml);
        var dto = (AppSettingsDTO)new XmlSerializer(typeof(AppSettingsDTO)).Deserialize(reader);
        var destination = new BackingStore { sidebarIcon = SidebarIconChoice.Controller };
        dto.MapTo(destination);
        Assert.AreEqual(SidebarIconChoice.BlueTile, destination.sidebarIcon);
    }

    [TestMethod]
    public void ChoicesHaveRequestedOrderNamesAndNotifyOnlyForValidChanges()
    {
        var previous = Global.UseSidebarIcon;
        try
        {
            Global.UseSidebarIcon = SidebarIconChoice.BlueTile;
            var model = (SettingsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(SettingsViewModel));
            CollectionAssert.AreEqual(new[] { "Blue Tile", "Midnight Circle", "Blue Frame", "Controller", "Angular Badge" },
                model.SidebarIconChoices.Select(choice => choice.Name).ToArray());
            int changed = 0;
            model.SidebarIconIndexChanged += (_, _) => changed++;
            model.SidebarIconIndex = -1;
            model.SidebarIconIndex = 999;
            model.SidebarIconIndex = 0;
            Assert.AreEqual(0, changed);
            model.SidebarIconIndex = 4;
            Assert.AreEqual(SidebarIconChoice.AngularBadge, Global.UseSidebarIcon);
            Assert.AreEqual(1, changed);
        }
        finally { Global.UseSidebarIcon = previous; }
    }

    [TestMethod]
    [DataRow("DarkTheme")]
    [DataRow("DefaultTheme")]
    public void ActualPickerShowsEachPreviewAndUpdatesTheSavedChoice(string theme)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var previous = Global.UseSidebarIcon;
            try
            {
                Global.UseSidebarIcon = SidebarIconChoice.BlueTile;
                XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var markup = new XElement(LocalizedSource.Load(SourcePath()).Descendants(wpf + "ComboBox")
                    .Single(element => (string)element.Attribute(x + "Name") == "sidebarIconCombo"));
                markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                var combo = (ComboBox)XamlReader.Parse(LocalizedSource.Markup(markup));
                var bridge = LocalizedSource.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, "Themes", "BridgeShellStyles.xaml")).Root;
                bridge.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
                    new XElement(wpf + "ResourceDictionary", new XAttribute("Source",
                        $"/DS4Windows;component/DS4Forms/Themes/{theme}.xaml"))));
                combo.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(LocalizedSource.Markup(bridge)));
                combo.DataContext = RuntimeHelpers.GetUninitializedObject(typeof(SettingsViewModel));
                foreach (var choice in SidebarIconCatalog.Choices)
                {
                    combo.SelectedIndex = (int)choice.Value;
                    combo.Measure(new Size(220, 60));
                    combo.Arrange(new Rect(0, 0, 220, combo.DesiredSize.Height));
                    combo.UpdateLayout();
                    Assert.AreEqual(choice.Value, Global.UseSidebarIcon);
                    Assert.AreSame(choice.Image, Visuals(combo).OfType<Image>().Single().Source);
                    Assert.IsTrue(Visuals(combo).OfType<TextBlock>().Any(text => text.Text == choice.Name));
                }
            }
            finally { Global.UseSidebarIcon = previous; }
        });
    }

    [TestMethod]
    [DataRow("DarkTheme")]
    [DataRow("DefaultTheme")]
    public void ActualSidebarUpdatesKeepsCompactLayoutAndSurvivesThemeChanges(string theme)
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(Theme(theme));
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative),
            });
            var tabs = new TabControl { Resources = resources, Style = (Style)resources["BridgeMainTabControlStyle"] };
            tabs.Items.Add(new TabItem { Header = "Overview", Content = new Grid() });
            Layout(tabs);
            var icon = (Image)tabs.Template.FindName("SidebarBrandIconImage", tabs);
            Assert.IsNotNull(icon);
            foreach (var choice in SidebarIconCatalog.Choices)
            {
                var bitmap = (BitmapSource)choice.Image;
                Assert.IsTrue(bitmap.IsFrozen);
                Assert.AreEqual(1254, bitmap.PixelWidth);
                Assert.AreEqual(1254, bitmap.PixelHeight);
                var rgba = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                byte[] corner = new byte[4];
                rgba.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
                Assert.AreEqual((byte)0, corner[3], "Transparent PNG corners must remain transparent.");
                SidebarIconCatalog.Apply(resources, choice.Value);
                Layout(tabs);
                Assert.AreSame(choice.Image, icon.Source, "Changing the resource must update the live template.");
                Assert.AreEqual(44d, icon.ActualWidth);
                Assert.AreEqual(44d, icon.ActualHeight);
                Assert.AreEqual(60d, ((Panel)icon.Parent).ActualHeight, "Keep the header and content below it in place.");
                var title = ((Panel)icon.Parent).Children.OfType<StackPanel>().Single();
                var header = (Panel)icon.Parent;
                var headerArea = (FrameworkElement)header.Parent;
                Assert.AreEqual(headerArea.ActualWidth / 2, header.TranslatePoint(new Point(), headerArea).X + header.ActualWidth / 2, 0.5d,
                    "Center the logo and title as one group within the sidebar.");
                Assert.AreEqual(icon.TranslatePoint(new Point(), tabs).Y + icon.ActualHeight / 2, title.TranslatePoint(new Point(), tabs).Y + title.ActualHeight / 2, 0.01d, "Center the icon with both title lines.");
                Assert.IsTrue(title.TranslatePoint(new Point(), tabs).X + title.ActualWidth <= 238,
                    "The compact icon must leave room for the application name.");
                foreach (double scale in new[] { 1d, 1.5d, 2d, 3d }) Save(tabs, theme, choice.Value, scale);
                resources.MergedDictionaries[0] = Theme(theme == "DarkTheme" ? "DefaultTheme" : "DarkTheme");
                Layout(tabs);
                Assert.AreSame(choice.Image, icon.Source, "Theme changes must retain the selected image.");
                resources.MergedDictionaries[0] = Theme(theme);
            }
        });
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(137d)]
    [DataRow(360d)]
    public void HueRoundTripsWithIconAndTraySettings(double hue)
    {
        var dto = new AppSettingsDTO();
        dto.MapFrom(new BackingStore { sidebarIconHue = hue, sidebarIcon = SidebarIconChoice.AngularBadge, useIconChoice = TrayIconChoice.White });
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var writer = new StringWriter();
        serializer.Serialize(writer, dto);
        using var reader = new StringReader(writer.ToString());
        var destination = new BackingStore();
        ((AppSettingsDTO)serializer.Deserialize(reader)).MapTo(destination);
        Assert.AreEqual(hue, destination.sidebarIconHue);
        Assert.AreEqual(SidebarIconChoice.AngularBadge, destination.sidebarIcon);
        Assert.AreEqual(TrayIconChoice.White, destination.useIconChoice);
    }

    [TestMethod]
    public void HueRotationPreservesTransparencyAndNeutralColours()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            byte[] pixels = [0, 0, 255, 128, 255, 255, 255, 255, 100, 100, 100, 255, 9, 8, 7, 0];
            var source = BitmapSource.Create(4, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
            byte[] shifted = new byte[16];
            SidebarIconCatalog.RotateHue(source, 120).CopyPixels(shifted, 16, 0);
            CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 128, 255, 255, 255, 255, 100, 100, 100, 255, 9, 8, 7, 0 }, shifted);
            SidebarIconCatalog.RotateHue(source, 360).CopyPixels(shifted, 16, 0);
            CollectionAssert.AreEqual(pixels, shifted);
        });
    }

    [TestMethod]
    public void ActualHueSliderUpdatesSavedValueAndAllPreviewsThenResets()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            double previousHue = Global.SidebarIconHue;
            try
            {
                Global.SidebarIconHue = 0;
                var resources = new ResourceDictionary();
                resources.MergedDictionaries.Add(Theme("DarkTheme"));
                SidebarIconCatalog.Apply(resources, SidebarIconChoice.BlueTile);
                var original = SidebarIconCatalog.Choices.Select(c => c.Image).ToArray();
                XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var markup = new XElement(LocalizedSource.Load(SourcePath()).Descendants(wpf + "Slider")
                    .Single(e => (string)e.Attribute(x + "Name") == "sidebarHueSlider"));
                markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                var slider = (Slider)XamlReader.Parse(LocalizedSource.Markup(markup));
                var model = (SettingsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(SettingsViewModel));
                model.SidebarIconHueChanged += (_, _) => SidebarIconCatalog.Apply(resources, SidebarIconChoice.BlueTile, model.SidebarIconHue);
                slider.DataContext = model;
                var comboMarkup = new XElement(LocalizedSource.Load(SourcePath()).Descendants(wpf + "ComboBox")
                    .Single(e => (string)e.Attribute(x + "Name") == "sidebarIconCombo"));
                comboMarkup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                var combo = (ComboBox)XamlReader.Parse(LocalizedSource.Markup(comboMarkup));
                var bridge = LocalizedSource.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, "Themes", "BridgeShellStyles.xaml")).Root;
                bridge.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
                    new XElement(wpf + "ResourceDictionary", new XAttribute("Source", "/DS4Windows;component/DS4Forms/Themes/DarkTheme.xaml"))));
                combo.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(LocalizedSource.Markup(bridge)));
                combo.DataContext = model;
                combo.Measure(new Size(220, 60)); combo.Arrange(new Rect(0, 0, 220, 60)); combo.UpdateLayout();
                var preview = Visuals(combo).OfType<Image>().Single();
                slider.Measure(new Size(300, 40));
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                slider.SetCurrentValue(Slider.ValueProperty, 120d);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Assert.AreEqual(120d, slider.Value, $"binding={slider.GetBindingExpression(Slider.ValueProperty)?.Status}; error={slider.GetBindingExpression(Slider.ValueProperty)?.HasError}; max={slider.Maximum}");
                Assert.AreEqual(120d, Global.SidebarIconHue);
                for (int i = 0; i < original.Length; i++) Assert.AreNotSame(original[i], SidebarIconCatalog.Choices[i].Image);
                Assert.AreSame(SidebarIconCatalog.Choices[0].Image, resources[SidebarIconCatalog.ResourceKey]);
                Assert.AreSame(SidebarIconCatalog.Choices[model.SidebarIconIndex].Image, preview.Source, "The visible dropdown preview must update without changing selection.");
                model.SidebarIconHue = 0;
                slider.GetBindingExpression(Slider.ValueProperty)!.UpdateTarget();
                Assert.AreEqual(0d, slider.Value);
                for (int i = 0; i < original.Length; i++) Assert.AreSame(original[i], SidebarIconCatalog.Choices[i].Image);
                Assert.AreEqual(0d, SidebarIconPolicy.NormalizeHue(double.NaN));
                Assert.AreEqual(0d, SidebarIconPolicy.NormalizeHue(double.PositiveInfinity));
                Assert.AreEqual(0d, SidebarIconPolicy.NormalizeHue(-1));
                Assert.AreEqual(360d, SidebarIconPolicy.NormalizeHue(361));
            }
            finally
            {
                Global.SidebarIconHue = previousHue;
                SidebarIconCatalog.Apply(new ResourceDictionary(), Global.UseSidebarIcon, previousHue);
            }
        });
    }

    private static ResourceDictionary Theme(string name) => new()
    {
        Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{name}.xaml", UriKind.Relative),
    };

    private static string SourcePath([CallerFilePath] string caller = "") =>
        Path.Combine(Path.GetDirectoryName(caller)!, "..", "DS4Windows", "DS4Forms", "MainWindow.xaml");

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(1180, 760));
        element.Arrange(new Rect(0, 0, 1180, 760));
        element.UpdateLayout();
    }

    private static void Save(FrameworkElement element, string theme, SidebarIconChoice choice, double scale)
    {
        string directory = Environment.GetEnvironmentVariable("DS4W_SIDEBAR_ICON_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(238 * scale), (int)(100 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"{theme}-{choice}-{scale * 100:0}.png"));
        encoder.Save(file);
    }
}
