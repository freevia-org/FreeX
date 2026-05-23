using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetValueSanitizer
{
    public static TEnum ValidEnumOrDefault<TEnum>(TEnum value, TEnum defaultValue)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : defaultValue;

    public static double NonNegativeFiniteOrDefault(double value, double defaultValue) =>
        double.IsFinite(value) && value >= 0 ? value : defaultValue;

    public static WorksheetPageMargins ValidPageMarginsOrDefault(
        WorksheetPageMargins margins,
        WorksheetPageMargins defaultValue) =>
        IsNonNegativeFinite(margins.Left) &&
        IsNonNegativeFinite(margins.Right) &&
        IsNonNegativeFinite(margins.Top) &&
        IsNonNegativeFinite(margins.Bottom)
            ? margins
            : defaultValue;

    public static WorksheetScaleToFit ValidScaleToFitOrDefault(
        WorksheetScaleToFit scaleToFit,
        WorksheetScaleToFit defaultValue) =>
        scaleToFit.ScalePercent is < 10 or > 400 ||
        scaleToFit.FitToPagesWide is < 1 ||
        scaleToFit.FitToPagesTall is < 1
            ? defaultValue
            : scaleToFit;

    public static int ValidZoomPercentOrDefault(int value) =>
        value is >= 10 and <= 400 ? value : 100;

    private static bool IsNonNegativeFinite(double value) =>
        double.IsFinite(value) && value >= 0;
}
