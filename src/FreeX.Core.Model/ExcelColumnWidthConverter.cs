namespace FreeX.Core.Model;

/// <summary>Converts between Excel character widths and viewport pixels.</summary>
public static class ExcelColumnWidthConverter
{
    public const double MaximumColumnWidth = 255.0;
    public const double MaximumColumnWidthPixels = 1790.0;

    public static double ColumnWidthToPixels(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
            return 0;

        return width < 1
            ? Math.Round(width * 12.0, MidpointRounding.AwayFromZero)
            : Math.Round(width * 7.0 + 5.0, MidpointRounding.AwayFromZero);
    }

    public static double PixelsToColumnWidth(double pixels)
    {
        if (!double.IsFinite(pixels) || pixels <= 0)
            return 0;

        var clampedPixels = Math.Min(pixels, MaximumColumnWidthPixels);
        return clampedPixels <= 12.0
            ? clampedPixels / 12.0
            : Math.Min(MaximumColumnWidth, (clampedPixels - 5.0) / 7.0);
    }
}
