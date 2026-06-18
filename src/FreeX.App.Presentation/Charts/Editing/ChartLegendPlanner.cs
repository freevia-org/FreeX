using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>A legend placement choice for the "Legend" dialog: the position value plus its English label.</summary>
public sealed record ChartLegendPositionChoice(ChartLegendPosition Position, string DisplayName);

/// <summary>The legend show/position state read from a chart and edited back through the dialog.</summary>
public readonly record struct ChartLegendInput(bool ShowLegend, ChartLegendPosition Position);

/// <summary>
/// Portable (no UI) planner for the "Legend" editing options (show/hide + position: top/bottom/left/right).
/// Single-sources the offered placements and projects an edited <see cref="ChartLegendInput"/> into the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. When
/// the legend is hidden the position is held at its prior value so re-showing restores the last placement
/// (Core keeps <c>LegendPosition</c> independent of <c>ShowLegend</c>). Reused across every shell.
/// </summary>
public static class ChartLegendPlanner
{
    // Excel's four legend placements. ChartLegendPosition.None is the "hidden" state and is modeled via
    // the ShowLegend flag instead of being a selectable placement.
    private static readonly ChartLegendPositionChoice[] PositionCatalog =
    [
        new(ChartLegendPosition.Right, "Right"),
        new(ChartLegendPosition.Top, "Top"),
        new(ChartLegendPosition.Left, "Left"),
        new(ChartLegendPosition.Bottom, "Bottom"),
    ];

    /// <summary>The selectable legend placements (top/bottom/left/right), in display order.</summary>
    public static IReadOnlyList<ChartLegendPositionChoice> GetPositionChoices() => PositionCatalog;

    /// <summary>The English display label for <paramref name="position"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartLegendPosition position)
    {
        foreach (var choice in PositionCatalog)
        {
            if (choice.Position == position)
                return choice.DisplayName;
        }

        return position.ToString();
    }

    /// <summary>
    /// Reads the chart's current legend state into the dialog input shape. A stored position of
    /// <see cref="ChartLegendPosition.None"/> (legend never placed) is surfaced as the default Right so the
    /// dialog always shows a concrete placement to re-show into.
    /// </summary>
    public static ChartLegendInput Read(ChartModel chart) =>
        new(chart.ShowLegend, chart.LegendPosition == ChartLegendPosition.None ? ChartLegendPosition.Right : chart.LegendPosition);

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited legend state. An invalid/unknown
    /// position falls back to Right. <see cref="ChartLayoutOptions.LegendPosition"/> is always set (even
    /// when hiding) so re-showing the legend later keeps the chosen placement.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartLegendInput input)
    {
        var position = IsSelectablePosition(input.Position) ? input.Position : ChartLegendPosition.Right;
        return new ChartLayoutOptions(
            ShowLegend: input.ShowLegend,
            LegendPosition: position);
    }

    private static bool IsSelectablePosition(ChartLegendPosition position)
    {
        foreach (var choice in PositionCatalog)
        {
            if (choice.Position == position)
                return true;
        }

        return false;
    }
}
