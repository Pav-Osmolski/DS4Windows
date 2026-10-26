using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DS4WinWPF.DS4Forms;

public sealed class ProfileIconBadge : Grid
{
    static ProfileIconBadge()
    {
        // Metadata defaults allow a DataTemplate to request a smaller badge.
        // Constructor-local Width/Height values override template attributes.
        WidthProperty.OverrideMetadata(typeof(ProfileIconBadge), new FrameworkPropertyMetadata(38.0));
        HeightProperty.OverrideMetadata(typeof(ProfileIconBadge), new FrameworkPropertyMetadata(38.0));
    }

    public ProfileIconBadge()
    {
        var visibility = new BooleanToVisibilityConverter();
        var letters = new Grid();
        letters.SetBinding(VisibilityProperty, new Binding("UsesLetterIcon") { Converter = visibility });
        var background = new Border { CornerRadius = new CornerRadius(5) };
        background.SetResourceReference(Border.BackgroundProperty, "AccentColor");
        letters.Children.Add(background);
        var custom = new Border { CornerRadius = new CornerRadius(5) };
        custom.SetBinding(Border.BackgroundProperty, new Binding("IconFill"));
        letters.Children.Add(custom);
        var letter = new TextBlock { Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letter.SetBinding(TextBlock.TextProperty, new Binding("IconLetter")); letters.Children.Add(letter);
        // Scale the complete badge, including text and rounded corners, as one
        // drawing. Row-sized hosts must not crop a full-size internal badge.
        var drawing = new Grid { Width = 38, Height = 38 };
        drawing.Children.Add(letters);
        var image = new Image { Stretch = Stretch.Uniform };
        image.SetBinding(Image.SourceProperty, new Binding("IconImage")); drawing.Children.Add(image);
        var scaled = new Viewbox { Stretch = Stretch.Uniform, Child = drawing };
        RenderOptions.SetBitmapScalingMode(scaled, BitmapScalingMode.HighQuality);
        Children.Add(scaled);
        SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("Name"));
    }
}
