using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for the K-print review group: (H19) hidden and filter-hidden rows/columns must
/// never appear in a print/PDF page's title or body rows/columns; (H20) <see cref="PrintLayoutPlanner.MeasurePrintableGrid"/>
/// must expose real per-row/per-column pixel offsets when the sheet has non-default row heights/column
/// widths, so cell/gridline/heading/chart/text-box placement matches the real (non-uniform) sheet
/// geometry rather than a fixed 20px row / evenly divided column.
/// </summary>
public sealed class PrintLayoutPlannerHiddenAndGeometryTests
{
    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    // ── H19: hidden rows/columns excluded from print body ─────────────────────────────────────

    [Fact]
    public void BuildRowPlans_ExcludesHiddenRowsFromBody()
    {
        var printRange = Range(1, 1, 10, 3);
        var hiddenRows = new HashSet<uint> { 5, 6, 7 };

        var pages = PrintLayoutPlanner.BuildRowPlans(
            printRange,
            repeatRows: null,
            rowsPerPage: 20,
            manualRowBreaks: null,
            isRowHidden: row => hiddenRows.Contains(row));

        pages.Should().ContainSingle();
        pages[0].BodyRows.Should().Equal(1u, 2u, 3u, 4u, 8u, 9u, 10u);
        pages[0].BodyRows.Should().NotContain(hiddenRows);
    }

    [Fact]
    public void BuildColumnPlans_ExcludesHiddenColumnsFromBody()
    {
        var printRange = Range(1, 1, 3, 10);
        var hiddenCols = new HashSet<uint> { 3, 4 };

        var pages = PrintLayoutPlanner.BuildColumnPlans(
            printRange,
            repeatColumns: null,
            columnsPerPage: 20,
            manualColumnBreaks: null,
            isColumnHidden: col => hiddenCols.Contains(col));

        pages.Should().ContainSingle();
        pages[0].BodyColumns.Should().Equal(1u, 2u, 5u, 6u, 7u, 8u, 9u, 10u);
        pages[0].BodyColumns.Should().NotContain(hiddenCols);
    }

    [Fact]
    public void BuildRowPlans_ExcludesHiddenTitleRowsFromRepeatSet()
    {
        // Row 1 is configured as a repeat/title row but is itself hidden: it must not be reprinted.
        var printRange = Range(1, 1, 5, 2);

        var pages = PrintLayoutPlanner.BuildRowPlans(
            printRange,
            repeatRows: new WorksheetRepeatRange(1, 1),
            rowsPerPage: 20,
            manualRowBreaks: null,
            isRowHidden: row => row == 1);

        pages.Should().ContainSingle();
        pages[0].TitleRows.Should().BeEmpty();
        pages[0].BodyRows.Should().Equal(2u, 3u, 4u, 5u);
    }

    [Fact]
    public void BuildRowPlans_NoHiddenPredicateBehavesExactlyAsBefore()
    {
        // Backward-compatibility guard: omitting isRowHidden must reproduce the pre-fix behavior
        // (every row in range included) so existing callers without hidden-row data are unaffected.
        var printRange = Range(1, 1, 5, 2);

        var pages = PrintLayoutPlanner.BuildRowPlans(printRange, repeatRows: null, rowsPerPage: 20);

        pages.Should().ContainSingle();
        pages[0].BodyRows.Should().Equal(1u, 2u, 3u, 4u, 5u);
    }

    [Fact]
    public void BuildRowPlans_AllRowsHiddenProducesEmptyBody()
    {
        var printRange = Range(1, 1, 3, 2);

        var pages = PrintLayoutPlanner.BuildRowPlans(
            printRange,
            repeatRows: null,
            rowsPerPage: 20,
            manualRowBreaks: null,
            isRowHidden: _ => true);

        pages.Should().BeEmpty();
    }

    // ── H20: MeasurePrintableGrid uses real per-row/per-column geometry ────────────────────────

