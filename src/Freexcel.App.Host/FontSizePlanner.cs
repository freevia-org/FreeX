namespace Freexcel.App.Host;

public static class FontSizePlanner
{
    public static double Increase(double currentSize) =>
        currentSize switch
        {
            < 10 => currentSize + 1,
            < 24 => currentSize + 2,
            _ => currentSize + 4
        };

    public static double Decrease(double currentSize) =>
        currentSize switch
        {
            <= 10 => Math.Max(1, currentSize - 1),
            <= 26 => currentSize - 2,
            _ => currentSize - 4
        };

    public static double EstimateFittingRowHeight(double fontSize) =>
        Math.Max(18.0, Math.Ceiling(fontSize * 96.0 / 72.0 + 5.0));
}
