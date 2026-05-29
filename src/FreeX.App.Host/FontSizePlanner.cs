namespace FreeX.App.Host;

public static class FontSizePlanner
{
    private const double MinimumFontSize = 1.0;
    private const double SmallFontSizeThreshold = 10.0;
    private const double LargeFontSizeThreshold = 24.0;
    private const double LargeDecreaseThreshold = 26.0;
    private const double MinimumFittingRowHeight = 18.0;
    private const double PointsPerInch = 72.0;
    private const double DeviceIndependentPixelsPerInch = 96.0;
    private const double RowHeightPadding = 5.0;

    public static double Increase(double currentSize) =>
        currentSize switch
        {
            < SmallFontSizeThreshold => currentSize + 1,
            < LargeFontSizeThreshold => currentSize + 2,
            _ => currentSize + 4
        };

    public static double Decrease(double currentSize) =>
        currentSize switch
        {
            <= SmallFontSizeThreshold => Math.Max(MinimumFontSize, currentSize - 1),
            <= LargeDecreaseThreshold => currentSize - 2,
            _ => currentSize - 4
        };

    public static double EstimateFittingRowHeight(double fontSize) =>
        Math.Max(
            MinimumFittingRowHeight,
            Math.Ceiling(fontSize * DeviceIndependentPixelsPerInch / PointsPerInch + RowHeightPadding));
}
