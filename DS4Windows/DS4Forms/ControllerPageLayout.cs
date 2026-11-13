using System.Windows;
using System.Windows.Controls;

namespace DS4WinWPF.DS4Forms;

internal static class ControllerPageLayout
{
    internal const double SplitWidth = 724;

    internal static void ArrangePair(double width, ColumnDefinition gutter,
        ColumnDefinition secondaryColumn, FrameworkElement secondaryCard)
    {
        const double cardSpacing = 16;
        bool split = width >= SplitWidth;
        gutter.Width = new GridLength(split ? cardSpacing : 0);
        secondaryColumn.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(secondaryCard, split ? 2 : 0);
        Grid.SetRow(secondaryCard, split ? 0 : 1);
        secondaryCard.Margin = split ? default : new Thickness(0, cardSpacing, 0, 0);
    }
}
