using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>A number-format choice for the "Format Axis" dialog: the value plus its English label.</summary>
public sealed record ChartAxisNumberFormatChoice(ChartDataLabelNumberFormat NumberFormat, string DisplayName);

/// <summary>
/// The axis bounds / number-format / gridline state read from a chart and edited back through the dialog.
/// A null <see cref="Minimum"/> / <see cref="Maximum"/> means "auto" (let the renderer pick). Carries only
/// the fields the cross-platform "Format Axis" dialog exposes; richer axis styling stays on the model.
/// </summary>
public readonly record struct ChartAxisInput(
    bool UseXAxis,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    bool LogScale,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowMajorGridlines,
    bool ShowMinorGridlines);

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
    public static ChartAxisInput Read(ChartModel chart, bool useXAxis) => useXAxis
        ? new ChartAxisInput(
            UseXAxis: true,
            Minimum: chart.XAxisMinimum,
            Maximum: chart.XAxisMaximum,
            MajorUnit: chart.XAxisMajorUnit,
            LogScale: chart.XAxisLogScale,
            NumberFormat: chart.XAxisNumberFormat,
            ShowMajorGridlines: chart.ShowXAxisMajorGridlines,
            ShowMinorGridlines: chart.ShowXAxisMinorGridlines)
        : new ChartAxisInput(
            UseXAxis: false,
            Minimum: chart.YAxisMinimum,
            Maximum: chart.YAxisMaximum,
            MajorUnit: chart.YAxisMajorUnit,
            LogScale: chart.YAxisLogScale,
            NumberFormat: chart.YAxisNumberFormat,
            ShowMajorGridlines: chart.ShowYAxisMajorGridlines,
            ShowMinorGridlines: chart.ShowYAxisMinorGridlines);

    /// <summary>
    /// Validates the edited axis bounds. Returns null when the input is valid, otherwise an English reason
    /// the change is rejected (min not below max, or a non-positive major unit). Auto (null) bounds are
    /// always valid.
    /// </summary>
    public static string? Validate(ChartAxisInput input)
    {
        if (input.Minimum is { } min && input.Maximum is { } max && min >= max)
            return "The axis minimum must be less than the maximum.";

        if (input.MajorUnit is { } unit && unit <= 0)
            return "The major unit must be greater than zero.";

        return null;
    }

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited axis state. An invalid/unknown number
    /// format falls back to General. When both bounds are auto (null) the matching axis-bounds clear flag is
    /// set so the command resets any stale numeric bounds on that axis.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartAxisInput input)
    {
        var numberFormat = IsKnownNumberFormat(input.NumberFormat) ? input.NumberFormat : ChartDataLabelNumberFormat.General;
        var clearBounds = input.Minimum is null && input.Maximum is null;

        return input.UseXAxis
            ? new ChartLayoutOptions(
                XAxisMinimum: input.Minimum,
                XAxisMaximum: input.Maximum,
                XAxisMajorUnit: input.MajorUnit,
                XAxisLogScale: input.LogScale,
                XAxisNumberFormat: numberFormat,
                ShowXAxisMajorGridlines: input.ShowMajorGridlines,
                ShowXAxisMinorGridlines: input.ShowMinorGridlines,
                ClearXAxisBounds: clearBounds)
            : new ChartLayoutOptions(
                YAxisMinimum: input.Minimum,
                YAxisMaximum: input.Maximum,
                YAxisMajorUnit: input.MajorUnit,
                YAxisLogScale: input.LogScale,
                YAxisNumberFormat: numberFormat,
                ShowYAxisMajorGridlines: input.ShowMajorGridlines,
                ShowYAxisMinorGridlines: input.ShowMinorGridlines,
                ClearYAxisBounds: clearBounds);
    }

    private static bool IsKnownNumberFormat(ChartDataLabelNumberFormat numberFormat)
    {
        foreach (var choice in NumberFormatCatalog)
        {
            if (choice.NumberFormat == numberFormat)
                return true;
        }

        return false;
    }
}
