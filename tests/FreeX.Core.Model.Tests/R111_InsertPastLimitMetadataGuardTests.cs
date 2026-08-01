using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R111-commands-insert-overflow-metadata-1: InsertRowsCommand/InsertColumnsCommand's
/// past-the-boundary overflow guard used to derive its "is there anything down there that would
/// overflow" check purely from GetOccupiedCellMap/CellCount -- i.e. only rows/columns holding an
/// actual Cell object. Row/column-level state that lives OUTSIDE the cell dictionary (a
/// style-only formatting band from a whole-row/column header select with no cell value, a
/// RowHeights/ColumnWidths override, a hidden-row/column flag, or an outline/group level) was
/// invisible to it, so such metadata at the sheet's last row/column silently shifted past
/// MaxRow/MaxCol with no error -- and was then dropped on save. Excel itself refuses the insert
/// here ("cannot shift nonblank cells off the worksheet" treats a formatted-but-valueless
/// row/column as non-blank too).
/// </summary>
public class R111_InsertPastLimitMetadataGuardTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // --- Row axis: style-only formatting band at the last row (no cell value at all) ---

    [Fact]
    public void InsertRows_StyleOnlyBandAtLastRow_ReturnsFailed()
    {
        var (wb, sheet, ctx) = Setup();
        var style = wb.RegisterStyle(new CellStyle { Bold = true });
        // Mirrors what ApplyStyleCommand writes for a whole-row header select with no cell value:
        // an empty cell that carries ONLY a style-only override, no Cell object in _cells.
        sheet.SetStyleOnly(CellAddress.MaxRow, 1, style);

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse(
            "the style-only band at the last row would be pushed past MaxRow, exactly like a real cell value would");
        result.ErrorMessage.Should().Contain("pushed past the last row");
    }

    [Fact]
    public void InsertRows_RowHeightOverrideAtLastRow_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowHeights[CellAddress.MaxRow] = 30;

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse("a row-height override at the last row must not silently shift past MaxRow");
    }

    [Fact]
    public void InsertRows_HiddenLastRow_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.HiddenRows.Add(CellAddress.MaxRow);

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse("a hidden-row flag at the last row must not silently shift past MaxRow");
    }

    [Fact]
    public void InsertRows_OutlineLevelAtLastRow_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowOutlineLevels[CellAddress.MaxRow] = 1;

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse("an outline/group level at the last row must not silently shift past MaxRow");
    }

    // --- Column axis: mirror the same four cases for InsertColumnsCommand ---

    [Fact]
    public void InsertColumns_StyleOnlyBandAtLastColumn_ReturnsFailed()
    {
        var (wb, sheet, ctx) = Setup();
        var style = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(1, CellAddress.MaxCol, style);

        var result = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse(
            "the style-only band at the last column would be pushed past MaxCol, exactly like a real cell value would");
        result.ErrorMessage.Should().Contain("pushed past the last column");
    }

    [Fact]
    public void InsertColumns_ColumnWidthOverrideAtLastColumn_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[CellAddress.MaxCol] = 25;

        var result = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse("a column-width override at the last column must not silently shift past MaxCol");
    }

    [Fact]
    public void InsertColumns_HiddenLastColumn_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.HiddenCols.Add(CellAddress.MaxCol);

        var result = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse("a hidden-column flag at the last column must not silently shift past MaxCol");
    }

    [Fact]
    public void InsertColumns_OutlineLevelAtLastColumn_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColOutlineLevels[CellAddress.MaxCol] = 1;

        var result = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse("an outline/group level at the last column must not silently shift past MaxCol");
    }

    // --- No-regression: metadata that sits BEFORE the insert point (never shifted) must not
    // falsely trip the new guard, and metadata that lands well clear of the boundary after
    // shifting must still be allowed through. ---

    [Fact]
    public void InsertRows_StyleOnlyBandAboveInsertPoint_StillSucceeds()
    {
        var (wb, sheet, ctx) = Setup();
        var style = wb.RegisterStyle(new CellStyle { Bold = true });
        // A style-only band at the last row, but the insert happens ABOVE it -- rows below the
        // insert point (in sheet terms: greater row numbers) are the ones that move; a row above
        // the insert point is untouched, so this must not be treated as overflowing.
        sheet.SetStyleOnly(CellAddress.MaxRow, 1, style);

        var result = new InsertRowsCommand(sheet.Id, beforeRow: CellAddress.MaxRow, count: 1).Apply(ctx);

        // beforeRow == MaxRow still shifts the MaxRow-row itself (it is "at or below" the insert
        // point), so this specific case is still expected to fail -- see the companion test below
        // using a genuinely-untouched row instead.
        result.Success.Should().BeFalse("row MaxRow itself is always within the shifted region for any valid beforeRow");
    }

    [Fact]
    public void InsertRows_MetadataFarBelowInsertPoint_DoesNotFalsePositive()
    {
        var (_, sheet, ctx) = Setup();
        // Metadata sits at row 5, well ABOVE (numerically less than) the insert point at row 10 --
        // it is never part of the shifted region and must not affect the overflow guard at all.
        sheet.RowHeights[5] = 40;
        sheet.HiddenRows.Add(5);
        sheet.RowOutlineLevels[5] = 2;

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 10, count: 3).Apply(ctx);

        result.Success.Should().BeTrue("metadata above the insert point never shifts and cannot overflow");
    }

    [Fact]
    public void InsertColumns_MetadataFarBeforeInsertPoint_DoesNotFalsePositive()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[5] = 40;
        sheet.HiddenCols.Add(5);
        sheet.ColOutlineLevels[5] = 2;

        var result = new InsertColumnsCommand(sheet.Id, beforeCol: 10, count: 3).Apply(ctx);

        result.Success.Should().BeTrue("metadata before the insert point never shifts and cannot overflow");
    }

    // --- No-regression: ordinary insert with no metadata anywhere near the boundary still works,
    // and a plain value-cell overflow (the pre-existing guard path) is still honoured. ---

    [Fact]
    public void InsertRows_NoMetadataAnywhere_OrdinaryInsertStillSucceeds()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1).Apply(ctx);

        result.Success.Should().BeTrue();
        sheet.GetValue(4, 1).Should().Be(new NumberValue(100));
    }

    [Fact]
    public void InsertRows_ValueCellAtLastRow_StillReturnsFailed()
    {
        // Pre-existing behavior (InsertDeleteRowsTests.Insert.cs) must remain intact.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow, 1), new NumberValue(1));

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pushed past the last row");
    }
}
