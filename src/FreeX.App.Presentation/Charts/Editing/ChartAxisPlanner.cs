using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>A number-format choice for the "Format Axis" dialog: the value plus its English label.</summary>
public sealed record ChartAxisNumberFormatChoice(ChartDataLabelNumberFormat NumberFormat, string DisplayName);

/// <summary>
/// The axis bounds / number-format / gridline state read from a chart and edited back through the dialog.
/// A null <see cref="Minimum"/> / <see cref="Maximum"/> means "auto" (let the renderer pick). The first
/// parameters are the original cross-platform axis dialog surface; the optional tail carries the fuller WPF
/// format-axis surface without breaking existing callers.
/// </summary>
public readonly record struct ChartAxisInput(
    bool UseXAxis,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    bool LogScale,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowMajorGridlines,
    bool ShowMinorGridlines,
    double? MinorUnit = null,
    CellColor? MajorGridlineColor = null,
    CellColor? MinorGridlineColor = null,
    double? GridlineThickness = null,
    ChartAxisTickStyle? MajorTickStyle = null,
    ChartAxisTickStyle? MinorTickStyle = null,
    bool? ShowLabels = null,
    CellColor? LabelTextColor = null,
    double? LabelFontSize = null,
    double? LabelAngle = null,
    CellColor? LineColor = null,
    double? LineThickness = null);

/// <summary>A semantic validation failure for the "Format Axis" dialog input.</summary>
public enum ChartAxisValidationIssue
{
    MinimumNotBelowMaximum,
    MajorUnitNotPositive,
    MinorUnitNotPositive,
    GridlineThicknessNotPositive,
    LabelFontSizeOutOfRange,
    LabelAngleOutOfRange,
    LineThicknessOutOfRange,
}

/// <summary>
/// Portable (no UI) planner for the "Format Axis" editing dialog: per-axis (X/Y) bounds (min/max, with null
/// meaning auto), major unit, log scale, axis number format, and major/minor gridline visibility.
/// Single-sources the offered number formats and validates the bounds (min must be below max; major unit
/// must be positive) before projecting an edited <see cref="ChartAxisInput"/> into the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. When
/// both bounds are auto the matching <c>ClearAxisBounds</c> flag is set so the command clears any stale
/// numeric bounds. Reused across every shell.
/// </summary>
public static class ChartAxisPlanner
{
    public const double MinGridlineThickness = 0.25;
    public const double MaxGridlineThickness = 10;
    public const double MinLabelFontSize = 6;
    public const double MaxLabelFontSize = 72;
    public const double MinLabelAngle = -90;
    public const double MaxLabelAngle = 90;
    public const double MinLineThickness = 0.5;
    public const double MaxLineThickness = 10;

    private static readonly ChartAxisNumberFormatChoice[] NumberFormatCatalog =
    [
        new(ChartDataLabelNumberFormat.General, "General"),
        new(ChartDataLabelNumberFormat.Number, "Number"),
        new(ChartDataLabelNumberFormat.Currency, "Currency"),
        new(ChartDataLabelNumberFormat.Percent, "Percentage"),
    ];

    /// <summary>The selectable axis number formats, in display order.</summary>
    public static IReadOnlyList<ChartAxisNumberFormatChoice> GetNumberFormatChoices() => NumberFormatCatalog;

    /// <summary>The English display label for <paramref name="numberFormat"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartDataLabelNumberFormat numberFormat)
    {
        foreach (var choice in NumberFormatCatalog)
        {
            if (choice.NumberFormat == numberFormat)
                return choice.DisplayName;
        }

        return numberFormat.ToString();
    }

    /// <summary>True when <paramref name="type"/> has axes to format (false for pie/doughnut).</summary>
    public static bool SupportsAxes(ChartType type) => ChartTypeSupport.SupportsAxes(type);

