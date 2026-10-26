using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using DS4Windows;
using DS4WinWPF;
using DS4WinWPF.DS4Forms.Themes;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

internal static class SettingsStorageLayoutValidation
{
    internal static void Validate(Application application, string directory, List<string> files)
    {
        var originalResources = application.Resources.MergedDictionaries.ToArray();
        string originalRoot = Global.appdatapath, originalProgram = Global.exedirpath, originalAppData = Global.appDataPpath;
        string temporary = Directory.CreateTempSubdirectory("ds4w-storage-render-").FullName;
        directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath()), "..", "artifacts", "profile-storage-renders"));
        Directory.CreateDirectory(directory);
        Global.exedirpath = Path.Combine(temporary, "Program");
        Global.appdatapath = Global.appDataPpath = Path.Combine(temporary, "AppData");
        Directory.CreateDirectory(Path.Combine(Global.appdatapath, "Profiles"));
        foreach (string name in new[] { "Default", "MAME", "Windows Control" })
            File.WriteAllText(Path.Combine(Global.appdatapath, "Profiles", name + ".xml"), "<DS4Windows><OutputContDevice>DualSense</OutputContDevice></DS4Windows>");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = LocalizedSource.Load(Path.Combine(Path.GetDirectoryName(SourcePath()), "..", "DS4Windows", "DS4Forms", "MainWindow.xaml"));
        var markup = new XElement(source.Descendants().Single(element => (string)element.Attribute(x + "Name") == "profilesBrowserScrollViewer"));
        markup.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:DS4WinWPF.DS4Forms");
        markup.SetAttributeValue(XNamespace.Xmlns + "localConverters", "clr-namespace:DS4WinWPF.DS4Forms.Converters");
        markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        markup.SetAttributeValue("xmlns", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        foreach (var attribute in markup.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attribute.Name.LocalName is "Click" or "SelectionChanged" or "MouseDoubleClick" or "TextChanged" or "Loaded") attribute.Remove();
            else if (attribute.Value.StartsWith("{lex:Loc ")) attribute.Value = LocalizedSource.Resolve(attribute.Value);
        }
        try
        {
            foreach (var choice in new[] { AppThemeChoice.Light, AppThemeChoice.Dark, AppThemeChoice.Graphite, AppThemeChoice.Paper, AppThemeChoice.Midnight, AppThemeChoice.HighContrast })
            {
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(choice, highContrast: false));
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
                string isolated = LocalizedSource.Markup(markup)
                    .Replace("clr-namespace:DS4WinWPF.DS4Forms\"", "clr-namespace:DS4WinWPF.DS4Forms;assembly=DS4Windows\"")
                    .Replace("clr-namespace:DS4WinWPF.DS4Forms.Converters\"", "clr-namespace:DS4WinWPF.DS4Forms.Converters;assembly=DS4Windows\"");
                var content = (ScrollViewer)XamlReader.Parse(isolated);
                ((SettingsStorageControl)content.FindName("settingsStorageControl")).Refresh();
                var list = (ListBox)content.FindName("profilesListBox");
                list.ItemsSource = new[] { "Default", "MAME", "Windows Control" }.Select(name => new ProfileEntity { Name = name }).ToArray();
                list.SelectedIndex = 0;
                foreach (string name in new[] { "editProfBtn", "dupProfBtn", "renameProfBtn", "deleteProfBtn", "exportProfBtn" })
                    ((Button)content.FindName(name)).IsEnabled = true;
                var sort = (ComboBox)content.FindName("profileSortCombo");
                sort.Items.Add("Name: A–Z"); sort.SelectedIndex = 0;
                var host = new Border { Padding = new Thickness(18), Child = content };
                host.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundColor");
                host.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "ForegroundColor");
                ((TextBlock)content.FindName("profileNameHeader")).Text = "Name";
                ((TextBlock)content.FindName("profileOutputHeader")).Text = "Emulated controller";
                ((TextBlock)content.FindName("profileModifiedHeader")).Text = "Last modified";
                ((Button)content.FindName("profileIconBtn")).Content = "Profile icon…";
                var header = (Grid)content.FindName("profileDetailsHeader");
                var browser = new ProfileBrowserViews(list, header);
                ((Button)content.FindName("profileIconBtn")).IsEnabled = true;
                ProfileBrowserViews.CreatePicker((WrapPanel)content.FindName("profileViewChoices"), ProfileViewChoice.Tiles, browser.Apply);
                ThemeConsistencyValidation.Layout(host, 1200, 820);
                foreach (var button in ThemeConsistencyValidation.Visuals(content).OfType<Button>().Where(button => button.IsEnabled && button.Visibility == Visibility.Visible && button.ActualWidth > 0))
                    Assert.IsTrue(button.ActualHeight >= 20, button.Name + " must have a usable height.");
                string path = Path.Combine(directory, "profiles-" + choice + ".png");
                ThemeConsistencyValidation.Save(host, path, 1200, 820);
                files.Add(path);
                var selected = list.SelectedItem;
                var tileItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Point tileInset = tileItem.TranslatePoint(new Point(), list);
                foreach (var view in new[] { ProfileViewChoice.List, ProfileViewChoice.Details })
                {
                    browser.Apply(view);
                    ThemeConsistencyValidation.Layout(host, 1200, 820);
                    browser.AlignHeader();
                    ThemeConsistencyValidation.Layout(host, 1200, 820);
                    Assert.AreSame(selected, list.SelectedItem, "Changing view must preserve selection and actions.");
                    Assert.AreEqual(view == ProfileViewChoice.Details ? Visibility.Visible : Visibility.Collapsed, header.Visibility);
                    foreach (var item in list.Items.Cast<ProfileEntity>()) Assert.AreNotEqual("P", item.IconLetter);
                    var firstRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    Point rowInset = firstRow.TranslatePoint(new Point(), list);
                    Assert.AreEqual(tileInset.X, rowInset.X, 0.5, "Rows and tiles must have the same left inset.");
                    Assert.AreEqual(tileInset.Y, rowInset.Y, 0.5, "Rows and tiles must have the same top inset below their header.");
                    foreach (var badge in ThemeConsistencyValidation.Visuals(list).OfType<ProfileIconBadge>())
                    {
                        var fill = ThemeConsistencyValidation.Visuals(badge).OfType<Border>().First();
                        Rect drawing = fill.TransformToAncestor(badge).TransformBounds(new Rect(fill.RenderSize));
                        Assert.IsTrue(drawing.Left >= -0.5 && drawing.Top >= -0.5 &&
                            drawing.Right <= badge.ActualWidth + 0.5 && drawing.Bottom <= badge.ActualHeight + 0.5,
                            "The complete rounded badge must fit inside the reduced row icon, without clipping.");
                    }
                    if (view == ProfileViewChoice.Details)
                    {
                        var parent = (FrameworkElement)list.Parent;
                        Rect labels = header.TransformToAncestor(parent).TransformBounds(new Rect(header.RenderSize));
                        double rowTop = firstRow.TranslatePoint(new Point(), parent).Y;
                        Assert.AreEqual(labels.Top, rowTop - labels.Bottom, 0.5,
                            "Details headings need equal space above and below their text.");
                    }

                    path = Path.Combine(directory, "profiles-" + view + "-" + choice + ".png");
                    ThemeConsistencyValidation.Save(host, path, 1200, 820); files.Add(path);
                    if (choice == AppThemeChoice.Dark)
                    {
                        var scaled = new System.Windows.Media.Imaging.RenderTargetBitmap(2400, 1640, 192, 192, PixelFormats.Pbgra32);
                        scaled.Render(host);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scaled));
                        path = Path.Combine(directory, "profiles-" + view + "-Dark-200pct.png");
                        using (var stream = File.Create(path)) encoder.Save(stream);
                        files.Add(path);
                    }
                }
                var iconWindow = new ProfileIconWindow((ProfileEntity)selected);
                var dialogHost = (FrameworkElement)iconWindow.Content;
                iconWindow.Content = null;
                var dialogSurface = new Border { Child = dialogHost };
                dialogSurface.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundColor");
                ThemeConsistencyValidation.Layout(dialogSurface, 480, 480);
                path = Path.Combine(directory, "profile-icon-" + choice + ".png");
                ThemeConsistencyValidation.Save(dialogSurface, path, 480, 480); files.Add(path);
                browser.Apply(ProfileViewChoice.Tiles);
                if (choice is AppThemeChoice.Light or AppThemeChoice.Dark)
                {
                    ThemeConsistencyValidation.Layout(host, 620, 500);
                    Assert.IsTrue(content.ScrollableHeight > 0, "The complete page must scroll when its cards exceed the viewport.");
                    path = Path.Combine(directory, "profiles-" + choice + "-narrow.png");
                    ThemeConsistencyValidation.Save(host, path, 620, 500);
                    files.Add(path);
                }
            }
        }
        finally
        {
            Global.appdatapath = originalRoot; Global.exedirpath = originalProgram; Global.appDataPpath = originalAppData;
            application.Resources.MergedDictionaries.Clear();
            foreach (var resource in originalResources) application.Resources.MergedDictionaries.Add(resource);
            Assert.IsTrue(Path.GetFileName(Path.GetFullPath(temporary)).StartsWith("ds4w-storage-render-"));
            Directory.Delete(temporary, true);
        }
    }
    private static string SourcePath([CallerFilePath] string source = "") => source;
}
