using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DS4Windows;
using Microsoft.Win32;

namespace DS4WinWPF.DS4Forms;

internal sealed class ProfileIconWindow : Window
{
    private readonly ProfileEntity profile;
    private ProfileAppearance draft;
    private readonly ProfileIconBadge preview = new() { Width = 62, Height = 62, Margin = new Thickness(0, 0, 18, 0) };
    private readonly RadioButton initial = new() { Content = UiText.Get("First letter"), GroupName = "ProfileIcon" };
    private readonly RadioButton custom = new() { Content = UiText.Get("Custom image"), GroupName = "ProfileIcon", Margin = new Thickness(20, 0, 0, 0) };
    private readonly StackPanel colours = new(), imageOptions = new();
    private readonly TextBlock fileLabel = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button apply, choose;
    private bool busy, closed;

    internal ProfileIconWindow(ProfileEntity profile)
    {
        this.profile = profile; draft = profile.Appearance;
        Title = UiText.Get("Profile icon"); Width = 480; MinWidth = 420;
        SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 60);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(StyleProperty, "WindowStyle");
        SetResourceReference(BackgroundProperty, "SurfaceBackgroundColor");
        SetResourceReference(ForegroundProperty, "ForegroundColor");
        var panel = new StackPanel { Margin = new Thickness(22) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(BackgroundProperty, "SurfaceBackgroundColor");
        Content = scroll;
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        var identity = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        identity.ColumnDefinitions.Add(new ColumnDefinition());
        identity.Children.Add(preview);
        var profileName = new TextBlock { Text = profile.Name, TextWrapping = TextWrapping.Wrap, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(profileName, 1); identity.Children.Add(profileName);
        panel.Children.Add(identity);
        var modes = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        modes.Children.Add(initial); modes.Children.Add(custom); panel.Children.Add(modes);
        initial.Checked += (_, _) => { message.Text = ""; UpdatePreview(); }; custom.Checked += (_, _) => { message.Text = ""; UpdatePreview(); };
        colours.Children.Add(new TextBlock { Text = UiText.Get("Icon fill colour") });
        var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (string colour in new[] { "#2F80ED", "#237C69", "#7950B5", "#B64452", "#A45C24", "#52667D" })
        {
            var swatch = MakeButton("", () => { draft = draft with { Fill = colour }; UpdatePreview(); });
            swatch.Padding = new Thickness(5);
            swatch.Content = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)) };
            swatch.ToolTip = colour;
            System.Windows.Automation.AutomationProperties.SetName(swatch, UiText.Get("Icon fill colour") + " " + colour);
            swatches.Children.Add(swatch);
        }
        swatches.Children.Add(MakeButton(UiText.Get("Custom colour…"), PickColour)); colours.Children.Add(swatches);
        colours.Children.Add(Muted(UiText.Get("The letter follows the profile name.")));
        panel.Children.Add(colours);
        choose = MakeButton(UiText.Get("Choose image…"), ChooseImage);
        imageOptions.Children.Add(choose); imageOptions.Children.Add(fileLabel);
        imageOptions.Children.Add(Muted(UiText.Get("ICO or PNG. Transparency is preserved.")));
        panel.Children.Add(imageOptions);
        message.SetResourceReference(TextBlock.ForegroundProperty, "DangerColor"); panel.Children.Add(message);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        actions.Children.Add(MakeButton(UiText.Get("Restore default"), () => { draft = new(); initial.IsChecked = true; UpdatePreview(); }));
        var cancel = MakeButton(UiText.Get("Cancel"), () => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        apply = MakeButton(UiText.Get("Apply"), Apply); apply.IsDefault = true; apply.SetResourceReference(StyleProperty, "BridgePrimaryButtonStyle"); actions.Children.Add(apply);
        panel.Children.Add(actions);
        Closed += (_, _) => closed = true;
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        initial.IsChecked = draft.Png == null; custom.IsChecked = draft.Png != null;
        UpdatePreview();
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundColor"); return block;
    }
    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 7, 4) };
        button.SetResourceReference(StyleProperty, "BridgeSecondaryButtonStyle");
        button.Click += (_, _) => action(); return button;
    }

    internal void UpdatePreview()
    {
        bool useImage = custom.IsChecked == true;
        var image = useImage ? draft.Image : null;
        preview.DataContext = new { profile.Name, IconLetter = profile.IconLetter, IconFill = draft.Brush, IconImage = image, UsesLetterIcon = image == null };
        colours.Visibility = useImage ? Visibility.Collapsed : Visibility.Visible;
        imageOptions.Visibility = useImage ? Visibility.Visible : Visibility.Collapsed;
        fileLabel.Text = draft.Png == null ? UiText.Get("No image selected") : UiText.Get("Embedded image");
        if (apply != null) apply.IsEnabled = !busy && (!useImage || image != null);
    }

    private void PickColour()
    {
        var picker = new ColorPickerWindow { Owner = this };
        picker.colorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(draft.Fill ?? "#2F80ED");
        picker.ColorChanged += (_, colour) => { draft = draft with { Fill = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}" }; UpdatePreview(); };
        picker.ShowDialog();
    }

    private async void ChooseImage()
    {
        var dialog = new OpenFileDialog { Filter = UiText.FileFilter("Profile icons (*.ico;*.png)|*.ico;*.png"), CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        busy = true; choose.IsEnabled = false; UpdatePreview();
        try { draft = await Task.Run(() => ProfileAppearance.Import(dialog.FileName, draft.Fill)); message.Text = ""; }
        catch (Exception error) when (ProfileAppearance.IsImageFailure(error) || error is UnauthorizedAccessException)
        { message.Text = UiText.Get("The image could not be opened.") + "\n" + error.Message; }
        finally { busy = false; choose.IsEnabled = true; if (!closed) UpdatePreview(); }
    }

    private async void Apply()
    {
        var appearance = custom.IsChecked == true ? draft : draft with { Png = null };
        if (appearance == profile.Appearance) { DialogResult = true; return; }
        busy = true; UpdatePreview();
        try
        {
            await Task.Run(() => ProfilePersistence.SaveAppearance(profile.FilePath, appearance));
            busy = false;
            if (closed) return;
            profile.FireSaved(); DialogResult = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException || ProfileAppearance.IsImageFailure(error))
        { busy = false; if (closed) return; message.Text = UiText.Get("The profile icon could not be saved.") + "\n" + error.Message; UpdatePreview(); }
    }
}
