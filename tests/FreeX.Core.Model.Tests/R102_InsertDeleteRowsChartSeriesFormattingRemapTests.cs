using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R102: the ROW-axis twin of <see cref="R102_InsertDeleteColumnsChartSeriesFormattingRemapTests"/>.
/// Inserting or deleting a whole ROW STRICTLY INSIDE a Excel "Switch Row/Column" chart's
/// (<see cref="ChartModel.SeriesInRows"/> == true) plotted <see cref="ChartModel.DataRange"/>
/// mis-attributed every SeriesIndex-keyed per-series/per-point override to the wrong series. Before
/// this fix, <c>InsertRowsCommand</c>/<c>DeleteRowsCommand</c> only shifted <see
/// cref="ChartModel.DataRange"/> (via <c>RowColumnShiftHelpers.ShiftChartRowsUp/Down</c>) and never
/// touched any SeriesIndex-keyed collection at all -- unlike the column-axis sibling fix, which at
/// least covers 13 of them. This row-axis fix additionally covers <see
/// cref="ChartModel.MultiLevelCategoryXml"/>, <see cref="ChartModel.ExplodedSlices"/>, <see
/// cref="ChartModel.RangeDataLabels"/>, <see cref="ChartModel.SeriesRangeDataLabels"/>, <see
/// cref="ChartModel.AdditionalSeriesErrorBarsXml"/> and <see
/// cref="ChartModel.AdditionalSeriesTrendlinesXml"/>, all of which <see cref="RemoveChartSeriesCommand"/>
/// already treats as SeriesIndex-keyed but the sibling column-insert/delete remap does not yet cover.
/// </summary>
public sealed class R102_InsertDeleteRowsChartSeriesFormattingRemapTests
{
    // A1:D4, SeriesInRows=true, default FirstColIsCategories=true: because ChartRenderer.BuildPlotModel
    // transposes the cell lookup for a Switch-Row/Column chart, FirstColIsCategories ends up gating
    // the first ROW (row 1) instead of the first column -- so row 1 is the header/category row and
    // rows 2/3/4 are the three plotted series at SeriesIndex 0/1/2 respectively.
    private static GridRange ThreeSeriesRowRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));

    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateThreeSeriesRowChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = ThreeSeriesRowRange(sheet),
            Type = ChartType.Column,
            SeriesInRows = true
        };
        sheet.Charts.Add(chart);
        return (sheet, ctx, chart);
    }

    [Fact]
    public void InsertRow_StrictlyInsideChartRange_RemapsSeriesFormatsAndTrendlineIndex()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesRowChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0))); // row 2 -- red
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0))); // row 3 -- green
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255))); // row 4 -- blue
        chart.PointFillColors.Add(new ChartPointFillFormat(2, 0, CellColor.FromArgb(9, 9, 9)));
        chart.ShowLinearTrendline = true;
        chart.TrendlineSeriesIndex = 2; // attached to row 4 (blue)

        // Insert one row at row 3 (before the old row 3) -- strictly between the first series row
        // (2) and the last (4), so it creates a new blank series in the middle instead of merely
        // sliding the whole plotted block.
        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            because: "DataRange grows from A1:D4 to A1:D5 (the insert lands inside it)");

        // Old row 2 (SeriesIndex 0) is untouched; old row 3 (SeriesIndex 1, green) physically moved
        // to row 4 and old row 4 (SeriesIndex 2, blue) physically moved to row 5 -- so their
        // formatting must move WITH them to SeriesIndex 2 and 3 respectively, leaving the brand-new
        // blank row (now row 3, SeriesIndex 1) with no format at all.
        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 255, 0)), // was SeriesIndex 1
            new ChartSeriesFormat(3, FillColor: CellColor.FromArgb(0, 0, 255))  // was SeriesIndex 2
        ]);
        chart.PointFillColors.Should().ContainSingle().Which.SeriesIndex.Should().Be(3); // was 2
        chart.TrendlineSeriesIndex.Should().Be(3, because: "the trendline must stay attached to the (blue) series it was on, which slid from row 4 to row 5");
        chart.ShowLinearTrendline.Should().BeTrue();
    }

    [Fact]
    public void InsertRow_StrictlyInsideChartRange_RemapsExtendedSeriesIndexKeyedCollections()
    {
        // Covers the 6 SeriesIndex-keyed collections the sibling column-axis remap does not yet
        // handle (see class doc comment): MultiLevelCategoryXml, ExplodedSlices, RangeDataLabels,
        // SeriesRangeDataLabels, AdditionalSeriesErrorBarsXml, AdditionalSeriesTrendlinesXml.
        var (sheet, ctx, chart) = CreateThreeSeriesRowChart();
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(2, "<c:cat>blue</c:cat>"));
        chart.ExplodedSlices.Add(new ChartPointExplosion(2, 0, 0.25));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(2, 0, "blue-label"));
        chart.SeriesRangeDataLabels.Add(new ChartSeriesRangeDataLabels(2, "Sheet1!$F$1:$F$1", 1, []));
        chart.AdditionalSeriesErrorBarsXml.Add(new ChartSeriesRawXmlEntry(2, "<c:errBars/>"));
        chart.AdditionalSeriesTrendlinesXml.Add(new ChartSeriesRawXmlEntry(2, "<c:trendline/>"));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.MultiLevelCategoryXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.ExplodedSlices.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.RangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.SeriesRangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.AdditionalSeriesErrorBarsXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.AdditionalSeriesTrendlinesXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
    }

    [Fact]
    public void InsertRow_StrictlyInsideChartRange_IsUndoable()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesRowChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(2, "<c:cat>blue</c:cat>"));
        chart.TrendlineSeriesIndex = 2;
        chart.ShowLinearTrendline = true;
        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);

        cmd.Apply(ctx).Success.Should().BeTrue();
        chart.TrendlineSeriesIndex.Should().Be(3);
        chart.MultiLevelCategoryXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(ThreeSeriesRowRange(sheet));
        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ]);
        chart.TrendlineSeriesIndex.Should().Be(2, because: "undo must restore the pre-insert trendline attachment");
        chart.ShowLinearTrendline.Should().BeTrue();
        chart.MultiLevelCategoryXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(2,
            because: "undo must restore the pre-insert MultiLevelCategoryXml SeriesIndex too");
    }

    [Fact]
    public void DeleteRow_StrictlyInsideChartRange_DropsRemovedSeriesFormatAndShiftsSurvivors()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesRowChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0))); // row 2 -- red
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0))); // row 3 -- green
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255))); // row 4 -- blue

        // Delete row 3 (SeriesIndex 1, the middle series) -- its own worksheet row is gone.
        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 4)),
            because: "DataRange shrinks from A1:D4 to A1:D3");

        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 0, 255)) // old row 4, was SeriesIndex 2
        ], because: "the removed row's own (green) format must be dropped, and the surviving " +
                    "(blue) series that slid up from row 4 to row 3 must keep ITS OWN format at its new position");
    }

    [Fact]
    public void DeleteRow_StrictlyInsideChartRange_IsUndoable()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesRowChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 1);

        cmd.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesFormats.Should().HaveCount(2);

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(ThreeSeriesRowRange(sheet));
        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ], because: "undo must restore the deleted-away series' own format alongside the DataRange");
    }

    [Fact]
    public void InsertRow_BeforeTheWholeChartRange_LeavesSeriesFormatsUntouched()
    {
        // Sibling/no-regression case: an insert that lands AT OR BEFORE the whole DataRange shifts
        // the ENTIRE plotted block uniformly (no new series slot is created inside it), so every
        // series keeps its own already-correct SeriesIndex and nothing here should be remapped.
        var (sheet, ctx, chart) = CreateThreeSeriesRowChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 4)),
            because: "the whole chart range slides down by one row");
        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ], because: "every series' relative position inside the (uniformly shifted) range is unchanged");
    }

    [Fact]
    public void InsertRow_StrictlyInsideChartRange_WhenSeriesInRowsIsFalse_LeavesSeriesFormatsUntouched()
    {
        // Sibling/no-regression case: for an ordinary (column-major) chart, rows are the CATEGORY
        // axis, not the series axis -- a row insert must never touch any SeriesIndex-keyed
        // collection (only the column-axis insert/delete commands may do that). This guards the
        // `if (!chart.SeriesInRows) return;` early-exit in RemapChartSeriesFormattingForRowInsert.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            Type = ChartType.Column,
            SeriesInRows = false
        };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        sheet.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ], because: "series are columns here, not rows -- a row insert must never remap SeriesIndex for this chart");
    }
}
