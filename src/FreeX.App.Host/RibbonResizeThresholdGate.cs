namespace FreeX.App.Host;

internal static class RibbonResizeThresholdGate
{
    public static bool CrossedAnyThreshold(
        double previousWidth,
        double currentWidth,
        IReadOnlyList<double> thresholds) =>
        Free.Shared.Ribbon.RibbonResizeThresholdGate.CrossedAnyThreshold(
            previousWidth,
            currentWidth,
            thresholds);
}
