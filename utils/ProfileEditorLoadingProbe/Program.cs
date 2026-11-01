using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

// Offline probe: never runs App.Startup or a service constructor, opens a controller,
// installs drivers, or reads the user's settings. Use the same probe executable
// with both package directories for a comparable baseline.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: ProfileEditorLoadingProbe <package-directory> <report.json>"); return 2; }
        string directory = Path.GetFullPath(args[0]);
        string report = Path.GetFullPath(args[1]);
        string scratch = Path.Combine(Path.GetDirectoryName(report)!, "probe-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(scratch, "Profiles"));
        var resolver = new AssemblyDependencyResolver(Path.Combine(directory, "DS4Windows.dll"));
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, "DS4Windows.dll"));
            // A plain Application is intentional: App's Startup handler must
            // never run, even when we pump WPF's dispatcher for layout.
            var app = new Application();
            app.Resources["NoBGHoverBtn"] = System.Windows.Markup.XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Background' Value='Transparent'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate></Setter.Value></Setter></Style>");
            app.Resources["ShellViewportWidth"] = Activator.CreateInstance(assembly.GetType("DS4WinWPF.DS4Forms.Converters.ShellViewportWidthConverter")!);
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var theme = (ResourceDictionary)assembly.GetType("DS4WinWPF.DS4Forms.Themes.AppThemeCatalog")!.GetMethod("Create")!.Invoke(null,
                new object[] { Enum.Parse(assembly.GetType("DS4Windows.AppThemeChoice")!, args.FirstOrDefault(a => a.StartsWith("--theme="))?.Substring(8) ?? "Dark"), "Theme", false, false })!;
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(theme);
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml", UriKind.Relative) });
            var serviceType = assembly.GetType("DS4Windows.ControlService")!;
            var offlineService = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(serviceType);
            foreach (var field in serviceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (field.FieldType.IsArray && field.FieldType.GetArrayRank() == 1)
                    field.SetValue(offlineService, Array.CreateInstance(field.FieldType.GetElementType()!, 16));
            assembly.GetType("DS4WinWPF.App")!.GetField("rootHub")!.SetValue(null, offlineService);
            assembly.GetType("DS4Windows.Program")!.GetField("rootHub")!.SetValue(null, offlineService);
            var global = assembly.GetType("DS4Windows.Global")!;
            global.GetField("appdatapath")!.SetValue(null, scratch);
            int slot = args.Contains("--profile-slot=0") ? 0 : (int)global.GetField("TEST_PROFILE_INDEX")!.GetRawConstantValue()!;
            var paths = (string[])global.GetProperty("ProfilePath")!.GetValue(null)!;
            paths[slot] = "Offline probe";
            SetSlot(global, "BTPollRate", slot, (byte)8);
            var store = global.GetProperty("store")!.GetValue(null)!;
            ((Array)store.GetType().GetField("rumble")!.GetValue(store)!).SetValue((byte)73, slot);
            var actions = (System.Collections.IList)global.GetMethod("GetActions")!.Invoke(null, null)!;
            actions.Add(Activator.CreateInstance(assembly.GetType("DS4Windows.SpecialAction")!, "Probe action", "PS", "DisconnectBT", "0", 0d, ""));
            SetSlot(global, "ProfileActions", slot, new List<string> { "Probe action" });
            var entity = Activator.CreateInstance(assembly.GetType("DS4WinWPF.ProfileEntity")!);
            entity.GetType().GetProperty("Name")!.SetValue(entity, "Offline probe");
            bool visitPages = args.Contains("--visit-pages");
            var rows = new List<object>();
            var type = assembly.GetType("DS4WinWPF.DS4Forms.ProfileEditor")!;
            for (int attempt = 0; attempt < (visitPages ? 2 : 6); attempt++)
            {
                var saveProfile = global.GetMethod("SaveProfile")!;
                if (!(bool)saveProfile.Invoke(null, new object[] { slot, "Before", null, true })!)
                    throw new InvalidOperationException("Could not snapshot the isolated profile.");
                var before = XDocument.Load(Path.Combine(scratch, "Profiles", "Before.xml"));
                long allocations = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                var editor = (FrameworkElement)Activator.CreateInstance(type, slot, -1)!;
                long constructed = timer.ElapsedMilliseconds;
                // Suppress prerequisite UI in the baseline as well; this is an
                // offline layout probe, not a backend installation test.
                if (type.GetField("outConTypeCombo", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) is ComboBox selector)
                    selector.SelectionChanged -= (SelectionChangedEventHandler)type.GetMethod("OutConTypeCombo_SelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(SelectionChangedEventHandler), editor);
                type.GetMethod("Reload")!.Invoke(editor, new[] { (object)slot, entity, true });
                long bound = timer.ElapsedMilliseconds;

                editor.Measure(new Size(1100, 720));
                editor.Arrange(new Rect(0, 0, 1100, 720));
                editor.UpdateLayout();
                DrainDispatcher();
                var bitmap = new RenderTargetBitmap(1100, 720, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(editor);
                long rendered = timer.ElapsedMilliseconds;
                string shotDirectory = Path.Combine(Path.GetDirectoryName(report)!, Path.GetFileNameWithoutExtension(report) + "-screenshots");
                if (visitPages && attempt == 0) SaveImage(bitmap, shotDirectory, "mapping");
                var pages = type.GetField("profileSettingsTabCon", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(editor) as TabControl;
                int initiallyConstructed = pages.Items.Cast<TabItem>().Count(t => t.Content.GetType().Name != "DeferredProfilePage" || ((ContentControl)t.Content).Content != null);
                long initialAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocations;
                var pageTimes = new List<long>();
                for (int page = 3; page <= (visitPages ? 10 : 2); page++)
                {
                    Console.WriteLine($"Opening page {page}");
                    long start = timer.ElapsedMilliseconds;
                    type.GetMethod("SelectWorkspaceSection")!.Invoke(editor, new object[] { page });
                    editor.UpdateLayout(); DrainDispatcher();
                    pages.BeginAnimation(UIElement.OpacityProperty, null); pages.Opacity = 1;
                    pages.RenderTransform = Transform.Identity;
                    bitmap.Clear(); bitmap.Render(editor);
                    pageTimes.Add(timer.ElapsedMilliseconds - start);
                    if (attempt == 0) SaveImage(bitmap, shotDirectory, $"page-{page}");
                    var selected = ((TabItem)pages.SelectedItem).Content as ContentControl;
                    object retained = selected?.Content;
                    var model = type.GetField("profileSettingsVM", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor);
                    if (selected?.GetType().Name == "DeferredProfilePage" &&
                        (!ReferenceEquals(model, selected.DataContext) || !ReferenceEquals(model, ((FrameworkElement)selected.Content).DataContext)))
                        throw new InvalidOperationException($"Page {page} did not inherit the current profile model: host={selected.DataContext?.GetType().Name ?? "null"}, root={((FrameworkElement)selected.Content).DataContext?.GetType().Name ?? "null"}");
                    type.GetMethod("SelectWorkspaceSection")!.Invoke(editor, new object[] { 0 });
                    type.GetMethod("SelectWorkspaceSection")!.Invoke(editor, new object[] { page });
                    editor.UpdateLayout(); DrainDispatcher();
                    if (selected?.GetType().Name == "DeferredProfilePage" && !ReferenceEquals(retained, selected.Content))
                        throw new InvalidOperationException("A revisited page lost its editing controls.");
                }
                if (visitPages)
                {
                    type.GetMethod("StopEditorBindings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
                    type.GetMethod("RefreshEditorBindings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
                    DrainDispatcher();
                    var touch = (FrameworkElement)type.GetField("touchButtonUC", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                    var axis = type.GetField("axialLSStickControl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                    var axisGrid = (FrameworkElement)axis.GetType().GetField("mainGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(axis)!;
                    if (touch.DataContext == null || axisGrid.DataContext == null)
                        throw new InvalidOperationException("Preset refresh lost the touchpad/axis editing bindings.");
                }
                var save = type.GetMethod("SaveProfileChanges", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (!(bool)save.Invoke(editor, new object[] { "Offline probe" })!)
                    throw new InvalidOperationException("Could not save the isolated profile.");
                var after = XDocument.Load(Path.Combine(scratch, "Profiles", "Offline probe.xml"));
                if (!XNode.DeepEquals(before.Root, after.Root))
                {
                    File.WriteAllText(report + ".before.xml", before.ToString());
                    File.WriteAllText(report + ".after.xml", after.ToString());
                    throw new InvalidOperationException("Opening/navigating the editor changed unedited profile settings.");
                }
                if (visitPages)
                {
                    type.GetMethod("SelectWorkspaceSection")!.Invoke(editor, new object[] { 4 });
                    editor.UpdateLayout(); DrainDispatcher();
                    var colour = Visuals(editor).OfType<Slider>().Single(slider =>
                        System.Windows.Data.BindingOperations.GetBinding(slider, System.Windows.Controls.Primitives.RangeBase.ValueProperty)?.Path.Path == "MainColorR");
                    colour.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, 123d);
                    colour.GetBindingExpression(System.Windows.Controls.Primitives.RangeBase.ValueProperty)!.UpdateSource();
                    var model = type.GetField("profileSettingsVM", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                    if ((int)model.GetType().GetProperty("MainColorR")!.GetValue(model)! != 123)
                        throw new InvalidOperationException("The page edit did not update the profile model.");
                    type.GetMethod("SelectWorkspaceSection")!.Invoke(editor, new object[] { 0 });
                    type.GetMethod("SelectWorkspaceSection")!.Invoke(editor, new object[] { 4 });
                    editor.UpdateLayout(); DrainDispatcher();
                    if (colour.Value != 123) throw new InvalidOperationException("Navigation lost an unsaved edit.");
                    saveProfile.Invoke(null, new object[] { slot, "EditedExpected", null, true });
                    var expected = XDocument.Load(Path.Combine(scratch, "Profiles", "EditedExpected.xml"));
                    if (!(bool)save.Invoke(editor, new object[] { "Offline probe" })! ||
                        !XNode.DeepEquals(expected.Root, XDocument.Load(Path.Combine(scratch, "Profiles", "Offline probe.xml")).Root))
                        throw new InvalidOperationException("Saving a page edit changed other settings.");
                }
                Console.WriteLine("Return to mapping");
                type.GetMethod("SelectWorkspaceSection")!.Invoke(editor, new object[] { 0 });
                Console.WriteLine("Close editor");
                type.GetMethod("Close")!.Invoke(editor, null);
                Console.WriteLine("Drain closed editor");
                DrainDispatcher();
                rows.Add(new { attempt, constructedMs = constructed, boundMs = bound, firstRenderMs = rendered,
                    initiallyConstructedPages = initiallyConstructed, initialAllocatedBytes, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocations, pageTimes });
                Console.WriteLine(JsonSerializer.Serialize(rows[^1]));
            }
            File.WriteAllText(report, JsonSerializer.Serialize(new { assembly = assembly.FullName, rows }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var item in Visuals(child)) yield return item;
        }
    }

    private static void SetSlot(Type global, string name, int slot, object value)
    {
        var values = (Array)(global.GetProperty(name)?.GetValue(null) ?? global.GetField(name)!.GetValue(null))!;
        values.SetValue(value, slot);
    }

    private static void SaveImage(BitmapSource bitmap, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
