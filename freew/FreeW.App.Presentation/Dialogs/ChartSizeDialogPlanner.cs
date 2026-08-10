using System.Globalization;
using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Dialogs;

public enum ChartSizeDialogField
{
    Width,
    Height
}

public sealed record ChartSizeDialogInitialState(
    string WidthText,
    string HeightText);

public sealed record ChartSizeDialogInput(
    string? WidthText,
    string? HeightText);

public sealed record ChartSizeDialogResult(
    double WidthPt,
    double HeightPt);

public static class ChartSizeDialogPlanner
{
    public const string WidthValidationMessage = "Enter a positive width in points.";
    public const string HeightValidationMessage = "Enter a positive height in points.";

    public static DialogSurfaceSpec<ChartSizeDialogField> Surface { get; } = new(
        Title: "Chart Size",
        AutomationId: "ChartSizeDialog",
        AutomationName: "Chart Size",
        Fields:
        [
            new(ChartSizeDialogField.Width, "Width (pt):", "ChartSizeWidthTextBox", "Chart width"),
            new(ChartSizeDialogField.Height, "Height (pt):", "ChartSizeHeightTextBox", "Chart height"),
        ],
        ValidationAutomationId: "ChartSizeValidationText");

    public static ChartSizeDialogInitialState BuildInitialState(
        double widthPt,
        double heightPt,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new ChartSizeDialogInitialState(
            FormatPoints(widthPt, culture),
            FormatPoints(heightPt, culture));
    }

    public static bool TryBuildResult(
        ChartSizeDialogInput input,
        CultureInfo culture,
        out ChartSizeDialogResult? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        if (!TryParsePositive(input.WidthText, culture, out var width))
        {
            errorMessage = WidthValidationMessage;
            return false;
        }

        if (!TryParsePositive(input.HeightText, culture, out var height))
        {
            errorMessage = HeightValidationMessage;
            return false;
        }

        result = new ChartSizeDialogResult(width, height);
        return true;
    }

    public static string FormatPoints(double value, CultureInfo culture)
        => DialogNumericTextPolicy.FormatPoints(value, culture);

    private static bool TryParsePositive(string? text, CultureInfo culture, out double value)
        => DialogNumericTextPolicy.TryParsePositiveDouble(text, culture, out value);
}
