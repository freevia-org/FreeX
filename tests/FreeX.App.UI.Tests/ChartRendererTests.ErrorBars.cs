using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    // R25-chart-axis-series-deep-3: ChartModel has no per-series error-bar list, so
    // XlsxChartTrendlineErrorBarReader keeps only the FIRST <c:ser>'s <c:errBars> spec chart-wide. For
    // Custom-kind amounts that spec is a specific cached plus/minus value per point read off ONE series
    // in the source file; painting it onto every plotted series fabricates whiskers Excel never drew on
    // the other series. The renderer should draw Custom-kind whiskers on at most one series (the first
    // whose own point count matches the cached range's length) instead of every series that supports
    // error bars.
    [Fact]
    public void ColumnRenderer_CustomErrorBarsDoNotFabricateOntoEveryOtherSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.Custom,
            ErrorBarPlusRangeCacheXml = "<numCache><pt idx=\"0\"><v>1</v></pt><pt idx=\"1\"><v>2</v></pt></numCache>"
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "S1"), Cell(1, 3, "S2"), Cell(1, 4, "S3"),
                Cell(2, 1, "A"), Cell(2, 2, "10"), Cell(2, 3, "20"), Cell(2, 4, "30"),
                Cell(3, 1, "B"), Cell(3, 2, "15"), Cell(3, 3, "25"), Cell(3, 4, "35")
            ],
            [],
            []));

        var barSeriesCount = model.Series.OfType<RectangleBarSeries>().Count();
        barSeriesCount.Should().Be(3, "all three plotted columns render regardless of error bars");

        var whiskerSeries = model.Series.OfType<LineSeries>().ToList();
        whiskerSeries.Should().ContainSingle(
            "the custom plus/minus cache belongs to only one series in the source file, so only one series should get whiskers");
    }

    // Sibling/opposite case that must keep working: Standard Error (and Percentage/Fixed Value) amounts
    // are recomputed per series (or are a single chart-wide constant), matching Excel's "select the whole
    // chart, add error bars" gesture, which legitimately annotates every supporting series identically.
    [Fact]
    public void ColumnRenderer_StandardErrorBarsStillAnnotateEverySupportingSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.StandardError
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "S1"), Cell(1, 3, "S2"), Cell(1, 4, "S3"),
                Cell(2, 1, "A"), Cell(2, 2, "10"), Cell(2, 3, "20"), Cell(2, 4, "30"),
                Cell(3, 1, "B"), Cell(3, 2, "15"), Cell(3, 3, "25"), Cell(3, 4, "35")
            ],
            [],
            []));

        var barSeriesCount = model.Series.OfType<RectangleBarSeries>().Count();
        barSeriesCount.Should().Be(3);

        var whiskerSeries = model.Series.OfType<LineSeries>().ToList();
        whiskerSeries.Should().HaveCount(3, "a chart-wide error-bar amount legitimately annotates every supporting series");
    }
}
