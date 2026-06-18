using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>A trendline-type choice for the "Trendline" dialog: the value plus its English label.</summary>
public sealed record ChartTrendlineTypeChoice(ChartTrendlineType Type, string DisplayName);

/// <summary>
/// The trendline show/type/parameter state read from a chart and edited back through the dialog. The period
/// applies to a moving-average trendline; the order applies to a polynomial trendline; both are ignored for
/// the other types. Carries only the fields the cross-platform "Trendline" dialog exposes.
/// </summary>
public readonly record struct ChartTrendlineInput(
    bool ShowTrendline,
    ChartTrendlineType Type,
    int Period,
    int Order,
    bool ShowEquation,
    bool ShowRSquared);

/// <summary>
/// Portable (no UI) planner for the "Trendline" editing dialog (linear / exponential / logarithmic / power /
/// moving-average / polynomial, plus the equation and R-squared readouts). Single-sources the offered
/// trendline types, clamps the moving-average period and polynomial order into Excel's ranges, and projects
/// an edited <see cref="ChartTrendlineInput"/> into the <see cref="ChartLayoutOptions"/> the shell hands to
/// the Core <see cref="SetChartLayoutCommand"/>. Whether a chart can carry a trendline at all is gated by
/// <see cref="SupportsTrendlines"/> (column/line/bar/scatter/bubble/area). Reused across every shell.
/// </summary>
public static class ChartTrendlinePlanner
{
    /// <summary>Excel's smallest/largest moving-average period.</summary>
    public const int MinPeriod = 2;
    public const int MaxPeriod = 255;

    /// <summary>Excel's smallest/largest polynomial order.</summary>
    public const int MinOrder = 2;
    public const int MaxOrder = 6;

    private static readonly ChartTrendlineTypeChoice[] TypeCatalog =
    [
        new(ChartTrendlineType.Linear, "Linear"),
        new(ChartTrendlineType.Exponential, "Exponential"),
        new(ChartTrendlineType.Logarithmic, "Logarithmic"),
        new(ChartTrendlineType.Power, "Power"),
        new(ChartTrendlineType.MovingAverage, "Moving Average"),
        new(ChartTrendlineType.Polynomial, "Polynomial"),
    ];

    /// <summary>The selectable trendline types, in display order.</summary>
    public static IReadOnlyList<ChartTrendlineTypeChoice> GetTypeChoices() => TypeCatalog;

    /// <summary>The English display label for <paramref name="type"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartTrendlineType type)
    {
        foreach (var choice in TypeCatalog)
        {
            if (choice.Type == type)
                return choice.DisplayName;
        }

        return type.ToString();
    }

    /// <summary>True when <paramref name="type"/> can carry a trendline (column/line/bar/scatter/bubble/area).</summary>
    public static bool SupportsTrendlines(ChartType type) => ChartTypeSupport.SupportsTrendlines(type);

    /// <summary>Reads the chart's current trendline state into the dialog input shape.</summary>
    public static ChartTrendlineInput Read(ChartModel chart) =>
        new(
            chart.ShowLinearTrendline,
            chart.TrendlineType,
            chart.TrendlinePeriod < MinPeriod ? MinPeriod : chart.TrendlinePeriod,
            chart.TrendlineOrder < MinOrder ? MinOrder : chart.TrendlineOrder,
            chart.ShowTrendlineEquation,
            chart.ShowTrendlineRSquared);

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited trendline state. An invalid/unknown
    /// type falls back to Linear; the period and order are clamped into Excel's ranges. The type, period,
    /// order, and readout toggles are always set (even when hiding) so re-showing keeps the chosen
    /// configuration.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartTrendlineInput input)
    {
        var type = IsKnownType(input.Type) ? input.Type : ChartTrendlineType.Linear;
        return new ChartLayoutOptions(
            ShowLinearTrendline: input.ShowTrendline,
            TrendlineType: type,
            TrendlinePeriod: Math.Clamp(input.Period, MinPeriod, MaxPeriod),
            TrendlineOrder: Math.Clamp(input.Order, MinOrder, MaxOrder),
            ShowTrendlineEquation: input.ShowEquation,
            ShowTrendlineRSquared: input.ShowRSquared);
    }

    private static bool IsKnownType(ChartTrendlineType type)
    {
        foreach (var choice in TypeCatalog)
        {
            if (choice.Type == type)
                return true;
        }

        return false;
    }
}