    /// <summary>Reads the chart's current state for the chosen axis into the dialog input shape.</summary>
    public static ChartAxisInput Read(ChartModel chart, bool useXAxis) => Normalize(useXAxis
        ? new ChartAxisInput(
            UseXAxis: true,
            Minimum: chart.XAxisMinimum,
            Maximum: chart.XAxisMaximum,
            MajorUnit: chart.XAxisMajorUnit,
            LogScale: chart.XAxisLogScale,
            NumberFormat: chart.XAxisNumberFormat,
            ShowMajorGridlines: chart.ShowXAxisMajorGridlines,
            ShowMinorGridlines: chart.ShowXAxisMinorGridlines,
            MinorUnit: chart.XAxisMinorUnit,
            MajorGridlineColor: chart.XAxisMajorGridlineColor,
            MinorGridlineColor: chart.XAxisMinorGridlineColor,
            GridlineThickness: chart.XAxisGridlineThickness,
            MajorTickStyle: chart.XAxisMajorTickStyle,
            MinorTickStyle: chart.XAxisMinorTickStyle,
            ShowLabels: chart.ShowXAxisLabels,
            LabelTextColor: chart.XAxisLabelTextColor,
            LabelFontSize: chart.XAxisLabelFontSize,
            LabelAngle: chart.XAxisLabelAngle,
            LineColor: chart.XAxisLineColor,
            LineThickness: chart.XAxisLineThickness)
        : new ChartAxisInput(
            UseXAxis: false,
            Minimum: chart.YAxisMinimum,
            Maximum: chart.YAxisMaximum,
            MajorUnit: chart.YAxisMajorUnit,
            LogScale: chart.YAxisLogScale,
            NumberFormat: chart.YAxisNumberFormat,
            ShowMajorGridlines: chart.ShowYAxisMajorGridlines,
            ShowMinorGridlines: chart.ShowYAxisMinorGridlines,
            MinorUnit: chart.YAxisMinorUnit,
            MajorGridlineColor: chart.YAxisMajorGridlineColor,
            MinorGridlineColor: chart.YAxisMinorGridlineColor,
            GridlineThickness: chart.YAxisGridlineThickness,
            MajorTickStyle: chart.YAxisMajorTickStyle,
            MinorTickStyle: chart.YAxisMinorTickStyle,
            ShowLabels: chart.ShowYAxisLabels,
            LabelTextColor: chart.YAxisLabelTextColor,
            LabelFontSize: chart.YAxisLabelFontSize,
            LabelAngle: chart.YAxisLabelAngle,
            LineColor: chart.YAxisLineColor,
            LineThickness: chart.YAxisLineThickness));

    /// <summary>
    /// Normalizes result/default state for the axis dialog: unknown enum values fall back to Excel-like
    /// defaults, non-finite optional numbers become auto, and style dimensions are clamped to accepted
    /// command ranges before projection.
    /// </summary>
    public static ChartAxisInput Normalize(ChartAxisInput input) =>
        input with
        {
            Minimum = FiniteOrNull(input.Minimum),
            Maximum = FiniteOrNull(input.Maximum),
            MajorUnit = PositiveFiniteOrNull(input.MajorUnit),
            MinorUnit = PositiveFiniteOrNull(input.MinorUnit),
            NumberFormat = IsKnownNumberFormat(input.NumberFormat) ? input.NumberFormat : ChartDataLabelNumberFormat.General,
            GridlineThickness = ClampFiniteOrNull(input.GridlineThickness, 1, MinGridlineThickness, MaxGridlineThickness),
            MajorTickStyle = NormalizeTickStyle(input.MajorTickStyle, ChartAxisTickStyle.Outside),
            MinorTickStyle = NormalizeTickStyle(input.MinorTickStyle, ChartAxisTickStyle.None),
            LabelFontSize = ClampFiniteOrNull(input.LabelFontSize, 11, MinLabelFontSize, MaxLabelFontSize),
            LabelAngle = ClampFiniteOrNull(input.LabelAngle, 0, MinLabelAngle, MaxLabelAngle),
            LineThickness = ClampFiniteOrNull(input.LineThickness, 1, MinLineThickness, MaxLineThickness),
        };

    /// <summary>
    /// Validates the edited axis bounds. Returns null when the input is valid, otherwise an English reason
    /// the change is rejected (min not below max, or a non-positive major unit). Auto (null) bounds are
    /// always valid.
    /// </summary>
    public static string? Validate(ChartAxisInput input)
    {
        var issue = ValidateIssue(input);
        return issue switch
        {
            ChartAxisValidationIssue.MinimumNotBelowMaximum => "The axis minimum must be less than the maximum.",
            ChartAxisValidationIssue.MajorUnitNotPositive => "The major unit must be greater than zero.",
            ChartAxisValidationIssue.MinorUnitNotPositive => "The minor unit must be greater than zero.",
            ChartAxisValidationIssue.GridlineThicknessNotPositive => "The gridline width must be greater than zero.",
            ChartAxisValidationIssue.LabelFontSizeOutOfRange => "The label font size must be between 6 and 72.",
            ChartAxisValidationIssue.LabelAngleOutOfRange => "The label angle must be between -90 and 90.",
            ChartAxisValidationIssue.LineThicknessOutOfRange => "The axis line width must be between 0.5 and 10.",
            _ => null,
        };
    }

