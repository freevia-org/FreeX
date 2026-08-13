using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public static class ColumnWidthPixelMapper
{
    public const double MaximumColumnWidth = ExcelColumnWidthConverter.MaximumColumnWidth;
    public const double MaximumColumnWidthPixels = ExcelColumnWidthConverter.MaximumColumnWidthPixels;

    public static double ColumnWidthToPixels(double width) =>
        ExcelColumnWidthConverter.ColumnWidthToPixels(width);

    public static double PixelsToColumnWidth(double pixels) =>
        ExcelColumnWidthConverter.PixelsToColumnWidth(pixels);
}
