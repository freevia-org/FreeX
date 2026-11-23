using FreeX.Core.Model;
using FreeX.App.Presentation.Text;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// One series of numeric data fed to the layout engine. <see cref="Values"/> is in category order;
/// a null entry is a blank/gap point. For scatter series <see cref="XValues"/> carries the explicit
/// x coordinate of each point (otherwise the category index is used).
/// </summary>
public sealed class ChartSeriesData
{
    public required int SeriesIndex { get; init; }
    public string? Name { get; init; }
    public required IReadOnlyList<double?> Values { get; init; }
    public IReadOnlyList<double>? XValues { get; init; }

    /// <summary>
    /// For bubble series: the per-point size dimension (same order as <see cref="Values"/>). A null
    /// or missing entry falls back to a default size. Ignored by non-bubble chart types.
    /// </summary>
    public IReadOnlyList<double?>? SizeValues { get; init; }

    /// <summary>
    /// For stock series: the per-category high values (same order as <see cref="Values"/>, which then
    /// carries the close). When set, <see cref="LowValues"/> must also be set; <see cref="OpenValues"/>
    /// is optional (open-high-low-close). Ignored by non-stock chart types.
    /// </summary>
    public IReadOnlyList<double?>? HighValues { get; init; }

    /// <summary>For stock series: the per-category low values. See <see cref="HighValues"/>.</summary>
    public IReadOnlyList<double?>? LowValues { get; init; }

    /// <summary>For stock series: the per-category open values (open-high-low-close). Optional.</summary>
    public IReadOnlyList<double?>? OpenValues { get; init; }

    /// <summary>
    /// For stock series: the legend names of the merged columns in plotting order (Open?, High, Low, Close),
    /// taken from the header cells. Null falls back to the English column roles.
    /// </summary>
    public IReadOnlyList<string>? StockLegendNames { get; init; }
}

/// <summary>
/// Portable chart source data after embedded-cache fallback, row/column transposition, source
/// mapping, category formatting, and family-specific series extraction have been resolved.
/// </summary>
public sealed class ChartDataPlan
{
    public required IReadOnlyList<string> Categories { get; init; }
    public required IReadOnlyList<ChartSeriesData> Series { get; init; }
}

/// <summary>
/// Input to <see cref="ChartLayoutEngine"/>: the chart definition, the resolved numeric series data,
/// the category labels, the plot rectangle to lay out inside, and the text measurer used for any
/// label-size-dependent placement. Decoupling the numeric data from cell lookup keeps the layer
/// portable — the desktop hosts resolve cells/embedded caches into <see cref="ChartSeriesData"/>
/// before calling the engine.
/// </summary>
public sealed class ChartLayoutRequest
{
    public required ChartModel Chart { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
    public required IReadOnlyList<ChartSeriesData> Series { get; init; }
    public required PlotRect PlotArea { get; init; }
    public required ITextMeasurer TextMeasurer { get; init; }

    /// <summary>
    /// When true (the default) the legend gutter is carved out of <see cref="PlotArea"/>. Hosts that
    /// already know the final plot rectangle (their own renderer reserved the legend) set this to false
    /// so the series are laid out inside <see cref="PlotArea"/> unchanged.
    /// </summary>
    public bool ReserveLegendGutter { get; init; } = true;

    /// <summary>
    /// The chart object's full pixel size. Manual (dragged) plot-area, legend and data-label layouts are
    /// fractions of the chart space, so they are honored only when both are positive (0 = unknown).
    /// </summary>
    public double ChartWidth { get; init; }

    public double ChartHeight { get; init; }

    /// <summary>
    /// Gutters (px) the plot gives up to axis titles on its left, right and bottom edges, so a titled axis keeps
    /// its title inside the chart instead of drawing over the tick labels or the legend. Resolved by the engine
    /// from the laid-out axes (see <see cref="ChartLayoutEngine.Layout"/>); hosts leave these at zero.
    /// </summary>
    public double AxisTitleGutterLeft { get; init; }

    public double AxisTitleGutterRight { get; init; }

    public double AxisTitleGutterBottom { get; init; }

    /// <summary>A copy of this request that carries the chart object's pixel size.</summary>
    public ChartLayoutRequest WithChartSize(double chartWidth, double chartHeight) =>
        Clone(chartWidth, chartHeight, AxisTitleGutterLeft, AxisTitleGutterRight, AxisTitleGutterBottom);

    /// <summary>A copy of this request that reserves the given axis-title gutters.</summary>
    public ChartLayoutRequest WithAxisTitleGutters(double left, double right, double bottom) =>
        Clone(ChartWidth, ChartHeight, left, right, bottom);

    private ChartLayoutRequest Clone(double chartWidth, double chartHeight, double gutterLeft, double gutterRight, double gutterBottom) =>
        new()
        {
            Chart = Chart,
            Categories = Categories,
            Series = Series,
            PlotArea = PlotArea,
            TextMeasurer = TextMeasurer,
            ReserveLegendGutter = ReserveLegendGutter,
            ChartWidth = chartWidth,
            ChartHeight = chartHeight,
            AxisTitleGutterLeft = gutterLeft,
            AxisTitleGutterRight = gutterRight,
            AxisTitleGutterBottom = gutterBottom,
        };
}