    [Fact]
    public void MeasurePrintableGrid_RealGeometryOverload_UsesUniformFallbackWhenNoOverrides()
    {
        // With no explicit row-height/column-width overrides, the real-geometry overload must
        // measure identically to the original fixed-size overload (backward compatibility for the
        // common all-default-sheet case).
        IReadOnlyList<uint> pageRows = [1, 2, 3, 4, 5];
        IReadOnlyList<uint> pageColumns = [1, 2, 3, 4];

        var uniform = PrintLayoutPlanner.MeasurePrintableGrid(
            printableWidth: 400,
            printableHeight: 200,
            rowCount: (uint)pageRows.Count,
            columnCount: (uint)pageColumns.Count,
            printHeadings: true);

        var realGeometry = PrintLayoutPlanner.MeasurePrintableGrid(
            printableWidth: 400,
            printableHeight: 200,
            pageRows,
            pageColumns,
            rowHeightsPixels: new Dictionary<uint, double>(),
            columnWidthsPixels: new Dictionary<uint, double>(),
            printHeadings: true);

        realGeometry.ColumnWidth.Should().Be(uniform.ColumnWidth);
        realGeometry.RowHeight.Should().Be(uniform.RowHeight);
        realGeometry.HeaderWidth.Should().Be(uniform.HeaderWidth);
        realGeometry.HeaderHeight.Should().Be(uniform.HeaderHeight);
        for (var i = 0; i < pageColumns.Count; i++)
            realGeometry.ColumnOffset(i).Should().Be(i * uniform.ColumnWidth);
        for (var i = 0; i < pageRows.Count; i++)
            realGeometry.RowOffset(i).Should().Be(i * uniform.RowHeight);
    }

    [Fact]
    public void MeasurePrintableGrid_RealGeometryOverload_HonorsExplicitRowHeightOverrides()
    {
        // Row 2 is 3x the default 20px height; the cumulative offset of row 3 (index 2) must
        // reflect the real height, not a uniform 20px-per-row assumption.
        IReadOnlyList<uint> pageRows = [1, 2, 3];
        IReadOnlyList<uint> pageColumns = [1, 2];
        var rowHeights = new Dictionary<uint, double> { [2] = 60.0 };

        var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
            printableWidth: 400,
            printableHeight: 400,
            pageRows,
            pageColumns,
            rowHeightsPixels: rowHeights,
            columnWidthsPixels: new Dictionary<uint, double>(),
            printHeadings: false);

        // Row 1 (index 0) at offset 0, height 20 (default fallback).
        measurement.RowOffset(0).Should().Be(0);
        measurement.RowHeightAt(0).Should().Be(20);
        // Row 2 (index 1) starts at 20, is 60px tall.
        measurement.RowOffset(1).Should().Be(20);
        measurement.RowHeightAt(1).Should().Be(60);
        // Row 3 (index 2) starts after the tall row: 20 + 60 = 80.
        measurement.RowOffset(2).Should().Be(80);
        measurement.RowHeightAt(2).Should().Be(20);
        measurement.TotalRowHeight(pageRows.Count).Should().Be(100);
    }

    [Fact]
    public void MeasurePrintableGrid_RealGeometryOverload_HonorsExplicitColumnWidthOverrides()
    {
        // Column A (col 1) is wide (120px, above the 40px print-column floor); column B (col 2) uses
        // the uniform fallback.
        IReadOnlyList<uint> pageRows = [1];
        IReadOnlyList<uint> pageColumns = [1, 2, 3];
        var columnWidths = new Dictionary<uint, double> { [1] = 120.0 };

        var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
            printableWidth: 300,
            printableHeight: 100,
            pageRows,
            pageColumns,
            rowHeightsPixels: new Dictionary<uint, double>(),
            columnWidthsPixels: columnWidths,
            printHeadings: false);

        var uniformColumnWidth = measurement.ColumnWidth;
        measurement.ColumnOffset(0).Should().Be(0);
        measurement.ColumnWidthAt(0).Should().Be(120);
        measurement.ColumnOffset(1).Should().Be(120);
        measurement.ColumnWidthAt(1).Should().Be(uniformColumnWidth);
        measurement.ColumnOffset(2).Should().Be(120 + uniformColumnWidth);
    }

    [Fact]
    public void PrintGridMeasurement_ColumnOffset_FallsBackToUniformMultiplicationWithoutOffsets()
    {
        var measurement = new PrintGridMeasurement(HeaderWidth: 0, HeaderHeight: 0, ColumnWidth: 50, RowHeight: 18);

        measurement.ColumnOffset(3).Should().Be(150);
        measurement.RowOffset(4).Should().Be(72);
        measurement.ColumnWidthAt(3).Should().Be(50);
        measurement.RowHeightAt(4).Should().Be(18);
    }
}