    /// <summary>
    /// Validates the edited axis input and returns the semantic field that failed, if any. Text parsing and
    /// localized message ownership stay with the shell; the domain limits live here.
    /// </summary>
    public static ChartAxisValidationIssue? ValidateIssue(ChartAxisInput input)
    {
        if (input.Minimum is { } min && input.Maximum is { } max && min >= max)
            return ChartAxisValidationIssue.MinimumNotBelowMaximum;

        if (!IsPositiveOptional(input.MajorUnit))
            return ChartAxisValidationIssue.MajorUnitNotPositive;

        if (!IsPositiveOptional(input.MinorUnit))
            return ChartAxisValidationIssue.MinorUnitNotPositive;

        if (input.GridlineThickness is { } gridlineThickness && !IsPositiveFinite(gridlineThickness))
            return ChartAxisValidationIssue.GridlineThicknessNotPositive;

        if (input.LabelFontSize is { } labelFontSize && !IsFiniteInRange(labelFontSize, MinLabelFontSize, MaxLabelFontSize))
            return ChartAxisValidationIssue.LabelFontSizeOutOfRange;

        if (input.LabelAngle is { } labelAngle && !IsFiniteInRange(labelAngle, MinLabelAngle, MaxLabelAngle))
            return ChartAxisValidationIssue.LabelAngleOutOfRange;

        if (input.LineThickness is { } lineThickness && !IsFiniteInRange(lineThickness, MinLineThickness, MaxLineThickness))
            return ChartAxisValidationIssue.LineThicknessOutOfRange;

        return null;
    }

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited axis state. An invalid/unknown number
    /// format falls back to General. When both bounds are auto (null) the matching axis-bounds clear flag is
    /// set so the command resets any stale numeric bounds on that axis.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartAxisInput input)
    {
        var normalized = Normalize(input);
        var clearBounds = normalized.Minimum is null && normalized.Maximum is null;

        return normalized.UseXAxis
            ? new ChartLayoutOptions(
                XAxisMinimum: normalized.Minimum,
                XAxisMaximum: normalized.Maximum,
                XAxisMajorUnit: normalized.MajorUnit,
                XAxisMinorUnit: normalized.MinorUnit,
                XAxisLogScale: normalized.LogScale,
                XAxisNumberFormat: normalized.NumberFormat,
                ShowXAxisMajorGridlines: normalized.ShowMajorGridlines,
                ShowXAxisMinorGridlines: normalized.ShowMinorGridlines,
                XAxisMajorGridlineColor: normalized.MajorGridlineColor,
                XAxisMinorGridlineColor: normalized.MinorGridlineColor,
                XAxisGridlineThickness: normalized.GridlineThickness,
                XAxisMajorTickStyle: normalized.MajorTickStyle,
                XAxisMinorTickStyle: normalized.MinorTickStyle,
                ShowXAxisLabels: normalized.ShowLabels,
                XAxisLabelTextColor: normalized.LabelTextColor,
                XAxisLabelFontSize: normalized.LabelFontSize,
                XAxisLabelAngle: normalized.LabelAngle,
                XAxisLineColor: normalized.LineColor,
                XAxisLineThickness: normalized.LineThickness,
                ClearXAxisBounds: clearBounds)
            : new ChartLayoutOptions(
                YAxisMinimum: normalized.Minimum,
                YAxisMaximum: normalized.Maximum,
                YAxisMajorUnit: normalized.MajorUnit,
                YAxisMinorUnit: normalized.MinorUnit,
                YAxisLogScale: normalized.LogScale,
                YAxisNumberFormat: normalized.NumberFormat,
                ShowYAxisMajorGridlines: normalized.ShowMajorGridlines,
                ShowYAxisMinorGridlines: normalized.ShowMinorGridlines,
                YAxisMajorGridlineColor: normalized.MajorGridlineColor,
                YAxisMinorGridlineColor: normalized.MinorGridlineColor,
                YAxisGridlineThickness: normalized.GridlineThickness,
                YAxisMajorTickStyle: normalized.MajorTickStyle,
                YAxisMinorTickStyle: normalized.MinorTickStyle,
                ShowYAxisLabels: normalized.ShowLabels,
                YAxisLabelTextColor: normalized.LabelTextColor,
                YAxisLabelFontSize: normalized.LabelFontSize,
                YAxisLabelAngle: normalized.LabelAngle,
                YAxisLineColor: normalized.LineColor,
                YAxisLineThickness: normalized.LineThickness,
                ClearYAxisBounds: clearBounds);
    }

    private static double? FiniteOrNull(double? value) =>
        value is { } number && double.IsFinite(number) ? number : null;

    private static double? PositiveFiniteOrNull(double? value) =>
        value is { } number && IsPositiveFinite(number) ? number : null;

    private static bool IsPositiveOptional(double? value) =>
        value is null || IsPositiveFinite(value.Value);

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;

    private static bool IsFiniteInRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    private static double? ClampFiniteOrNull(double? value, double fallback, double min, double max) =>
        value is null ? null : Math.Clamp(double.IsFinite(value.Value) ? value.Value : fallback, min, max);

    private static bool IsKnownNumberFormat(ChartDataLabelNumberFormat numberFormat)
    {
        foreach (var choice in NumberFormatCatalog)
        {
            if (choice.NumberFormat == numberFormat)
                return true;
        }

        return false;
    }

    private static ChartAxisTickStyle? NormalizeTickStyle(ChartAxisTickStyle? tickStyle, ChartAxisTickStyle fallback) =>
        tickStyle is null ? null : IsKnownTickStyle(tickStyle.Value) ? tickStyle.Value : fallback;

    private static bool IsKnownTickStyle(ChartAxisTickStyle tickStyle) =>
        tickStyle is ChartAxisTickStyle.None
            or ChartAxisTickStyle.Inside
            or ChartAxisTickStyle.Outside
            or ChartAxisTickStyle.Cross;
}
