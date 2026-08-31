using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R17-freeze-largegrid-1: GetUsedRange() must include style-only (formatting-only, empty) cells
/// so that Ctrl+End / scroll / print extent reach a formatted-but-empty far cell, matching Excel's
/// used-range semantics.
/// </summary>
public partial class SheetTests
{
    [Fact]
    public void GetUsedRange_IncludesFarStyleOnlyCell_BeyondValueCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));

        var fill = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 199, 206) });
        // Z100: row 100, col 26 (A=1 ... Z=26).
        sheet.SetStyleOnly(100, 26, fill);

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 100, 26)));
    }

    [Fact]
    public void GetUsedRange_SheetWithOnlyStyleOnlyCell_IsNotReportedEmpty()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var fill = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(198, 239, 206) });

        sheet.SetStyleOnly(5, 3, fill);

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 5, 3),
            new CellAddress(sheet.Id, 5, 3)));
    }

    [Fact]
    public void GetUsedRange_WithNoValuesOrStyles_IsStillEmpty()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.GetUsedRange().Should().BeNull();
    }

    [Fact]
    public void GetUsedRange_IncludesStyleOnlyRunBounds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var fill = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(189, 215, 238) });
        sheet.SetStyleOnlyRuns([new StyleOnlyRun(50, 10, 20, fill)]);

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 50, 20)));
    }

    [Fact]
    public void GetUsedRange_StyleOnlyOverlayMutationsInvalidateCachedBounds()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var style = new StyleId(1);
        sheet.SetStyleOnly(5, 3, style);
        sheet.SetStyleOnly(100, 26, style);
        sheet.GetUsedRange().Should().Be(Range(sheet, 5, 3, 100, 26));

        sheet.ClearStyleOnly(100, 26);
        sheet.GetUsedRange().Should().Be(Range(sheet, 5, 3, 5, 3));

        sheet.SetStyleOnly(200, 40, style);
        sheet.GetUsedRange().Should().Be(Range(sheet, 5, 3, 200, 40));

        sheet.ClearStyleOnlyEntries();
        sheet.GetUsedRange().Should().BeNull();
    }

    [Fact]
    public void GetUsedRange_ReplacingCompressedRunsInvalidatesCachedBounds()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var style = new StyleId(1);
        sheet.SetStyleOnlyRuns([new StyleOnlyRun(50, 10, 20, style)]);
        sheet.GetUsedRange().Should().Be(Range(sheet, 50, 10, 50, 20));

        sheet.SetStyleOnlyRuns([new StyleOnlyRun(7, 2, 4, style)]);

        sheet.GetUsedRange().Should().Be(Range(sheet, 7, 2, 7, 4));
    }

    [Fact]
    public void GetUsedRange_RunBoundaryTombstonesShrinkBoundsAndOverlaysRestoreThem()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var baseStyle = new StyleId(1);
        var overrideStyle = new StyleId(2);
        sheet.SetStyleOnlyRuns([new StyleOnlyRun(10, 5, 10, baseStyle)]);
        sheet.GetUsedRange().Should().Be(Range(sheet, 10, 5, 10, 10));

        sheet.ClearStyleOnly(10, 10);
        sheet.GetUsedRange().Should().Be(Range(sheet, 10, 5, 10, 9));

        sheet.ClearStyleOnly(10, 5);
        sheet.GetUsedRange().Should().Be(Range(sheet, 10, 6, 10, 9));

        sheet.SetStyleOnly(10, 10, overrideStyle);
        sheet.SetStyleOnly(30, 40, overrideStyle);
        sheet.GetUsedRange().Should().Be(Range(sheet, 10, 6, 30, 40));

        sheet.ClearStyleOnly(30, 40);
        sheet.GetUsedRange().Should().Be(Range(sheet, 10, 6, 10, 10));
    }

    [Fact]
    public void GetUsedRange_FullyTombstonedCompressedRunIsEmpty()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetStyleOnlyRuns([new StyleOnlyRun(10, 5, 7, new StyleId(1))]);
        sheet.GetUsedRange().Should().Be(Range(sheet, 10, 5, 10, 7));

        sheet.ClearStyleOnly(10, 5);
        sheet.ClearStyleOnly(10, 6);
        sheet.ClearStyleOnly(10, 7);

        sheet.StyleOnlyCellCount.Should().Be(0);
        sheet.GetUsedRange().Should().BeNull();
    }

    [Fact]
    public void GetUsedRange_WholeRowAndColumnDefaultsRemainExcluded()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.RowStyles[500] = new StyleId(1);
        sheet.ColumnStyles[100] = new StyleId(2);

        sheet.GetUsedRange().Should().BeNull();
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
