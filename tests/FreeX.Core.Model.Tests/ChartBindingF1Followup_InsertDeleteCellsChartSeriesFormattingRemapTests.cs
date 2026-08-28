using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// chart-binding F1 follow-up: InsertCellsCommand/DeleteCellsCommand (the band-scoped "Insert
/// Cells... / Delete Cells..." dialog commands) shift/collapse a chart's <see
/// cref="ChartModel.DataRange"/> (see <see cref="ChartBindingF1_InsertDeleteCellsChartDataRangeTests"/>)
/// but, before this fix, never remapped any SeriesIndex/PointIndex-keyed per-series/per-point
/// formatting (<see cref="ChartModel.SeriesFormats"/>, <see cref="ChartModel.PointFillColors"/>,
/// etc.) the way the whole-row/whole-column siblings do (see
/// <see cref="R102_InsertDeleteColumnsChartSeriesFormattingRemapTests"/> and
/// <see cref="R102_InsertDeleteRowsChartSeriesFormattingRemapTests"/>). Deleting/inserting a middle
/// series column (or row, for a Switch-Row/Column chart) via this band-scoped path therefore left a
/// stale index that silently reattached to whatever series/point slid into that ordinal slot.
/// </summary>
public sealed class ChartBindingF1Followup_InsertDeleteCellsChartSeriesFormattingRemapTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    // A1:D10, default FirstColIsCategories=true: column A is categories, columns B/C/D (2/3/4) are
    // the three plotted series at SeriesIndex 0/1/2 respectively -- matches the finding's user
    // gesture (3+ series columns, edit a middle one over the chart's full row extent).
    private static ChartModel ThreeSeriesColumnChart(Sheet sheet) => new()
    {
        DataRange = Range(sheet.Id, 1, 1, 10, 4),
        Type = ChartType.Column
    };

    // A1:D4, SeriesInRows=true: row 1 is the header/category row, rows 2/3/4 are the three plotted
    // series at SeriesIndex 0/1/2 -- the ROW-axis sibling (see R102_InsertDeleteRowsChart...).
    private static ChartModel ThreeSeriesRowChart(Sheet sheet) => new()
    {
        DataRange = Range(sheet.Id, 1, 1, 4, 4),
        Type = ChartType.Column,
        SeriesInRows = true
    };

    // ══════════════════════════════════════════════════════════════════════════
    // Reproduces the finding's probe scenario: Delete Cells > Shift Cells Left over a middle
    // series column spanning the chart's full row extent.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteCells_ShiftLeft_MiddleSeriesColumn_RemapsSeriesFormatsAndPointFormat()
    {
        var (_, sheet, ctx) = Setup();
        var chart = ThreeSeriesColumnChart(sheet);
        sheet.Charts.Add(chart);
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0))); // B -- red
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0))); // C -- green
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255))); // D -- blue
        // Custom point fill on a point in series index 2 (column D), matching the finding's
        // "Format Data Point" gesture.
        chart.PointFillColors.Add(new ChartPointFillFormat(2, 0, CellColor.FromArgb(9, 9, 9)));

        // Select C1:C10 -- header + all data rows of the middle series column, the SAME row extent
        // as the chart's DataRange -- and Delete Cells > Shift Cells Left.
        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 1, 3, 10, 3), DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 10, 3),
            because: "DataRange shrinks from A1:D10 to A1:C10");

        // Old B (SeriesIndex 0, red) is untouched. Old C (SeriesIndex 1, green) is the deleted
        // column -- its format must be DROPPED, not silently reattached to whatever now sits at
        // index 1. Old D (SeriesIndex 2, blue) physically slid left to C, so its format -- and the
        // point format on it -- must move WITH it to SeriesIndex 1.
        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 0, 255)) // was SeriesIndex 2
        ], because: "the deleted (green) column's own format must be dropped, and the surviving " +
                    "(blue) series that slid from D to C must keep ITS OWN format at its new position");
        chart.PointFillColors.Should().ContainSingle().Which.SeriesIndex.Should().Be(1,
            because: "the point format was on the series that slid from index 2 to index 1 -- it " +
                     "must not stay stuck at index 2, which is now out of range with only 2 series left");
    }

    [Fact]
    public void DeleteCells_ShiftLeft_MiddleSeriesColumn_Undo_RestoresFormatting()
    {
        var (_, sheet, ctx) = Setup();
        var chart = ThreeSeriesColumnChart(sheet);
        sheet.Charts.Add(chart);
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        chart.PointFillColors.Add(new ChartPointFillFormat(2, 0, CellColor.FromArgb(9, 9, 9)));
        var originalDataRange = chart.DataRange;

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 1, 3, 10, 3), DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesFormats.Should().HaveCount(2);

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(originalDataRange, because: "undo restores the pre-delete DataRange");
        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ], because: "undo must restore the pre-delete formatting list, not just DataRange");
        chart.PointFillColors.Should().ContainSingle().Which.SeriesIndex.Should().Be(2,
            because: "undo must restore the point format's original SeriesIndex too");
    }

    [Fact]
    public void InsertCells_ShiftRight_MiddleSeriesColumn_RemapsSeriesFormats()
    {
        var (_, sheet, ctx) = Setup();
        var chart = ThreeSeriesColumnChart(sheet);
        sheet.Charts.Add(chart);
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));

        // Insert one column at C1:C10 (before old column C) -- strictly between the first series
        // column (B) and the last (D), creating a brand-new blank series slot in the middle.
        var cmd = new InsertCellsCommand(sheet.Id, Range(sheet.Id, 1, 3, 10, 3), InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 10, 5),
            because: "DataRange grows from A1:D10 to A1:E10");
        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)), // untouched
            new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 255, 0)), // was SeriesIndex 1
            new ChartSeriesFormat(3, FillColor: CellColor.FromArgb(0, 0, 255))  // was SeriesIndex 2
        ], because: "old C and D slide right to D and E, so their formats must move to SeriesIndex 2 " +
                    "and 3, leaving the new blank column (now C, SeriesIndex 1) with no format");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ROW-axis sibling (Switch-Row/Column chart, Insert/Delete Shift Down/Up).
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertCells_ShiftDown_MiddleSeriesRow_RemapsSeriesFormats()
    {
        var (_, sheet, ctx) = Setup();
        var chart = ThreeSeriesRowChart(sheet);
        sheet.Charts.Add(chart);
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0))); // row 2
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0))); // row 3
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255))); // row 4

        // Insert one row at A3:D3 (before old row 3) -- strictly interior to the plotted series
        // row span (rows 2..4).
        var cmd = new InsertCellsCommand(sheet.Id, Range(sheet.Id, 3, 1, 3, 4), InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 5, 4),
            because: "DataRange grows from A1:D4 to A1:D5");
        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 255, 0)), // was SeriesIndex 1
            new ChartSeriesFormat(3, FillColor: CellColor.FromArgb(0, 0, 255))  // was SeriesIndex 2
        ]);
    }

    [Fact]
    public void DeleteCells_ShiftUp_MiddleSeriesRow_RemapsSeriesFormatsToSurvivors()
    {
        var (_, sheet, ctx) = Setup();
        var chart = ThreeSeriesRowChart(sheet);
        sheet.Charts.Add(chart);
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0))); // row 2
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0))); // row 3
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255))); // row 4

        // Delete row 3 (SeriesIndex 1, the middle series) via A3:D3, Shift Cells Up.
        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 3, 1, 3, 4), DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 3, 4),
            because: "DataRange shrinks from A1:D4 to A1:D3");
        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 0, 255)) // was SeriesIndex 2, row 4 -> row 3
        ], because: "the deleted (green) row's own format must be dropped, and the surviving " +
                    "(blue) series that slid from row 4 to row 3 must keep ITS OWN format at index 1");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sibling no-regression: the band-containment guard must not remap a chart this specific
    // band-scoped edit never actually touches (its DataRange lies outside the edited band), even
    // though its OTHER axis happens to straddle the insert/delete point too -- unlike the
    // unconditional whole-column/whole-row siblings, which always apply because a whole column/row
    // always spans every row/column on the sheet.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteCells_ShiftLeft_ChartDataRangeOutsideRowBand_SeriesFormattingUnaffected()
    {
        var (_, sheet, ctx) = Setup();
        // This chart's DataRange lives on rows 20..29, NOT rows 1..10 -- outside the edited band --
        // even though its columns (B/C/D) are the same ones the delete touches.
        var chart = new ChartModel { DataRange = Range(sheet.Id, 20, 1, 29, 4), Type = ChartType.Column };
        sheet.Charts.Add(chart);
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));

        // Same column (C), but rows 1..10 -- this band never contains the chart's actual DataRange
        // rows (20..29), so DataRange itself is left untouched by DeleteChartDataRangesInBandLeft.
        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 1, 3, 10, 3), DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 20, 1, 29, 4),
            because: "rows 20-29 are outside the edited 1-10 band, so DataRange is untouched");
        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)),
            new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255))
        ], because: "a chart this band-scoped edit never actually touches must not have its " +
                    "formatting remapped just because its column span happens to overlap too");
    }
}
