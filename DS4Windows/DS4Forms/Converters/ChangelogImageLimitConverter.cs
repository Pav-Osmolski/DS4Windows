using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DS4WinWPF.DS4Forms.Converters;

public sealed class ChangelogImageLimitConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double size || !double.IsFinite(size) || size <= 0) return 1d;
        // Leave space for document padding, the scrollbar and nested lists.
        return parameter as string == "Height" ? Math.Clamp(size * 0.8, 1, 640) : Math.Max(1, size - 80);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
