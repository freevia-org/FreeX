namespace FreeX.App.Host;

internal static class RibbonResizeThresholdGate
{
    public static bool CrossedAnyThreshold(
        double previousWidth,
        double currentWidth,
        IReadOnlyList<double> thresholds)
    {
        if (thresholds.Count == 0 ||
            double.IsNaN(previousWidth) ||
            double.IsNaN(currentWidth))
        {
            return false;
        }

        return CountThresholdsBelowWidth(previousWidth, thresholds) !=
            CountThresholdsBelowWidth(currentWidth, thresholds);
    }

    private static int CountThresholdsBelowWidth(double width, IReadOnlyList<double> sortedThresholds)
    {
        var low = 0;
        var high = sortedThresholds.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (width > sortedThresholds[middle])
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}
