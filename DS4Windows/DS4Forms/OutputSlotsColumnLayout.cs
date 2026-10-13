using System;
using System.Linq;

namespace DS4WinWPF.DS4Forms;

public static class OutputSlotsColumnLayout
{
    public static double[] Normalize(double[] widths)
    {
        double[] minimums = [60, 220, 140, 140, 110];
        if (widths == null || widths.Length != minimums.Length ||
            widths.Any(width => !double.IsFinite(width) || width <= 0 || width > 4000))
            return Array.Empty<double>();
        return widths.Select((width, index) => Math.Max(width, minimums[index])).ToArray();
    }
}
