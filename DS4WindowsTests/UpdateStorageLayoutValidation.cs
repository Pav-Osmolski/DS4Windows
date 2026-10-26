using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DS4Windows;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

internal static class UpdateStorageLayoutValidation
{
    internal static void Validate(Application application, List<string> rendered)
    {
        var dictionaries = application.Resources.MergedDictionaries.ToArray();
        string originalProgram = Global.exedirpath, originalRoot = Global.appdatapath, originalAppData = Global.appDataPpath;
        string temporary = Directory.CreateTempSubdirectory("ds4w-update-layout-").FullName;
        string output = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath()), "..", "artifacts", "update-storage-renders"));
        Directory.CreateDirectory(output);
        Global.exedirpath = Directory.CreateDirectory(Path.Combine(temporary, "Program")).FullName;
        Global.appdatapath = Global.appDataPpath = Directory.CreateDirectory(Path.Combine(temporary, "Roaming")).FullName;
        var settings = new SettingsStorage(Global.exedirpath, Global.appDataPpath, Path.Combine(temporary, "Preferences"));
        try
        {
            foreach (var choice in new[] { AppThemeChoice.Light, AppThemeChoice.Dark, AppThemeChoice.Graphite, AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast })
            {
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(choice, highContrast: false));
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                settings.SetStorageExpanded(false);
                var profiles = new SettingsStorageControl { Storage = () => settings };
                profiles.Refresh();
                var header = ThemeConsistencyValidation.Visuals(profiles).OfType<Button>().FirstOrDefault();
                var host = new Border { Padding = new Thickness(16), Child = profiles };
                host.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundColor");
                host.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "ForegroundColor");
                ThemeConsistencyValidation.Layout(host, 1000, 130);
                Assert.IsTrue(profiles.ActualHeight < 110, "Collapsed storage should be a compact disclosure header.");
                Save("profiles-collapsed", host, 1000, 130);
                header ??= ThemeConsistencyValidation.Visuals(profiles).OfType<Button>().First();
                var indicator = ThemeConsistencyValidation.Visuals(header).OfType<Expander>().Single();
                var collapsedArrowPosition = indicator.TransformToAncestor(profiles).Transform(new Point(indicator.ActualWidth / 2, indicator.ActualHeight / 2));
                var headerCenter = header.TransformToAncestor(profiles).Transform(new Point(header.ActualWidth / 2, header.ActualHeight / 2));
                Assert.AreEqual(headerCenter.Y, collapsedArrowPosition.Y, 0.5, "The circle indicator must be vertically centred in the collapsed header.");
                var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                header.SetValue(hoverKey, true);
                ThemeConsistencyValidation.Layout(host, 1000, 130);
                var chrome = (Border)header.Template.FindName("Chrome", header);
                Assert.AreEqual(((System.Windows.Media.SolidColorBrush)application.FindResource("RaisedBackgroundColor")).Color,
                    ((System.Windows.Media.SolidColorBrush)chrome.Background).Color, "Disclosure hover must use the subtle themed surface, not the selected-item fill.");
                Assert.IsNotNull(header.FocusVisualStyle, "Keyboard navigation needs its own visible focus outline.");
                Save("profiles-collapsed-hover", host, 1000, 130);
                header.SetValue(hoverKey, false);
                header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsTrue(settings.StorageExpanded, "User expansion must be persisted.");
                ThemeConsistencyValidation.Layout(host, 1000, 350);
                var expandedArrowPosition = indicator.TransformToAncestor(profiles).Transform(new Point(indicator.ActualWidth / 2, indicator.ActualHeight / 2));
                Assert.AreEqual(collapsedArrowPosition.X, expandedArrowPosition.X, 0.1, "Expanding must not move the indicator horizontally.");
                Assert.AreEqual(collapsedArrowPosition.Y, expandedArrowPosition.Y, 0.1, "Expanding must not move the indicator vertically.");
                Assert.IsTrue(indicator.IsExpanded);
                Save("profiles-expanded", host, 1000, 350);
                header.SetValue(hoverKey, true);
                ThemeConsistencyValidation.Layout(host, 1000, 350);
                Save("profiles-expanded-hover", host, 1000, 350);
                header.SetValue(hoverKey, false);
                var recreated = new SettingsStorageControl { Storage = () => settings };
                recreated.Refresh();
                Assert.IsTrue(recreated.IsExpanded, "A recreated page must read the saved choice.");
                if (choice == AppThemeChoice.Dark)
                {
                    settings.SetStorageExpanded(false);
                    var folder = new DirectoryInfo(Global.appdatapath);
                    var originalAcl = System.IO.FileSystemAclExtensions.GetAccessControl(folder);
                    var restricted = System.IO.FileSystemAclExtensions.GetAccessControl(folder);
                    restricted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        System.Security.Principal.WindowsIdentity.GetCurrent().User,
                        System.Security.AccessControl.FileSystemRights.WriteData, System.Security.AccessControl.AccessControlType.Deny));
                    try
                    {
                        System.IO.FileSystemAclExtensions.SetAccessControl(folder, restricted);
                        var readOnly = new SettingsStorageControl { Storage = () => settings }; readOnly.Refresh();
                        Assert.IsTrue(readOnly.IsExpanded, "Read-only folders must automatically expose the warning and recovery actions.");
                        Assert.IsFalse(settings.StorageExpanded, "Automatic expansion must not overwrite the user's preference.");
                        host.Child = readOnly; ThemeConsistencyValidation.Layout(host, 1000, 350);
                        Save("profiles-read-only", host, 1000, 350);
                    }
                    finally
                    {
                        var restored = new System.Security.AccessControl.DirectorySecurity();
                        restored.SetSecurityDescriptorBinaryForm(originalAcl.GetSecurityDescriptorBinaryForm(), System.Security.AccessControl.AccessControlSections.Access);
                        System.IO.FileSystemAclExtensions.SetAccessControl(folder, restored);
                    }
                    var writable = new SettingsStorageControl { Storage = () => settings }; writable.Refresh();
                    Assert.IsFalse(writable.IsExpanded, "When access is restored, the user's collapsed preference still applies.");
                }
                var updates = new UpdateStorageControl();
                var old = new UpdateStorage.StoredFile("example", "DS4Windows", "5.0.48", new Version(5, 0, 48), "", 512L * 1024 * 1024, 0);
                var current = old with { Release = "5.0.50", Bytes = 300L * 1024 * 1024 };
                updates.ShowSnapshot(new(new[] { old, current }, new[] { old }, Array.Empty<string>()));
                var updateCard = new Border { Child = updates, VerticalAlignment = VerticalAlignment.Top };
                updateCard.SetResourceReference(FrameworkElement.StyleProperty, "BridgeCardStyle");
                var scroll = new ScrollViewer { Content = updateCard, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                host.Child = scroll;
                ThemeConsistencyValidation.Layout(host, 1000, 600);
                var divider = (Border)updates.Children[0];
                Assert.AreEqual(((System.Windows.Media.SolidColorBrush)application.FindResource("BorderColor")).Color,
                    ((System.Windows.Media.SolidColorBrush)divider.Background).Color, "The divider must match the themed outlines.");
                var status = updates.Children.OfType<TextBlock>().Last();
                Assert.AreEqual(Visibility.Collapsed, status.Visibility, "An empty status must not reserve a blank line below the footer.");
                status.Text = "Cleanup result";
                ThemeConsistencyValidation.Layout(host, 1000, 600);
                Assert.AreEqual(Visibility.Visible, status.Visibility);
                status.Text = "";
                ThemeConsistencyValidation.Layout(host, 1000, 600);
                Assert.AreEqual(Visibility.Collapsed, status.Visibility);
                var footer = updates.Children.OfType<TextBlock>().Reverse().Skip(1).First();
                double footerBottom = footer.TransformToAncestor(updateCard).TransformBounds(new Rect(footer.RenderSize)).Bottom;
                Assert.IsTrue(updateCard.ActualHeight - footerBottom <= 30,
                    "The footer-to-outline gap must match the normal card padding, without an empty status row.");
                Save("updates", host, 1000, 600);
                ThemeConsistencyValidation.Layout(host, 500, 420);
                Assert.IsTrue(scroll.ScrollableHeight > 0, "Narrow update storage must scroll rather than clip its controls.");
                Save("updates-narrow", host, 500, 420);

                void Save(string name, FrameworkElement control, int width, int height)
                {
                    string path = Path.Combine(output, name + "-" + choice + ".png");
                    ThemeConsistencyValidation.Save(control, path, width, height); rendered.Add(path);
                }
            }
        }
        finally
        {
            Global.exedirpath = originalProgram; Global.appdatapath = originalRoot; Global.appDataPpath = originalAppData;
            application.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in dictionaries) application.Resources.MergedDictionaries.Add(dictionary);
            Assert.IsTrue(Path.GetFileName(Path.GetFullPath(temporary)).StartsWith("ds4w-update-layout-"));
            Directory.Delete(temporary, true);
        }
    }
    private static string SourcePath([CallerFilePath] string source = "") => source;
}
