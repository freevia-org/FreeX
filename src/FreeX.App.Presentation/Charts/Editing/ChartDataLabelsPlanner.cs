using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>A data-label position choice for the "Data Labels" dialog: the value plus its English label.</summary>
public sealed record ChartDataLabelPositionChoice(ChartDataLabelPosition Position, string DisplayName);

/// <summary>
/// The data-label show/position/which-values state read from a chart and edited back through the dialog.
/// Only the fields the cross-platform "Data Labels" dialog exposes are carried here (show/hide, position,
/// and the value/series-name/category-name/percentage/legend-key toggles); richer label styling stays on
/// the model untouched.
/// </summary>
public readonly record struct ChartDataLabelsInput(
    bool ShowDataLabels,
    ChartDataLabelPosition Position,
    bool ShowValue,
    bool ShowCategoryName,
    bool ShowSeriesName,
    bool ShowPercentage,
    bool ShowLegendKey);

/// <summary>
/// Portable (no UI) planner for the "Data Labels" editing dialog (show/hide, position, and which values
/// each label prints). Single-sources the offered positions and projects an edited
/// <see cref="ChartDataLabelsInput"/> into the <see cref="ChartLayoutOptions"/> the shell hands to the Core
/// <see cref="SetChartLayoutCommand"/>. When labels are hidden the position and value toggles are still set
/// so re-showing restores the chosen configuration. Reused across every shell.
/// </summary>
public static class ChartDataLabelsPlanner
{
    // Excel's data-label placements. Order mirrors the position cycler used by the ribbon toggle.
    private static readonly ChartDataLabelPositionChoice[] PositionCatalog =
    [
        new(ChartDataLabelPosition.BestFit, "Best Fit"),
        new(ChartDataLabelPosition.OutsideEnd, "Outside End"),
        new(ChartDataLabelPosition.InsideEnd, "Inside End"),
        new(ChartDataLabelPosition.Center, "Center"),
    ];

    /// <summary>The selectable data-label positions, in display order.</summary>
    public static IReadOnlyList<ChartDataLabelPositionChoice> GetPositionChoices() => PositionCatalog;

    /// <summary>The English display label for <paramref name="position"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartDataLabelPosition position)
    {
        foreach (var choice in PositionCatalog)
        {
            if (choice.Position == position)
                return choice.DisplayName;
        }

        return position.ToString();
    }

    /// <summary>Reads the chart's current data-label state into the dialog input shape.</summary>
    public static ChartDataLabelsInput Read(ChartModel chart) =>
        new(
            chart.ShowDataLabels,
            chart.DataLabelPosition,
            chart.ShowDataLabelValue,
            chart.ShowDataLabelCategoryName,
            chart.ShowDataLabelSeriesName,
            chart.ShowDataLabelPercentage,
            chart.ShowDataLabelLegendKey);

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited data-label state. An invalid/unknown
    /// position falls back to Best Fit. When the labels are shown but no value toggle is selected the planner
    /// turns on the plain value so a shown label always prints something. The position and value toggles are
    /// always set (even when hiding) so re-showing keeps the chosen configuration.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartDataLabelsInput input)
    {
        var position = IsSelectablePosition(input.Position) ? input.Position : ChartDataLabelPosition.BestFit;

        var showValue = input.ShowValue;
        var anyValueSelected = showValue || input.ShowCategoryName || input.ShowSeriesName
            || input.ShowPercentage || input.ShowLegendKey;
        if (input.ShowDataLabels && !anyValueSelected)
            showValue = true;

        return new ChartLayoutOptions(
            ShowDataLabels: input.ShowDataLabels,
            DataLabelPosition: position,
            ShowDataLabelValue: showValue,
            ShowDataLabelCategoryName: input.ShowCategoryName,
            ShowDataLabelSeriesName: input.ShowSeriesName,
            ShowDataLabelPercentage: input.ShowPercentage,
            ShowDataLabelLegendKey: input.ShowLegendKey);
    }

    private static bool IsSelectablePosition(ChartDataLabelPosition position)
    {
        foreach (var choice in PositionCatalog)
        {
            if (choice.Position == position)
                return true;
        }

        return false;
    }
}
