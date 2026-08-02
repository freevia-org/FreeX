using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R114: BuildBubbleModel (<c>ChartRenderer.Bubble.cs</c>) read its shared X column from
/// <c>chart.DataRange.Start.Col</c> directly instead of the substituted <c>startCol</c> value the
/// R113 embedded-fallback substitution (<c>ChartRenderer.cs</c>'s <c>BuildEmbeddedCellLookup</c>)
/// actually placed the synthesized data at.
/// <para>
/// That substitution ALWAYS writes the shared X column at column 1 and each series' Y column at
/// column 2, 4, 6... (stride 2, to leave room for the uncached size column) -- regardless of what
/// column the chart's real, resolved <see cref="ChartModel.DataRange"/> starts at. For the common
/// "unresolvable named-range series that resolves to a direct CROSS-SHEET cell reference" case
/// (e.g. a bubble chart on one sheet plotting <c>'Data'!$B$2:$D$10</c> on another), the XLSX reader
/// (<c>XlsxChartPartReader.PieBubble.cs</c>) sets <c>DataRange = UnionRanges(ranges)</c> to the
/// REAL resolved range -- Start.Col == 2 (column B), not the 1x1 placeholder every OTHER
/// embedded-fallback scenario uses. BuildBubbleModel then read xCol = 2, so its Y-scan loop
/// (`yCol = xCol + 1 .. `) started one column to the right of where the data actually was,
/// missing every single point and rendering a completely blank chart.
/// </para>
/// <para>
/// The fix threads the local `startCol` BuildPlotModel already reassigns during the embedded
/// substitution (mirroring `dataStartRow`/`endRow`/`dataStartCol`/`endCol`) into BuildBubbleModel
/// instead of it re-deriving from chart.DataRange.Start.Col.
/// </para>
/// </summary>
public sealed partial class ChartRendererTests
{
    [Fact]
    public void BubbleRenderer_EmbeddedFallback_RendersRealXyPoints_WhenDataRangeStartsAtNonOneColumn()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            // Mirrors XlsxChartPartReader.PieBubble.cs's UnionRanges(ranges) result for a resolved
            // direct cross-sheet series formula like 'Data'!$B$2:$D$10 -- Start.Col == 2 (column
            // B), NOT the (1,1)-(1,1) placeholder the "ranges.Count == 0" branch sets for a truly
            // unresolvable named range.
            DataRange = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 10, 4)),
            EmbeddedSeriesData =
            [
                // PieBubble's reader stores the cached <c:xVal> numCache (formatted as strings) as
                // this record's Categories and <c:yVal> as Values -- see
                // XlsxChartPartReader.PieBubble.cs's categoryContainerName override to "xVal".
                new ChartEmbeddedSeriesData(0, "Points", Categories: ["1", "2", "3"], Values: [5.0, 8.0, 3.0])
            ]
        };

        // Empty viewport: the live cellLookup for chart.DataRange is empty (the referenced cells
        // live on another sheet not present in this viewport), which is exactly what triggers
        // BuildPlotModel's embedded-fallback substitution.
        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle(
            "the cached (x,y) data must still produce one bubble series, not a completely blank chart")
            .Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Should().HaveCount(3,
            "3 cached (x,y) pairs must produce 3 bubble points regardless of the real worksheet column the resolved cross-sheet range started at");
        series.Points[0].X.Should().BeApproximately(1.0, 0.001);
        series.Points[0].Y.Should().BeApproximately(5.0, 0.001);
        series.Points[1].X.Should().BeApproximately(2.0, 0.001);
        series.Points[1].Y.Should().BeApproximately(8.0, 0.001);
        series.Points[2].X.Should().BeApproximately(3.0, 0.001);
        series.Points[2].Y.Should().BeApproximately(3.0, 0.001);
    }

    [Fact]
    public void BubbleRenderer_EmbeddedFallback_RendersEverySeries_WhenDataRangeStartsAtNonOneColumn()
    {
        // Same as above but with two series, to confirm the fix holds for every subsequent series
        // (the finding calls out that the one-column shift "repeats for every subsequent series"),
        // not just the first.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            DataRange = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 10, 6)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Revenue", Categories: ["1", "2"], Values: [10.0, 20.0]),
                new ChartEmbeddedSeriesData(1, "Cost", Categories: ["1", "2"], Values: [7.0, 11.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        model!.Series.Should().HaveCount(2, "2 cached series must produce 2 bubble series, not a blank chart");
        var first = model.Series[0].Should().BeOfType<ScatterSeries>().Subject;
        first.Points.Should().HaveCount(2);
        first.Points[0].X.Should().BeApproximately(1.0, 0.001);
        first.Points[0].Y.Should().BeApproximately(10.0, 0.001);
        var second = model.Series[1].Should().BeOfType<ScatterSeries>().Subject;
        second.Points.Should().HaveCount(2);
        second.Points[0].X.Should().BeApproximately(1.0, 0.001);
        second.Points[0].Y.Should().BeApproximately(7.0, 0.001);
    }

    // ---------------------------------------------------------------------------------------
    // Sibling no-regression: an ordinary LIVE-cell bubble chart whose DataRange also starts at a
    // non-1 column (e.g. columns C/D/E) must still resolve X/Y/size from the REAL DataRange start
    // column -- proving the fix (threading `startCol` instead of re-deriving from
    // chart.DataRange.Start.Col) did not accidentally break the live/non-fallback path, which must
    // keep reading directly from chart.DataRange.Start.Col (unaffected by the embedded
    // substitution since cellLookup.Count > 0 here).
    // ---------------------------------------------------------------------------------------
    [Fact]
    public void BubbleRenderer_LiveCellRangeChart_StartingAtNonOneColumn_UnaffectedByEmbeddedFallbackFix()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstRowIsHeader = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 2, 5))
        };

        var viewport = new ViewportModel(
        [
            Cell(1, 3, "1"),
            Cell(1, 4, "10"),
            Cell(1, 5, "4"),
            Cell(2, 3, "2"),
            Cell(2, 4, "20"),
            Cell(2, 5, "8")
        ], [], []);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Should().HaveCount(2);
        series.Points[0].X.Should().BeApproximately(1.0, 0.001);
        series.Points[0].Y.Should().BeApproximately(10.0, 0.001);
        series.Points[1].X.Should().BeApproximately(2.0, 0.001);
        series.Points[1].Y.Should().BeApproximately(20.0, 0.001);
    }
}
