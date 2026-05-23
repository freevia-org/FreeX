using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class NativeJsonValueSanitizer
{
    public static int ValidTextRotationOrDefault(int rotation) =>
        rotation == 255 || rotation is >= -90 and <= 90 ? rotation : 0;

    public static TEnum ValidEnumOrDefault<TEnum>(TEnum value, TEnum defaultValue)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : defaultValue;

    public static TEnum? ValidNullableEnumOrNull<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value is { } concrete && Enum.IsDefined(concrete) ? concrete : null;

    public static double PositiveFiniteOrDefault(double value, double defaultValue) =>
        IsPositiveFinite(value) ? value : defaultValue;

    public static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;

    public static bool IsValidRowIndex(uint row) =>
        row is >= 1 and <= CellAddress.MaxRow;

    public static bool IsValidColumnIndex(uint column) =>
        column is >= 1 and <= CellAddress.MaxCol;

    public static bool IsValidOutlineLevel(int level) =>
        level is >= 1 and <= 8;

    public static double NonNegativeFiniteOrDefault(double? value, double defaultValue) =>
        value is { } concrete && IsNonNegativeFinite(concrete) ? concrete : defaultValue;

    public static WorksheetPageMargins ValidPageMarginsOrDefault(WorksheetPageMargins margins, WorksheetPageMargins defaultValue) =>
        IsNonNegativeFinite(margins.Left) &&
        IsNonNegativeFinite(margins.Right) &&
        IsNonNegativeFinite(margins.Top) &&
        IsNonNegativeFinite(margins.Bottom)
            ? margins
            : defaultValue;

    public static WorksheetScaleToFit ValidScaleToFitOrDefault(WorksheetScaleToFit scaleToFit, WorksheetScaleToFit defaultValue) =>
        scaleToFit.ScalePercent is < 10 or > 400 ||
        scaleToFit.FitToPagesWide is < 1 ||
        scaleToFit.FitToPagesTall is < 1
            ? defaultValue
            : scaleToFit;

    public static int ValidZoomPercentOrDefault(int value) =>
        value is >= 10 and <= 400 ? value : 100;

    public static int ValidZoomPercentOrDefault(int? zoomPercent) =>
        zoomPercent is >= 10 and <= 400 ? zoomPercent.Value : 100;

    public static uint? ValidRowPaneOrNull(uint? row) =>
        row is >= 1 and <= CellAddress.MaxRow ? row : null;

    public static uint? ValidColumnPaneOrNull(uint? column) =>
        column is >= 1 and <= CellAddress.MaxCol ? column : null;

    public static uint ValidFrozenRowsOrZero(uint row) =>
        row <= CellAddress.MaxRow ? row : 0;

    public static uint ValidFrozenColumnsOrZero(uint column) =>
        column <= CellAddress.MaxCol ? column : 0;

    public static bool IsNonNegativeFinite(double value) =>
        double.IsFinite(value) && value >= 0;

    public static double NormalizeRotation(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    public static double SanitizeCropEdge(double value) =>
        double.IsFinite(value) && value > 0 ? Math.Min(0.99, value) : 0;
}
