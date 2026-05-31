using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Describes the column layout that <see cref="ForecastSheetCommand"/> writes to a generated
/// forecast sheet so the chart planner can be decoupled from the generator's internals.
/// The fixed column layout is:
/// A = Timeline (category axis), B = Actual values, C = Forecast,
/// D = Lower Confidence Bound, E = Upper Confidence Bound.
/// </summary>
public readonly record struct ForecastChartLayout(SheetId Sheet, uint HeaderRow, uint LastRow)
{
    /// <summary>1-based column holding the timeline / category values.</summary>
    public const uint TimelineColumn = 1;

    /// <summary>1-based column holding the historical actual values.</summary>
    public const uint ActualColumn = 2;

    /// <summary>1-based column holding the forecast values.</summary>
    public const uint ForecastColumn = 3;

    /// <summary>1-based column holding the lower confidence bound.</summary>
    public const uint LowerBoundColumn = 4;

    /// <summary>1-based column holding the upper confidence bound.</summary>
    public const uint UpperBoundColumn = 5;
}

/// <summary>
/// Pure, deterministic planner that turns a generated forecast-sheet layout into the chart
/// definition the existing chart-create path (<see cref="AddChartCommand"/> / <see cref="ChartModel"/>)
/// consumes: a line chart plotting Actual, Forecast and the lower/upper confidence bounds against
/// the timeline category axis. Has no WPF dependency and is fully unit-testable.
/// </summary>
public static class ForecastChartPlanner
{
    // Series indexes (0-based) as interpreted by ChartTypeSupport given FirstColIsCategories=true.
    private const int ActualSeriesIndex = 0;
    private const int ForecastSeriesIndex = 1;
    private const int LowerBoundSeriesIndex = 2;
    private const int UpperBoundSeriesIndex = 3;

    // Lighter grey for the confidence bounds so they read as a secondary band.
    private static readonly CellColor ConfidenceBoundColor = new(170, 170, 170);

    /// <summary>
    /// Build the forecast chart definition for the given layout. Deterministic for a given layout.
    /// </summary>
    public static ChartModel Plan(ForecastChartLayout layout)
    {
        var dataRange = new GridRange(
            new CellAddress(layout.Sheet, layout.HeaderRow, ForecastChartLayout.TimelineColumn),
            new CellAddress(layout.Sheet, layout.LastRow, ForecastChartLayout.UpperBoundColumn));

        return new ChartModel
        {
            Type = ChartType.Line,
            DataRange = dataRange,
            FirstColIsCategories = true,
            FirstRowIsHeader = true,
            Title = "Forecast",
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Bottom,
            // Place to the right of the five data columns so it does not cover the table.
            Left = 360,
            Top = 20,
            Width = 480,
            Height = 320,
            SeriesFormats =
            [
                // Actual and Forecast keep their default theme colours but render as solid lines
                // with markers so the historical and projected segments read clearly.
                new ChartSeriesFormat(
                    ActualSeriesIndex,
                    DashStyle: ChartLineDashStyle.Solid,
                    MarkerStyle: ChartMarkerStyle.Circle),
                new ChartSeriesFormat(
                    ForecastSeriesIndex,
                    DashStyle: ChartLineDashStyle.Solid,
                    MarkerStyle: ChartMarkerStyle.Circle),
                // Confidence bounds: lighter grey dashed lines, no markers, so they read as a band.
                new ChartSeriesFormat(
                    LowerBoundSeriesIndex,
                    StrokeColor: ConfidenceBoundColor,
                    DashStyle: ChartLineDashStyle.Dash,
                    MarkerStyle: ChartMarkerStyle.None),
                new ChartSeriesFormat(
                    UpperBoundSeriesIndex,
                    StrokeColor: ConfidenceBoundColor,
                    DashStyle: ChartLineDashStyle.Dash,
                    MarkerStyle: ChartMarkerStyle.None),
            ],
        };
    }
}
