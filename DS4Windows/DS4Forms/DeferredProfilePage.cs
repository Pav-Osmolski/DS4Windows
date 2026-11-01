using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace DS4WinWPF.DS4Forms;

// Keep each page's visual tree for this editing session, but do not construct
// hidden pages. Profile values live in the editor model, not in this container.
public sealed class DeferredProfilePage : ContentControl
{
    public DataTemplate PageTemplate { get; set; }
    public bool IsMaterialized => Content != null;
    internal bool Ready { get; set; }
    internal bool Retired { get; set; }
    private bool constructing;
    public event EventHandler<ProfilePageMaterializingEventArgs> Materializing;

    public DeferredProfilePage()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsVisibleChanged += (_, _) => { if (IsVisible) EnsureContent(); };
        Loaded += (_, _) => { if (IsVisible) EnsureContent(); };
    }

    internal void EnsureContent()
    {
        if (!Ready || Retired || constructing || IsMaterialized || PageTemplate == null) return;
        var watch = Stopwatch.StartNew();
        constructing = true;
        try
        {
            PageTemplate.Seal();
            var root = (FrameworkElement)PageTemplate.LoadContent();
            // Assign named controls and initialise their bindings before the page
            // joins the visual tree and can raise selection/Loaded events.
            Materializing?.Invoke(this, new ProfilePageMaterializingEventArgs(root));
            Content = root;
        }
        finally { constructing = false; }
        if (DS4Windows.Global.ProfileEditorTimingLogging)
            DS4Windows.AppLogger.LogToGui($"[ProfileEditorTiming] page {Tag} constructed: phase={watch.ElapsedMilliseconds}ms", false);
    }
}

public sealed class ProfilePageMaterializingEventArgs : EventArgs
{
    public FrameworkElement Root { get; }
    internal ProfilePageMaterializingEventArgs(FrameworkElement root)
        => Root = root;
    public T Find<T>(string name) where T : FrameworkElement
        => Root is UserControl && Root is T control ? control :
            Root.FindName(name) as T ?? throw new InvalidOperationException($"Missing deferred page control: {name}");
}
