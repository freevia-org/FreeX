using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R113-render-chart-embedded-fallback-all-types.
/// <para>
/// r110-r112 taught the XLSX <em>readers</em> to fall back to a chart series' embedded
/// <c>&lt;c:numCache&gt;</c>/<c>&lt;c:strCache&gt;</c> (or chartEx <c>&lt;cx:lvl&gt;/&lt;cx:pt&gt;</c>)
/// cache when its series formula is an unresolvable named range (e.g. the OFFSET-based
/// "auto-expanding chart" pattern) or an unreachable cross-sheet reference, and r112 fixed
/// <c>ChartTypeSupport.GetDataSeriesCount</c>/<c>GetDataPointCount</c> to consult
/// <see cref="ChartModel.EmbeddedSeriesData"/> too. The data therefore survived load and save --
/// but <see cref="ChartRenderer"/> (the WPF renderer under test here) still iterated the
/// live-cell lookup directly for every chart type except Column/Bar (the old
/// <c>BuildPlotModelFromEmbeddedData</c> implemented only those two), so a fallback-loaded Line,
/// Pie, Scatter, Radar, Stacked, or chartEx (Waterfall/Histogram/Pareto/BoxAndWhisker/Treemap/
/// Sunburst/Funnel) chart still rendered completely blank: the actual user-facing defect.
/// </para>
/// <para>
/// The fix synthesizes an ordinary cellLookup + row/column bounds from
/// <see cref="ChartModel.EmbeddedSeriesData"/> ONCE, in <c>BuildPlotModel</c>, before any
/// chart-type-specific code runs (<c>ChartRenderer.BuildEmbeddedCellLookup</c>) -- so every branch
/// below (the inline Column/Bar/Area/Line/Scatter loop, the Pie/Doughnut block, and every
/// extracted BuildXxxModel helper) renders a fallback-loaded chart through EXACTLY the same code
/// that renders a live cell-range-backed one.
/// </para>
/// <para>
/// These tests construct <see cref="ChartModel"/> instances directly with
/// <see cref="ChartModel.EmbeddedSeriesData"/> populated and an EMPTY viewport (no live cells for
/// the chart's <see cref="ChartModel.DataRange"/>) -- exactly the same style already established
/// by the sibling <c>ChartRendererTests.CrossSheetCache.cs</c> file for the Column-chart case this
/// round is generalizing: that file tests the RENDERER's consumption of EmbeddedSeriesData
/// directly, independent of how the data got there (a live reader round-trip is exercised
/// separately by the IO-layer R110-R112 tests), so a hand-built ChartModel is the right fixture at
/// this layer -- not the "hand-authored chart XML" the round-trip fixture rule warns against.
/// </para>
/// </summary>
public sealed partial class ChartRendererTests
{
    // -----------------------------------------------------------------------------------------
    // THE FIX: Line, Pie, Scatter, and one chartEx type (Waterfall) must render real series/point
    // data from EmbeddedSeriesData when the live cellLookup is empty for the chart's DataRange.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void LineRenderer_EmbeddedFallback_RendersRealPoints_WhenLiveCellsEmpty()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Revenue", Categories: ["Jan", "Feb", "Mar"], Values: [10.0, 20.0, 15.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>().Subject;
        series.Points.Should().HaveCount(3, "3 cached values must produce 3 line points, not a blank chart");
        series.Points[0].Y.Should().BeApproximately(10.0, 0.001);
        series.Points[1].Y.Should().BeApproximately(20.0, 0.001);
        series.Points[2].Y.Should().BeApproximately(15.0, 0.001);
    }

    [Fact]
    public void PieRenderer_EmbeddedFallback_RendersRealSlices_WhenLiveCellsEmpty()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Share", Categories: ["North", "South", "East"], Values: [40.0, 35.0, 25.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var pie = model!.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        pie.Slices.Should().HaveCount(3, "3 cached values must produce 3 pie slices, not a blank chart");
        pie.Slices[0].Value.Should().BeApproximately(40.0, 0.001);
        pie.Slices[0].Label.Should().Be("North");
    }

    [Fact]
    public void ScatterRenderer_EmbeddedFallback_RendersRealXyPoints_WhenLiveCellsEmpty()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                // Scatter's reader stores the <c:xVal> numCache as this record's Categories (as
                // strings) and <c:yVal> as Values -- see XlsxChartPartReader.Scatter.cs's
                // TryReadEmbeddedSeriesData(..., valueContainerName: "yVal", categoryContainerName: "xVal").
                new ChartEmbeddedSeriesData(0, "Points", Categories: ["1", "2", "3"], Values: [5.0, 8.0, 3.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Should().HaveCount(3, "3 cached (x,y) pairs must produce 3 scatter points, not a blank chart");
        series.Points[0].X.Should().BeApproximately(1.0, 0.001);
        series.Points[0].Y.Should().BeApproximately(5.0, 0.001);
        series.Points[1].X.Should().BeApproximately(2.0, 0.001);
        series.Points[1].Y.Should().BeApproximately(8.0, 0.001);
    }

    [Fact]
    public void WaterfallRenderer_EmbeddedFallback_RendersRealBars_WhenLiveCellsEmpty()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Waterfall,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Bridge", Categories: ["Start", "Q1", "Q2", "End"], Values: [100.0, 20.0, -10.0, 110.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var bars = model!.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        bars.Items.Should().HaveCount(4, "4 cached values must produce 4 waterfall bars, not a blank chart");
    }

    // -----------------------------------------------------------------------------------------
    // A few more representative types, since the fix is a single shared substitution point that
    // is expected to cover every chart-type branch, not just the four above.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void RadarRenderer_EmbeddedFallback_RendersRealPoints_WhenLiveCellsEmpty()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Radar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Skill", Categories: ["Speed", "Power", "Agility"], Values: [3.0, 4.0, 2.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>().Subject;
        // Radar closes the polygon by repeating the first point, so 3 cached values -> 4 points.
        series.Points.Should().HaveCount(4, "3 cached values must produce a closed 4-point radar polygon, not a blank chart");
    }

    [Fact]
    public void DoughnutRenderer_EmbeddedFallback_RendersRealSlices_WhenLiveCellsEmpty()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Share", Categories: ["A", "B"], Values: [60.0, 40.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var pie = model!.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        pie.Slices.Should().HaveCount(2);
    }

    [Fact]
    public void StackedColumnRenderer_EmbeddedFallback_RendersRealBars_WhenLiveCellsEmpty()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "SeriesA", Categories: ["Q1", "Q2"], Values: [3.0, 5.0]),
                new ChartEmbeddedSeriesData(1, "SeriesB", Categories: ["Q1", "Q2"], Values: [7.0, 2.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        model!.Series.Should().HaveCount(2, "2 cached series must produce 2 stacked bar series, not a blank chart");
        foreach (var series in model.Series)
            series.Should().BeOfType<RectangleBarSeries>().Which.Items.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------------------------
    // Sibling no-regression: an ordinary cell-range chart of the SAME types must still render
    // identically from live cells (embedded fallback must never override live data).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void LineRenderer_OrdinaryCellRangeChart_UnaffectedByEmbeddedFallbackFix()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1)),
            FirstRowIsHeader = false,
            FirstColIsCategories = false
        };

        var viewport = new ViewportModel(
        [
            Cell(1, 1, "10"),
            Cell(2, 1, "20"),
            Cell(3, 1, "30")
        ], [], []);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>().Subject;
        series.Points.Should().HaveCount(3);
        series.Points[0].Y.Should().BeApproximately(10.0, 0.001);
        series.Points[2].Y.Should().BeApproximately(30.0, 0.001);
    }

    [Fact]
    public void PieRenderer_OrdinaryCellRangeChart_UnaffectedByEmbeddedFallbackFix()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1)),
            FirstRowIsHeader = false,
            FirstColIsCategories = false
        };

        var viewport = new ViewportModel(
        [
            Cell(1, 1, "60"),
            Cell(2, 1, "40")
        ], [], []);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull();
        var pie = model!.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        pie.Slices.Should().HaveCount(2);
        pie.Slices[0].Value.Should().BeApproximately(60.0, 0.001);
    }

    [Fact]
    public void ScatterRenderer_OrdinaryCellRangeChart_UnaffectedByEmbeddedFallbackFix()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var viewport = new ViewportModel(
        [
            Cell(1, 1, "1"),
            Cell(1, 2, "5"),
            Cell(2, 1, "2"),
            Cell(2, 2, "8")
        ], [], []);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Should().HaveCount(2);
        series.Points[0].X.Should().BeApproximately(1.0, 0.001);
        series.Points[0].Y.Should().BeApproximately(5.0, 0.001);
    }

    [Fact]
    public void WaterfallRenderer_OrdinaryCellRangeChart_UnaffectedByEmbeddedFallbackFix()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1))
        };

        var viewport = new ViewportModel(
        [
            Cell(1, 1, "100"),
            Cell(2, 1, "20")
        ], [], []);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull();
        var bars = model!.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        bars.Items.Should().HaveCount(2);
    }
}
