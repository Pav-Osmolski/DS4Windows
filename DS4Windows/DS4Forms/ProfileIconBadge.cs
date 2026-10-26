using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DS4WinWPF.DS4Forms;

public sealed class ProfileIconBadge : Grid
{
    public ProfileIconBadge()
    {
        Width = Height = 38;
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
        Children.Add(letters);
        var image = new Image { Stretch = Stretch.Uniform };
        image.SetBinding(Image.SourceProperty, new Binding("IconImage")); Children.Add(image);
        SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("Name"));
    }
}
