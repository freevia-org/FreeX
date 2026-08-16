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

public sealed record ChartSizeDialogValidation(
    ChartSizeDialogField Field,
    string Message);

public static class ChartSizeDialogPlanner
{
    public const ChartSizeDialogField InitialFocusField = ChartSizeDialogField.Width;

    public const string WidthValidationMessage = "Enter a positive width in points.";
    public const string HeightValidationMessage = "Enter a positive height in points.";

    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("ChartSize_Dialog_Title", "Chart Size"),
        new("ChartSize_Width_Label", "Width (pt):"),
        new("ChartSize_Height_Label", "Height (pt):"),
        new("ChartSize_Width_Validation", WidthValidationMessage),
        new("ChartSize_Height_Validation", HeightValidationMessage),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static DialogSurfaceSpec<ChartSizeDialogField> Surface { get; } = BuildSurface();

    public static DialogSurfaceSpec<ChartSizeDialogField> BuildSurface(
        Func<string, string?>? getText = null) => new(
        Title: Texts[0].Resolve(getText),
        AutomationId: "ChartSizeDialog",
        AutomationName: "Chart Size",
        Fields:
        [
            new(ChartSizeDialogField.Width, Texts[1].Resolve(getText), "ChartSizeWidthTextBox", "Chart width"),
            new(ChartSizeDialogField.Height, Texts[2].Resolve(getText), "ChartSizeHeightTextBox", "Chart height"),
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
        out ChartSizeDialogValidation? validation)
        => TryBuildResult(input, culture, getText: null, out result, out validation);

    public static bool TryBuildResult(
        ChartSizeDialogInput input,
        CultureInfo culture,
        Func<string, string?>? getText,
        out ChartSizeDialogResult? result,
        out ChartSizeDialogValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!TryParsePositive(input.WidthText, culture, out var width))
        {
            validation = new ChartSizeDialogValidation(
                ChartSizeDialogField.Width,
                Texts[3].Resolve(getText));
            return false;
        }

        if (!TryParsePositive(input.HeightText, culture, out var height))
        {
            validation = new ChartSizeDialogValidation(
                ChartSizeDialogField.Height,
                Texts[4].Resolve(getText));
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
