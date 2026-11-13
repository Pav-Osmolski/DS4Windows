using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF.DS4Forms.ViewModels;
using DS4WinWPF.DS4Forms;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ControllerOverviewAudioTests
{
    [TestMethod]
    [DataRow(InputDeviceType.Switch2Pro, ConnectionType.USB, true)]
    [DataRow(InputDeviceType.Switch2Pro, ConnectionType.BT, true)]
    [DataRow(InputDeviceType.Switch2JoyConLeft, ConnectionType.BT, false)]
    [DataRow(InputDeviceType.Switch2JoyConRight, ConnectionType.BT, false)]
    [DataRow(InputDeviceType.Switch2JoyConJoined, ConnectionType.BT, false)]
    [DataRow(InputDeviceType.SwitchPro, ConnectionType.USB, false)]
    public void UnsupportedAudioBindingsCannotRewriteSharedProfile(
        InputDeviceType type, ConnectionType connection, bool usbHelp)
    {
        using var profile = new AudioProfileScope();
        var model = Model(new PhysicalDevice(type, connection));
        Assert.IsFalse(model.SelectedControllerSupportsAudio);
        Assert.AreEqual(usbHelp, model.SelectedControllerShowsUsbHeadsetHelp);
        int changes = 0;
        model.QuickProfileSettingChanged += (_, _) => changes++;
        var before = profile.Snapshot();
        _ = model.ControllerAudioSourceId;
        model.SpeakerOutputEnabled = false;
        model.HeadsetOnlyAudio = false;
        model.MicrophoneInputEnabled = false;
        model.SpeakerVolumePercent = 99;
        model.HeadphoneVolumePercent = 99;
        model.MicrophoneVolumePercent = 99;
        model.SpeakerCompressionIndex = 2;
        model.SpeakerBassBoostDb = 6;
        model.MicrophoneNoiseSuppressionIndex = 2;
        model.ControllerAudioSourceId = "must-not-select-another-device";
        CollectionAssert.AreEqual(before, profile.Snapshot());
        Assert.AreEqual(0, changes, "An inapplicable binding must not save/reload the active profile.");
    }

    [TestMethod]
    public void GenuineDualSenseStillEditsItsManagedSpeakerLevel()
    {
        using var profile = new AudioProfileScope();
        var model = Model(new PhysicalDevice(InputDeviceType.DualSense, ConnectionType.USB, sony: true));
        Assert.IsTrue(model.SelectedControllerSupportsAudio);
        Assert.IsFalse(model.SelectedControllerShowsUsbHeadsetHelp);
        int changes = 0;
        model.QuickProfileSettingChanged += (_, _) => changes++;
        model.SpeakerVolumePercent = 100;
        Assert.AreEqual(byte.MaxValue, Global.DualSenseSpeakerVolume[0]);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void EmptyOverviewDoesNotAdvertiseAConnectedUsbHeadset()
    {
        var model = new MainWindowsViewModel();
        Assert.IsFalse(model.SelectedControllerShowsUsbHeadsetHelp);
        Assert.IsFalse(model.SelectedControllerSupportsAudio);
    }

    [TestMethod]
    public void EveryOverviewAudioControlHasThePhysicalCapabilityGuard()
    {
        var xml = LoadOverview();
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (string binding in new[] { "SpeakerOutputEnabled", "ControllerAudioRoute",
            "SpeakerVolumePercent", "HeadphoneVolumePercent", "MicrophoneInputEnabled",
            "MicrophoneVolumePercent", "SpeakerCompressionIndex", "SpeakerBassBoostDb",
            "MicrophoneNoiseSuppressionIndex", "ControllerAudioSourceId" })
        {
            var controls = xml.Descendants().Where(n => n.Attributes().Any(a =>
                a.Value.StartsWith("{Binding " + binding + ",", StringComparison.Ordinal))).ToArray();
            Assert.IsTrue(controls.Length > 0, binding);
            foreach (var control in controls)
                Assert.IsTrue(control.Ancestors().Any(n => ((string)n.Attribute("Visibility"))?
                    .Contains("SelectedControllerSupportsAudio") == true), binding);
        }
        var native = xml.Descendants(wpf + "Border").Single(n =>
            (string)n.Attribute(xaml + "Name") == "nativeUsbHeadsetCard");
        StringAssert.Contains((string)native.Attribute("Visibility"), "SelectedControllerShowsUsbHeadsetHelp");
        Assert.AreEqual("OpenWindowsSoundSettings_Click", (string)native.Descendants(wpf + "Button").Single().Attribute("Click"));
        Assert.IsFalse(native.Descendants(wpf + "Slider").Any());
    }

    private static MainWindowsViewModel Model(DS4Device device, string profileName = "Test profile")
    {
        var model = new MainWindowsViewModel();
        // Avoid invoking live runtime/endpoint discovery in a pure binding test.
        var card = new CompositeDeviceModel(device, 0, profileName, new DS4WinWPF.ProfileList());
        typeof(MainWindowsViewModel).GetField("selectedController", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, card);
        var status = ControllerRuntimeStatusPolicy.Evaluate(new ControllerRuntimeSignals(
            true, true, true, true, !string.IsNullOrEmpty(profileName), true,
            ControllerRuntimeLaneState.NotRequired, ControllerRuntimeLaneState.NotRequired,
            ControllerRuntimeLaneState.NotRequired, ControllerRuntimeLaneState.NotRequired,
            "Xbox 360", profileSelected: !string.IsNullOrEmpty(profileName)));
        typeof(MainWindowsViewModel).GetField("selectedControllerStartupStatus", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, status);
        return model;
    }

    // Called on the existing theme test's STA. There can be only one WPF
    // Application per process, so do not create a competing test app/window.
    internal static void ValidateRenderedOverview(ControllerOverviewControl overview,
        string resultsDirectory, List<string> renderedFiles)
    {
        ValidateImmediateSliderLabels(overview);
        var resources = Application.Current.Resources.MergedDictionaries;
        var originalTheme = resources[0];
        foreach (string name in new[] { "DefaultTheme", "DarkTheme" })
        {
            resources[0] = new ResourceDictionary { Source = new Uri($"/DS4Windows;component/DS4Forms/Themes/{name}.xaml", UriKind.Relative) };
            var themedOverview = new ControllerOverviewControl();
            themedOverview.SetResourceReference(Control.ForegroundProperty, "ForegroundColor");
            themedOverview.SetResourceReference(Control.BackgroundProperty, "SurfaceBackgroundColor");
            var protectedController = new PhysicalDevice(InputDeviceType.DualSense, ConnectionType.USB, sony: true)
            {
                CurrentExclusiveStatus = DS4Device.ExclusiveStatus.HidHideAffected
            };
            themedOverview.DataContext = Model(protectedController);
            ThemeConsistencyValidation.Layout(themedOverview, 1100, 950);
            var fill = (Border)themedOverview.FindName("overviewBatteryFill");
            foreach (var (percent, brush) in new[]
            {
                (100, "SuccessColor"), (50, "SuccessColor"), (25, "SuccessColor"),
                (24, "WarningColor"), (11, "WarningColor"), (10, "DangerColor"), (0, "DangerColor")
            })
            {
                protectedController.SetBattery(percent);
                themedOverview.DataContext = Model(protectedController);
                themedOverview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.AreEqual(percent / 100d, ((ScaleTransform)fill.RenderTransform).ScaleX, 0.001);
                Assert.AreEqual(((SolidColorBrush)themedOverview.FindResource(brush)).Color,
                    ((SolidColorBrush)fill.Background).Color);
            }

            var model = Model(protectedController, "");
            themedOverview.DataContext = model;
            Assert.AreEqual("Select a profile", model.SelectedControllerStartupTitle);
            themedOverview.Measure(new Size(1100, 950));
            themedOverview.Arrange(new Rect(0, 0, 1100, 950));
            themedOverview.UpdateLayout();
            themedOverview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(((FrameworkElement)themedOverview.FindName("quickProfileControls")).IsEnabled);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)themedOverview.FindName("profileSelectionNotice")).Visibility);
            Assert.IsTrue(((Button)themedOverview.FindName("selectProfileButton")).IsEnabled);
            Assert.AreEqual(((SolidColorBrush)Application.Current.FindResource("AccentColor")).Color,
                ((SolidColorBrush)((TextBlock)themedOverview.FindName("controllerStartupTitle")).Foreground).Color,
                "Profile guidance must retain readable contrast in light and dark themes.");
            var image = new RenderTargetBitmap(1100, 950, 96, 96, PixelFormats.Pbgra32);
            image.Render(themedOverview);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            Directory.CreateDirectory(resultsDirectory);
            string path = Path.Combine(resultsDirectory, $"overview-no-profile-{name}.png");
            using (var stream = File.Create(path)) encoder.Save(stream);
            renderedFiles.Add(path);
            themedOverview.DataContext = Model(protectedController);
            themedOverview.UpdateLayout();
            Assert.IsTrue(((FrameworkElement)themedOverview.FindName("quickProfileControls")).IsEnabled);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)themedOverview.FindName("profileSelectionNotice")).Visibility);
            Assert.AreEqual(DS4WinWPF.Translations.Strings.HidHideAccess,
                ((TextBlock)themedOverview.FindName("overviewAccessStatus")).Text);
            var audioCard = (Border)themedOverview.FindName("managedSpeakerRouteCard");
            var microphoneCard = (Border)themedOverview.FindName("overviewMicrophoneCardBorder");
            double Bottom(Border card) => card.TranslatePoint(new Point(0, card.ActualHeight), themedOverview).Y;
            Assert.AreEqual(Bottom(audioCard), Bottom(microphoneCard), 0.1);

            themedOverview.Measure(new Size(660, 430));
            themedOverview.Arrange(new Rect(0, 0, 660, 430));
            themedOverview.UpdateLayout();
            Assert.IsTrue(Bottom(audioCard) < Bottom(microphoneCard));
            var scroll = (ScrollViewer)themedOverview.FindName("overviewScroll");
            var actions = (Panel)themedOverview.FindName("overviewQuickActions");
            Assert.IsNull(actions.Background);
            Assert.IsGreaterThan(0d, scroll.ScrollableHeight);
            var initialPosition = actions.TranslatePoint(new Point(), themedOverview);
            foreach (Button button in actions.Children)
            {
                double top = button.TranslatePoint(new Point(), themedOverview).Y;
                Assert.AreEqual(16d, top - scroll.ActualHeight, 0.1);
                Assert.AreEqual(16d, themedOverview.ActualHeight - top - button.ActualHeight, 0.1);
            }
            scroll.ScrollToEnd();
            themedOverview.UpdateLayout();
            Assert.AreEqual(initialPosition, actions.TranslatePoint(new Point(), themedOverview),
                "Quick actions must stay above the footer while Overview scrolls.");

            var showActions = (CheckBox)themedOverview.FindName("showQuickActions");
            var showOptions = (CheckBox)themedOverview.FindName("showDeviceOptionsAction");
            var optionsButton = (Button)themedOverview.FindName("overviewDeviceOptionsButton");
            var controlButton = (Button)themedOverview.FindName("overviewControlPanelButton");
            var infoButton = (Button)themedOverview.FindName("overviewControllerInfoButton");
            showOptions.IsChecked = false;
            themedOverview.UpdateLayout();
            Assert.AreEqual(Visibility.Collapsed, optionsButton.Visibility);
            Assert.AreEqual(Visibility.Visible, infoButton.Visibility, "Controller info belongs to the controller card.");
            showActions.IsChecked = false;
            themedOverview.UpdateLayout();
            Assert.AreEqual(Visibility.Collapsed, actions.Visibility);
            Assert.AreEqual(Visibility.Visible, scroll.Visibility, "Hiding the action bar must not hide Overview.");
            showActions.IsChecked = true;
            themedOverview.UpdateLayout();
            Assert.AreEqual(Visibility.Visible, actions.Visibility);
            Assert.AreEqual(Visibility.Collapsed, optionsButton.Visibility, "Restoring the bar must retain button choices.");
            showOptions.IsChecked = true;
            bool optionsRequested = false, panelRequested = false, infoRequested = false;
            themedOverview.DeviceOptionsRequested += (_, _) => optionsRequested = true;
            themedOverview.ControlPanelRequested += (_, _) => panelRequested = true;
            themedOverview.ControllerInfoRequested += (_, _) => infoRequested = true;
            optionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            controlButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            infoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(optionsRequested && panelRequested && infoRequested);
            Assert.IsFalse(infoButton.IsDescendantOf(actions));
            var showPanel = (CheckBox)themedOverview.FindName("showControlPanelAction");
            showPanel.IsChecked = false;
            themedOverview.UpdateLayout();
            Assert.AreEqual(Visibility.Collapsed, controlButton.Visibility);
            showPanel.IsChecked = true;
            themedOverview.UpdateLayout();

            var titleToggle = (CheckBox)themedOverview.FindName("showFactTitles");
            var titles = new[] { "overviewConnectionTitle", "overviewInputRateTitle", "overviewAccessTitle", "overviewOutputTitle" }
                .Select(n => (TextBlock)themedOverview.FindName(n)).ToArray();
            Rect Bounds(FrameworkElement element) => new(element.TranslatePoint(new Point(), themedOverview), element.RenderSize);
            var headers = titles.Select(t => (DockPanel)t.Parent).ToArray();
            var icons = headers.Select(h => (FrameworkElement)h.Children[0]).ToArray();
            var values = headers.Select(h => (FrameworkElement)((Panel)h.Parent).Children[1]).ToArray();
            var iconBounds = icons.Select(Bounds).ToArray();
            var valueBounds = values.Select(Bounds).ToArray();
            var headerBounds = headers.Select(Bounds).ToArray();
            var cards = (FrameworkElement)themedOverview.FindName("quickProfileControls");
            var cardsBefore = Bounds(cards);
            titleToggle.IsChecked = false;
            themedOverview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            themedOverview.UpdateLayout();
            for (int i = 0; i < titles.Length; i++)
            {
                Assert.AreEqual(Visibility.Hidden, titles[i].Visibility);
                Assert.AreEqual(iconBounds[i], Bounds(icons[i]), "Icon positions and size must not change.");
                Assert.AreEqual(valueBounds[i], Bounds(values[i]), "Values must retain their original line height and position.");
                Assert.AreEqual(headerBounds[i], Bounds(headers[i]));
            }
            Assert.AreEqual(cardsBefore, Bounds(cards), "Hiding titles must not shift the rest of Overview.");
            Assert.AreEqual(Visibility.Visible, ((TextBlock)themedOverview.FindName("overviewAccessStatus")).Visibility);
            titleToggle.IsChecked = true;
            var levels = (FrameworkElement)themedOverview.FindName("overviewLevelsCard");
            var showLevels = (CheckBox)themedOverview.FindName("showLevels");
            var microphone = (FrameworkElement)themedOverview.FindName("overviewMicrophoneCard");
            showLevels.IsChecked = false;
            themedOverview.UpdateLayout();
            Assert.AreEqual(Visibility.Collapsed, levels.Visibility);
            Assert.AreEqual(Visibility.Visible, microphone.Visibility);
            showLevels.IsChecked = true;
            themedOverview.UpdateLayout();
            Assert.AreEqual(Visibility.Visible, levels.Visibility);
            // Synthetic endpoint snapshots exercise the real UI without changing
            // the user's Windows audio defaults or enumerating test devices.
            themedOverview.ApplyWindowsAudioOutput(new("Desktop headphones", 0, -1));
            themedOverview.UpdateLayout();
            var routeHelp = (TextBlock)themedOverview.FindName("windowsAudioRouteHelp");
            Assert.AreEqual(Visibility.Collapsed, routeHelp.Visibility);
            themedOverview.ApplyWindowsAudioOutput(new("DS4Windows Controller 1 Audio", 4, 0));
            themedOverview.Measure(new Size(1100, 950));
            themedOverview.Arrange(new Rect(0, 0, 1100, 950));
            themedOverview.UpdateLayout();
            Assert.AreEqual(Visibility.Visible, routeHelp.Visibility);
            StringAssert.Contains(((TextBlock)themedOverview.FindName("windowsAudioOutputText")).Text, "DS4Windows Controller 1 Audio");
            var routeImage = new RenderTargetBitmap(1100, 950, 96, 96, PixelFormats.Pbgra32);
            routeImage.Render(themedOverview);
            var routeEncoder = new PngBitmapEncoder();
            routeEncoder.Frames.Add(BitmapFrame.Create(routeImage));
            string routePath = Path.Combine(resultsDirectory, $"overview-windows-audio-{name}.png");
            using (var stream = File.Create(routePath)) routeEncoder.Save(stream);
            renderedFiles.Add(routePath);
            themedOverview.ApplyWindowsAudioOutput(new("Unrelated USB/IP audio", 6, -1));
            Assert.AreEqual(Visibility.Collapsed, routeHelp.Visibility, "Never claim ownership of another app's virtual audio device.");
            themedOverview.ApplyWindowsAudioOutput(null);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)themedOverview.FindName("windowsAudioRoute")).Visibility);
        }
        resources[0] = originalTheme;
        var managed = (FrameworkElement)overview.FindName("managedAudioLevelsPanel");
        var speaker = (FrameworkElement)overview.FindName("managedSpeakerRouteCard");
        var native = (FrameworkElement)overview.FindName("nativeUsbHeadsetCard");
        foreach (int width in new[] { 760, 1100 })
        {
            overview.DataContext = Model(new PhysicalDevice(InputDeviceType.Switch2Pro, ConnectionType.USB));
            overview.Measure(new Size(width, 950));
            overview.Arrange(new Rect(0, 0, width, 950));
            overview.UpdateLayout();
            Assert.AreEqual(Visibility.Collapsed, managed.Visibility);
            Assert.AreEqual(Visibility.Collapsed, speaker.Visibility);
            Assert.AreEqual(Visibility.Visible, native.Visibility);
            Assert.IsTrue(native.ActualWidth > 150 && native.ActualHeight > 100);
            var image = new RenderTargetBitmap(width, 950, 96, 96, PixelFormats.Pbgra32);
            image.Render(overview);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            Directory.CreateDirectory(resultsDirectory);
            string path = Path.Combine(resultsDirectory, $"overview-usb-headset-{width}.png");
            using (var stream = File.Create(path)) encoder.Save(stream);
            renderedFiles.Add(path);
        }
        overview.DataContext = Model(new PhysicalDevice(InputDeviceType.DualSense, ConnectionType.USB, sony: true));
        overview.UpdateLayout();
        Assert.AreEqual(Visibility.Collapsed, native.Visibility);
        Assert.AreEqual(Visibility.Visible, managed.Visibility);
        Assert.AreEqual(Visibility.Visible, speaker.Visibility);
        overview.DataContext = Model(new PhysicalDevice(InputDeviceType.Switch2JoyConLeft, ConnectionType.BT));
        overview.UpdateLayout();
        Assert.AreEqual(Visibility.Collapsed, native.Visibility);
        Assert.AreEqual(Visibility.Collapsed, managed.Visibility);
        Assert.AreEqual(Visibility.Collapsed, speaker.Visibility);
        overview.DataContext = null;
    }

    private static void ValidateImmediateSliderLabels(ControllerOverviewControl overview)
    {
        using var profile = new AudioProfileScope();
        overview.DataContext = Model(new PhysicalDevice(InputDeviceType.DualSense, ConnectionType.USB, sony: true));
        overview.Measure(new Size(1100, 950)); overview.Arrange(new Rect(0, 0, 1100, 950)); overview.UpdateLayout();
        overview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        foreach (string name in new[] { "overviewHapticStrengthSlider", "overviewSpeakerVolumeSlider", "overviewHeadphoneVolumeSlider", "overviewSpeakerBassSlider", "overviewMicrophoneVolumeSlider" })
        {
            var slider = (Slider)overview.FindName(name);
            TextBlock label = null;
            foreach (var child in LogicalTreeHelper.GetChildren(slider.Parent))
                if (child is TextBlock text && System.Windows.Data.BindingOperations.GetBinding(text, TextBlock.TextProperty)?.ElementName == name) label = text;
            Assert.IsNotNull(label, name);
            foreach (int value in slider.Maximum == 6 ? new[] { 1, 4, 6, 2 } : new[] { 12, 78, 93, 27 })
            {
                slider.SetCurrentValue(Slider.ValueProperty, (double)value);
                Assert.AreEqual((double)value, slider.Value);
                overview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.AreEqual(slider.Maximum == 6 ? $"{value} dB" : $"{value}%", label.Text,
                    "The numeric readout must follow the thumb without waiting for a delayed profile write.");
            }
            slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
        }
        overview.DataContext = null;
    }

    [TestMethod]
    public void NoProfileCannotWriteAudioOrOutputSettings()
    {
        using var profile = new AudioProfileScope();
        var model = Model(new PhysicalDevice(InputDeviceType.DualSense, ConnectionType.USB, sony: true), "");
        Assert.IsFalse(model.HasSelectedProfile);
        Assert.IsTrue(model.NeedsProfileSelection);
        Assert.AreEqual("Not configured", model.SelectedOutputControllerName);
        var before = profile.Snapshot();
        var output = Global.OutContType[0];
        var haptics = Global.DualSenseHapticPowerLevel[0];
        int changes = 0;
        model.QuickProfileSettingChanged += (_, _) => changes++;
        _ = model.ControllerAudioSourceId;
        model.SelectedOutputController = output == OutContType.X360 ? OutContType.ViiperDualSense : OutContType.X360;
        model.HapticStrengthPercent = 0;
        model.SpeakerVolumePercent = 99;
        model.SpeakerOutputEnabled = false;
        model.ControllerAudioSourceId = "must-not-write";
        CollectionAssert.AreEqual(before, profile.Snapshot());
        Assert.AreEqual(output, Global.OutContType[0]);
        Assert.AreEqual(haptics, Global.DualSenseHapticPowerLevel[0]);
        Assert.AreEqual(0, changes);
    }

    private static XDocument LoadOverview()
    {
        for (var root = new DirectoryInfo(AppContext.BaseDirectory); root != null; root = root.Parent)
        {
            string file = Path.Combine(root.FullName, "DS4Windows", "DS4Forms", "ControllerOverviewControl.xaml");
            if (File.Exists(file)) return LocalizedSource.Load(file);
        }
        throw new FileNotFoundException("Production Overview markup not found.");
    }

    private sealed class PhysicalDevice : DS4Device
    {
        internal PhysicalDevice(InputDeviceType type, ConnectionType connection, bool sony = false)
            : base("Overview audio test", type, connection)
        {
            if (sony)
            {
                hDevice = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
                typeof(HidDevice).GetField("_deviceAttributes", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(hDevice, new HidDeviceAttributes(new NativeMethods.HIDD_ATTRIBUTES
                    { VendorID = 0x054C, ProductID = 0x0CE6 }));
            }
        }
        internal void SetBattery(int percent) => battery = percent;
        public override bool IsAlive() => true;
    }

    private sealed class AudioProfileScope : IDisposable
    {
        private readonly object[] previous;
        internal AudioProfileScope()
        {
            previous = Snapshot();
            Global.DualSenseEnableSpeakerOutput[0] = true;
            Global.DualSenseHeadsetOnlyAudio[0] = true;
            Global.DualSenseEnableMicrophonePassthrough[0] = true;
            Global.DualSenseSpeakerVolume[0] = 50;
            Global.DualSenseHeadphoneVolume[0] = 50;
            Global.DualSenseMicrophoneVolume[0] = 50;
            Global.DualSenseSpeakerCompression[0] = 0;
            Global.DualSenseSpeakerBassBoost[0] = 0;
            Global.DualSenseMicrophoneNoiseSuppression[0] = 0;
            Global.DualSenseAudioCaptureEndpointId[0] = "saved-source";
        }
        internal object[] Snapshot() => new object[]
        {
            Global.DualSenseEnableSpeakerOutput[0], Global.DualSenseHeadsetOnlyAudio[0],
            Global.DualSenseEnableMicrophonePassthrough[0], Global.DualSenseSpeakerVolume[0],
            Global.DualSenseHeadphoneVolume[0], Global.DualSenseMicrophoneVolume[0],
            Global.DualSenseSpeakerCompression[0], Global.DualSenseSpeakerBassBoost[0],
            Global.DualSenseMicrophoneNoiseSuppression[0], Global.DualSenseAudioCaptureEndpointId[0],
        };
        public void Dispose()
        {
            Global.DualSenseEnableSpeakerOutput[0] = (bool)previous[0];
            Global.DualSenseHeadsetOnlyAudio[0] = (bool)previous[1];
            Global.DualSenseEnableMicrophonePassthrough[0] = (bool)previous[2];
            Global.DualSenseSpeakerVolume[0] = (byte)previous[3];
            Global.DualSenseHeadphoneVolume[0] = (byte)previous[4];
            Global.DualSenseMicrophoneVolume[0] = (byte)previous[5];
            Global.DualSenseSpeakerCompression[0] = (byte)previous[6];
            Global.DualSenseSpeakerBassBoost[0] = (byte)previous[7];
            Global.DualSenseMicrophoneNoiseSuppression[0] = (byte)previous[8];
            Global.DualSenseAudioCaptureEndpointId[0] = (string)previous[9];
        }
    }
}
