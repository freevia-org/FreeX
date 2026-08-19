using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R151 (freex-chart-data F1): the SeriesIndex-keyed remap passes in
/// RowColumnShiftHelpers.PrintAndCharts.cs (<see cref="R102_InsertDeleteRowsChartSeriesFormattingRemapTests"/>
/// and <see cref="R102_InsertDeleteColumnsChartSeriesFormattingRemapTests"/>) never touched PointIndex --
/// the point's 0-based position along the CATEGORY axis (literally `row - dataStartRow` for the default,
/// SeriesInRows == false orientation; see ChartRenderer.cs's `var row = dataStartRow + (uint)pointIndex;`).
/// A row insert/delete strictly inside an ordinary (column-series) chart's plotted data span left every
/// per-point override (<see cref="ChartModel.PointFillColors"/>, <see cref="ChartModel.PointMarkerFormats"/>,
/// <see cref="ChartModel.PointDataLabelFormats"/>, <see cref="ChartModel.ExplodedSlices"/>,
/// <see cref="ChartModel.RangeDataLabels"/>, <see cref="ChartModel.SeriesRangeDataLabels"/>) pinned to its old
/// PointIndex, silently reattaching the formatting to the wrong data point. The mirror gap existed on the
/// column axis for a SeriesInRows == true (Switch Row/Column) chart.
/// </summary>
public sealed class R151_ChartPointIndexRemapTests
{
    // A1:D10, default FirstRowIsHeader=true/FirstColIsCategories=true, SeriesInRows=false (the default,
    // overwhelmingly common orientation): column A is categories, columns B/C/D are series 0/1/2, and
    // rows 2..10 are the plotted points at PointIndex 0..8.
    private static GridRange ThreeSeriesColumnRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 4));

    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateDefaultOrientationChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = ThreeSeriesColumnRange(sheet),
            Type = ChartType.Column
        };
        sheet.Charts.Add(chart);
        return (sheet, ctx, chart);
    }

    private static void AddAllPointOverrides(ChartModel chart, int seriesIndex, int pointIndex)
    {
        chart.PointFillColors.Add(new ChartPointFillFormat(seriesIndex, pointIndex, CellColor.FromArgb(9, 9, 9)));
        chart.PointMarkerFormats.Add(new ChartPointMarkerFormat(seriesIndex, pointIndex, ChartMarkerStyle.Diamond));
        chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(seriesIndex, pointIndex, ShowValue: true));
        chart.ExplodedSlices.Add(new ChartPointExplosion(seriesIndex, pointIndex, 0.25));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(seriesIndex, pointIndex, "custom-label"));
        chart.SeriesRangeDataLabels.Add(new ChartSeriesRangeDataLabels(
            seriesIndex, "Sheet1!$F$2:$F$10", 9, [new ChartRangeDataLabelPoint(pointIndex, "custom-label")]));
    }

    [Fact]
    public void InsertRow_StrictlyInsideDefaultOrientationChart_RemapsEveryPointIndexedCollection()
    {
        var (sheet, ctx, chart) = CreateDefaultOrientationChart();
        // Format the point at row 4 (PointIndex 2, since dataStartRow=2 -> row2=idx0, row3=idx1, row4=idx2)
        // on series 1 (column C).
        AddAllPointOverrides(chart, seriesIndex: 1, pointIndex: 2);

        // Insert one row at row 3 -- strictly inside the plotted point span (rows 2..10) and BEFORE the
        // formatted point's row (4), so the formatted point physically slides from row 4 to row 5.
        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 11, 4)),
            because: "DataRange grows from A1:D10 to A1:D11 (the insert lands inside it)");

        chart.PointFillColors.Should().ContainSingle()
            .Which.Should().Match<ChartPointFillFormat>(f => f.SeriesIndex == 1 && f.PointIndex == 3);
        chart.PointMarkerFormats.Should().ContainSingle()
            .Which.Should().Match<ChartPointMarkerFormat>(f => f.SeriesIndex == 1 && f.PointIndex == 3);
        chart.PointDataLabelFormats.Should().ContainSingle()
            .Which.Should().Match<ChartPointDataLabelFormat>(f => f.SeriesIndex == 1 && f.PointIndex == 3);
        chart.ExplodedSlices.Should().ContainSingle()
            .Which.Should().Match<ChartPointExplosion>(s => s.SeriesIndex == 1 && s.PointIndex == 3);
        chart.RangeDataLabels.Should().ContainSingle()
            .Which.Should().Match<ChartRangeDataLabel>(l => l.SeriesIndex == 1 && l.PointIndex == 3);

        chart.SeriesRangeDataLabels.Should().ContainSingle();
        var rangeDef = chart.SeriesRangeDataLabels[0];
        rangeDef.SeriesIndex.Should().Be(1);
        rangeDef.PointCount.Should().Be(10, because: "the plotted point count grows by one along with the insert");
        rangeDef.Points.Should().ContainSingle().Which.PointIndex.Should().Be(3);
    }

    [Fact]
    public void DeleteRow_OverlappingFormattedPoint_DropsItAndShiftsSurvivorsDown()
    {
        var (sheet, ctx, chart) = CreateDefaultOrientationChart();
        AddAllPointOverrides(chart, seriesIndex: 1, pointIndex: 1); // row 3 -- inside the deleted band
        chart.PointFillColors.Add(new ChartPointFillFormat(1, 4, CellColor.FromArgb(1, 2, 3))); // row 6 -- survives, shifts down

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 4)));

        // The point-3 override (row 3, PointIndex 1) was inside the deleted band -- dropped entirely.
        chart.PointMarkerFormats.Should().BeEmpty();
        chart.PointDataLabelFormats.Should().BeEmpty();
        chart.ExplodedSlices.Should().BeEmpty();
        chart.RangeDataLabels.Should().BeEmpty();

        // SeriesRangeDataLabels is a per-SERIES feature definition (formula + cached point list), not
        // itself point-keyed -- deleting the one cached point inside the deleted band empties its
        // Points list and shrinks PointCount, but the definition itself (and its own SeriesIndex)
        // survives, matching how the outer record is never dropped just because one point vanished.
        chart.SeriesRangeDataLabels.Should().ContainSingle();
        var rangeDef = chart.SeriesRangeDataLabels[0];
        rangeDef.SeriesIndex.Should().Be(1);
        rangeDef.PointCount.Should().Be(8, because: "the plotted point count shrinks by one along with the delete");
        rangeDef.Points.Should().BeEmpty();

        // The row-6 override (PointIndex 4) survives and slides down to PointIndex 3 (row 6 -> row 5).
        chart.PointFillColors.Should().ContainSingle()
            .Which.Should().Match<ChartPointFillFormat>(f => f.SeriesIndex == 1 && f.PointIndex == 3);
    }

    [Fact]
    public void InsertColumn_OnDefaultOrientationChart_LeavesPointIndexUntouched()
    {
        // Sibling/no-regression case: for the default (column-series) orientation, columns are the
        // SERIES axis, not the point axis -- a column insert must never touch PointIndex (only the
        // row-axis insert/delete commands may do that). Guards the `if (!chart.SeriesInRows) return;`
        // early-exit in the new RemapChartPointFormattingForColumnInsert.
        var (sheet, ctx, chart) = CreateDefaultOrientationChart();
        AddAllPointOverrides(chart, seriesIndex: 1, pointIndex: 2);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.PointFillColors.Should().ContainSingle().Which.PointIndex.Should().Be(2,
            because: "PointIndex is row-relative in this orientation -- a column insert must not move it");
    }

    // A1:D4, SeriesInRows=true (Switch Row/Column): rows are series (0/1/2 at rows 2/3/4), and once
    // ChartRenderer transposes the plotted grid, columns become the POINT axis -- column B holds
    // PointIndex 0, C holds 1, D holds 2 (see RemapChartPointFormattingForColumnInsert's doc comment
    // for why FirstRowIsHeader, not FirstColIsCategories, gates the first point COLUMN here).
    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateSeriesInRowsChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            Type = ChartType.Column,
            SeriesInRows = true
        };
        sheet.Charts.Add(chart);
        return (sheet, ctx, chart);
    }

    [Fact]
    public void InsertColumn_StrictlyInsideSwitchRowColumnChart_RemapsPointIndex()
    {
        // Covers the "mirror gap" the finding calls out: for a SeriesInRows == true chart, a COLUMN
        // insert/delete is the point-axis edit (rows are series there instead).
        var (sheet, ctx, chart) = CreateSeriesInRowsChart();
        chart.PointFillColors.Add(new ChartPointFillFormat(0, 1, CellColor.FromArgb(9, 9, 9))); // column C -> PointIndex 1

        // Insert a column at C -- strictly inside the plotted point span (columns B..D) and before the
        // formatted point's column, so it slides from C to D.
        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 5)));
        chart.PointFillColors.Should().ContainSingle()
            .Which.Should().Match<ChartPointFillFormat>(f => f.SeriesIndex == 0 && f.PointIndex == 2);
    }

    [Fact]
    public void InsertRow_OnSwitchRowColumnChart_LeavesPointIndexUntouched()
    {
        // Sibling/no-regression case: for SeriesInRows == true, rows are the SERIES axis (already
        // covered by R102_InsertDeleteRowsChartSeriesFormattingRemapTests) -- a row insert must not
        // touch PointIndex, only SeriesIndex.
        var (sheet, ctx, chart) = CreateSeriesInRowsChart();
        chart.PointFillColors.Add(new ChartPointFillFormat(0, 1, CellColor.FromArgb(9, 9, 9)));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.PointFillColors.Should().ContainSingle().Which.PointIndex.Should().Be(1,
            because: "PointIndex is column-relative in this orientation -- a row insert must not move it");
    }
}
