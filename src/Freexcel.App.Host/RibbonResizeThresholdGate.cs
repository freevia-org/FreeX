namespace Freexcel.App.Host;

internal static class RibbonResizeThresholdGate
{
    public static bool CrossedAnyThreshold(
        double previousWidth,
        double currentWidth,
        IReadOnlyList<double> thresholds)
    {
        foreach (var threshold in thresholds)
        {
            if (CrossedThreshold(previousWidth, currentWidth, threshold))
                return true;
        }

        return false;
    }

    private static bool CrossedThreshold(double previousWidth, double currentWidth, double threshold) =>
        previousWidth > threshold && currentWidth <= threshold ||
        previousWidth <= threshold && currentWidth > threshold;
}
