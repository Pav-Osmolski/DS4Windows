using Microsoft.VisualStudio.TestTools.UnitTesting;
using DS4WinWPF;
using DS4WinWPF.DS4Forms;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace DS4WindowsTests
{
    [TestClass]
    [DoNotParallelize]
    public class ThemeResourceTests
    {
        public TestContext TestContext { get; set; }

        private static void ValidateProfileEditorLightbarLifecycle(Application application)
        {
            // App.xaml supplies this diagram-button style in the running app.
            application.Resources.Add("NoBGHoverBtn", new Style(typeof(Button)));
            try
            {
                foreach (bool visitLightbar in new[] { false, true })
                {
                    var editor = new ProfileEditor(DS4Windows.Global.TEST_PROFILE_INDEX);
                    try
                    {
                        var lightbar = (DeferredProfilePage)editor.FindName("lightbarPage");
                        Assert.IsFalse(lightbar.IsMaterialized);
                        if (visitLightbar)
                        {
                            lightbar.Ready = true;
                            lightbar.EnsureContent();
                            Assert.IsNotNull(((FrameworkElement)lightbar.Content).FindName("colorGB"));
                            var touchpad = (DeferredProfilePage)editor.FindName("touchpadPage");
                            touchpad.Ready = true;
                            touchpad.EnsureContent();
                            var content = (FrameworkElement)touchpad.Content;
                            var mode = (ComboBox)content.FindName("touchpadOutModeCombo");
                            var distance = (Xceed.Wpf.Toolkit.IntegerUpDown)content.FindName("touchpadSwipeDistanceInput");
                            ThemeConsistencyValidation.Layout(content, 640, 600);
                            Assert.IsGreaterThan(0d, distance.ActualHeight);
                            Assert.IsFalse(distance.IsEnabled, "Mouse mode must show the field as unavailable.");
                            mode.SelectedIndex = 1;
                            ThemeConsistencyValidation.Layout(content, 640, 600);
                            Assert.IsTrue(distance.IsEnabled, "Controls mode must enable the distance field.");
                        }
                    }
                    finally { editor.RetireEditorSession(); }
                }
            }
            finally { application.Resources.Remove("NoBGHoverBtn"); }
        }

        private static void ValidateControllerSettingsDialogs(Application application)
        {
            var service = (DS4Windows.ControlService)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(DS4Windows.ControlService));
            service.DS4Controllers = System.Array.Empty<DS4Windows.DS4Device>();
            var dark = new ResourceDictionary
            {
                Source = new Uri("/DS4Windows;component/DS4Forms/Themes/DarkTheme.xaml", UriKind.Relative),
            };
            try
            {
                foreach (bool useDarkTheme in new[] { false, true })
                {
                    if (useDarkTheme) application.Resources.MergedDictionaries.Add(dark);
                    var devices = new ControllerRegisterOptionsWindow(new DS4Windows.ControlServiceDeviceOptions(), service);
                    try
                    {
                        var controllerList = (ListBox)devices.FindName("controllerListBox");
                        controllerList.ItemsSource = new[] { new { IdText = "DualSense" } };
                        ThemeConsistencyValidation.Layout((FrameworkElement)devices.Content, 760, 500);
                        var row = (ListBoxItem)controllerList.ItemContainerGenerator.ContainerFromIndex(0);
                        var inset = row.TranslatePoint(new Point(), controllerList);
                        Assert.AreEqual(inset.Y, controllerList.ActualHeight - inset.Y - row.ActualHeight, 1d,
                            "A single detected controller must have equal space above and below its row.");
                        Assert.AreEqual(inset.X, controllerList.ActualWidth - inset.X - row.ActualWidth, 1d,
                            "A single detected controller must have equal space to the left and right.");

                        controllerList.ItemsSource = Enumerable.Repeat(new { IdText = "DualSense" }, 8).ToArray();
                        ThemeConsistencyValidation.Layout((FrameworkElement)devices.Content, 760, 500);
                        var controllerScroll = ThemeConsistencyValidation.Visuals(controllerList).OfType<ScrollViewer>().First();
                        Assert.IsGreaterThan(0d, controllerScroll.ScrollableHeight, "Long controller lists must scroll.");
                        var details = (TabControl)devices.FindName("deviceSettingsTabControl");
                        Assert.IsNotNull(details.Template.FindName("PART_SelectedContentHost", details));
                    }
                    finally { devices.Close(); }
                }
            }
            finally { application.Resources.MergedDictionaries.Remove(dark); }

            var profiles = new ProfileList();
            profiles.ProfileListCol.Add(new ProfileEntity { Name = "Default" });
            profiles.ProfileListCol.Add(new ProfileEntity { Name = "Testing" });
            var swipe = new SwipeProfilesEditor(profiles, new[] { "Testing" });
            try
            {
                ThemeConsistencyValidation.Layout((FrameworkElement)swipe.Content, 500, 460);
                var rows = (ItemsControl)swipe.FindName("profilesListBox");
                Assert.AreEqual(2, rows.Items.Count);
                Assert.IsTrue(((SwipeProfileItem)rows.Items[1]).IsAllowed);
                ((Button)swipe.FindName("clearAllBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsFalse(((SwipeProfileItem)rows.Items[1]).IsAllowed);
                ((Button)swipe.FindName("selectAllBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsTrue(((SwipeProfileItem)rows.Items[0]).IsAllowed);
            }
            finally { swipe.Close(); }
        }

        [TestMethod]
        public void DefaultThemeLoadsBridgeShellStylesOnFreshConfiguration()
        {
            Exception failure = null;
            var renderedFiles = new System.Collections.Generic.List<string>();
            string resultsDirectory = TestContext.TestResultsDirectory;
            Action thread = () =>
            {
                try
                {
                    var application = new Application();
                    var defaultTheme = new ResourceDictionary();
                    application.Resources.MergedDictionaries.Add(defaultTheme);
                    defaultTheme.Source = new Uri(
                        "/DS4Windows;component/DS4Forms/Themes/DefaultTheme.xaml",
                        UriKind.Relative);

                    var bridgeStyles = new ResourceDictionary();
                    application.Resources.MergedDictionaries.Add(bridgeStyles);
                    bridgeStyles.Source = new Uri(
                        "/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml",
                        UriKind.Relative);

                    Assert.IsNotNull(application.TryFindResource(
                        "BridgePrimaryButtonStyle"));
                    Assert.IsNotNull(application.TryFindResource(
                        "BridgeSecondaryButtonStyle"));
                    Assert.IsNotNull(application.TryFindResource(
                        "BridgeProfileComboBoxStyle"));
                    Assert.IsNotNull(application.TryFindResource(
                        "BridgeDescribedCheckBoxStyle"));

                    // This is the same construction path used by MainWindow
                    // on a clean install. It must not depend on a converter
                    // that exists only in DarkTheme or in a parent window.
                    var overview = new ControllerOverviewControl();
                    Assert.IsNotNull(overview.Resources[
                        "InverseBoolConverter"]);
                    ControllerOverviewAudioTests.ValidateRenderedOverview(overview, resultsDirectory, renderedFiles);

                    var repairPrompt = new ViiperSetupPrompt(
                        "usbip-win2 0.9.7.8 must be replaced with supported 0.9.8.1",
                        null, verifiedUpdateRequired: true,
                        usbipReplacementRequired: true,
                        mandatoryRepairRequired: true);
                    Assert.AreEqual("USB-IP version must be replaced",
                        ((TextBlock)repairPrompt.FindName(
                            "headingText")).Text);
                    Assert.AreEqual("Repair VIIPER + USB-IP",
                        ((Button)repairPrompt.FindName(
                            "installButton")).Content);
                    Assert.AreEqual(Visibility.Collapsed,
                        ((CheckBox)repairPrompt.FindName(
                            "suppressPromptCheck")).Visibility);
                    Assert.IsFalse(repairPrompt.ExitApplicationRequested,
                        "The settings UI must remain available in degraded mode while virtual output fails closed.");

                    var missingPrompt = new ViiperSetupPrompt(
                        "VIIPER and usbip-win2 need setup", null,
                        mandatoryRepairRequired: true);
                    Assert.AreEqual("VIIPER setup required",
                        ((TextBlock)missingPrompt.FindName(
                            "headingText")).Text);
                    Assert.AreEqual("Continue without virtual output",
                        ((Button)missingPrompt.FindName(
                            "notNowButton")).Content);
                    Assert.IsFalse(missingPrompt.ExitApplicationRequested,
                        "Missing prerequisites must leave repair and diagnostics accessible.");

                    // The repair progress surface is created before the
                    // elevated setup host runs. Its clean-install XAML must
                    // therefore resolve using only application theme assets.
                    var setupProgress = new ViiperSetupProgress(
                        System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            "ds4windows-missing-setup-log.txt"));
                    Assert.AreEqual("Setting up VIIPER",
                        ((TextBlock)setupProgress.FindName(
                            "headingText")).Text);
                    Assert.AreEqual("Preparing verified package...",
                        ((TextBlock)setupProgress.FindName(
                            "phaseText")).Text);
                    ValidateProfileEditorLightbarLifecycle(application);
                    ValidateControllerSettingsDialogs(application);
                    Switch2StickCalibrationEditorTests.ValidateStickCalibrationWindow(application);
                    BindingWindowLiveInputLayoutTests.ValidateRenderedBindingWindows(
                        application, resultsDirectory, renderedFiles);
                    FinalUiConsistencyValidation.Validate(application, resultsDirectory, renderedFiles);
                    SharedMenuLayoutTests.ValidateDetachedThemeSwitch(application);
                    ThemeConsistencyValidation.Validate(application, resultsDirectory, renderedFiles);
                    AutoProfileLayoutValidation.Validate(application, resultsDirectory, renderedFiles);
                    BatteryAlertsLayoutValidation.Validate(application, resultsDirectory, renderedFiles);
                    ProfileAudioSelectionValidation.Validate();
                    AppThemeValidation.Validate(application, resultsDirectory, renderedFiles);
                    ControllerInfoWindowValidation.Validate(application, resultsDirectory, renderedFiles);
                    SettingsStorageLayoutValidation.Validate(application, resultsDirectory, renderedFiles);
                    UpdateStorageLayoutValidation.Validate(application, renderedFiles);
                    new ContextualHeaderTests { TestContext = TestContext }.ValidateProfileActions();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            };
            HeadlessWpfDispatcher.Invoke(thread);
            if (failure != null)
            {
                Assert.Fail(failure.ToString());
            }
            Assert.AreEqual(155, renderedFiles.Count);
            foreach (string path in renderedFiles)
            {
                Assert.IsTrue(System.IO.File.Exists(path), path);
                TestContext.AddResultFile(path);
            }
        }
    }
}
