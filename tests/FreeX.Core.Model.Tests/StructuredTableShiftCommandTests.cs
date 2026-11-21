using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Excel parity for Insert &gt; Table Rows/Columns and Delete &gt; Table Rows/Columns: only the
/// table's own cells shift, the table grows or shrinks, and everything is undoable. Expected values
/// were taken from real Excel (ListRows/ListColumns Add/Delete) on the same layout.
/// </summary>
public sealed class StructuredTableShiftCommandTests
{
    // Table A1:C4 -- header Qty/Price/Total, three data rows, Total = Qty + Price (calculated).
    // Cells outside: A7/B7 below the table, D2/D3 beside it, formulas in G1:G3.
    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx, StructuredTableModel Table) Build(bool totals = false)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        void Text(uint r, uint c, string v) => sheet.SetCell(new CellAddress(sheet.Id, r, c), new TextValue(v));
        void Num(uint r, uint c, double v) => sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(v));
        void Formula(uint r, uint c, string f) => sheet.SetCell(new CellAddress(sheet.Id, r, c), Cell.FromFormula(f));

        Text(1, 1, "Qty"); Text(1, 2, "Price"); Text(1, 3, "Total");
        for (uint r = 2; r <= 4; r++)
        {
            Num(r, 1, r * 10);
            Num(r, 2, r);
            Formula(r, 3, $"A{r}+B{r}");
        }

        Text(7, 1, "below"); Text(7, 2, "belowB");
        Text(2, 4, "outsideD2"); Text(3, 4, "outsideD3");
        Formula(1, 7, "SUM(Table1[Price])");
        Formula(2, 7, "B3");
        Formula(3, 7, "SUM(B2:B4)");

        Ok(new CreateStructuredTableCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3))), ctx);
        var table = sheet.StructuredTables.Single();
        table.SetCalculatedColumnFormula(table.Columns[2].Id, "A2+B2");
        if (totals)
            Ok(new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, true), ctx);
        return (wb, sheet, ctx, sheet.StructuredTables.Single());
    }

    private static void Ok(IWorkbookCommand command, ICommandContext ctx)
    {
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    private static string Text(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(row, col)?.Value is TextValue t ? t.Value : "";

    [Fact]
    public void InsertTableRows_Middle_ShiftsOnlyTableColumnsGrowsTableFillsCalculatedColumnAndUndoes()
    {
        var (_, sheet, ctx, table) = Build();
        var command = new InsertTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 1);

        Ok(command, ctx);

        sheet.StructuredTables.Single().Range.End.Row.Should().Be(5);
        StructuredTableShiftSupportAssert.IsBlankRow(sheet, 3, 1, 1);
        sheet.GetCell(4, 1)!.Value.Should().Be(new NumberValue(30));
        sheet.GetCell(3, 3)!.FormulaText.Should().Be("A3+B3");
        sheet.GetCell(4, 3)!.FormulaText.Should().Be("A4+B4");
        sheet.GetCell(5, 3)!.FormulaText.Should().Be("A5+B5");
        // Below the table (in the table's columns) shifts down; beside it does not.
        Text(sheet, 8, 1).Should().Be("below");
        sheet.GetCell(7, 1).Should().BeNull();
        Text(sheet, 2, 4).Should().Be("outsideD2");
        Text(sheet, 3, 4).Should().Be("outsideD3");
        // References into the shifted band follow the cells; ranges straddling the insert grow.
        sheet.GetCell(2, 7)!.FormulaText.Should().Be("B4");
        sheet.GetCell(3, 7)!.FormulaText.Should().Be("SUM(B2:B5)");
        sheet.GetCell(1, 7)!.FormulaText.Should().Be("SUM(Table1[Price])");

        command.Revert(ctx);

        sheet.StructuredTables.Single().Range.End.Row.Should().Be(4);
        Text(sheet, 7, 1).Should().Be("below");
        sheet.GetCell(8, 1).Should().BeNull();
        sheet.GetCell(3, 1)!.Value.Should().Be(new NumberValue(30));
        sheet.GetCell(3, 3)!.FormulaText.Should().Be("A3+B3");
        sheet.GetCell(2, 7)!.FormulaText.Should().Be("B3");
        sheet.GetCell(3, 7)!.FormulaText.Should().Be("SUM(B2:B4)");
    }

    [Fact]
    public void InsertTableRows_MultipleAbove_InsertsCountRows()
    {
        var (_, sheet, ctx, table) = Build();

        Ok(new InsertTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 0, count: 2), ctx);

        sheet.StructuredTables.Single().Range.End.Row.Should().Be(6);
        StructuredTableShiftSupportAssert.IsBlankRow(sheet, 2, 1, 2);
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("A2+B2");
        sheet.GetCell(3, 3)!.FormulaText.Should().Be("A3+B3");
        sheet.GetCell(4, 1)!.Value.Should().Be(new NumberValue(20));
        Text(sheet, 9, 1).Should().Be("below");
    }

    [Fact]
    public void InsertTableRows_BelowLastRowWithTotals_InsertsBeforeTotalsRow()
    {
        var (_, sheet, ctx, table) = Build(totals: true);
        var totalsRow = table.Range.End.Row;

        Ok(new InsertTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 3), ctx);

        var grown = sheet.StructuredTables.Single();
        grown.TotalsRowShown.Should().BeTrue();
        grown.Range.End.Row.Should().Be(totalsRow + 1);
        StructuredTableShiftSupportAssert.IsBlankRow(sheet, totalsRow, 1, 2);
        sheet.GetCell(totalsRow, 3)!.FormulaText.Should().Be("A5+B5");
        sheet.GetCell(totalsRow + 1, 3).Should().NotBeNull("the totals row moves down intact");
    }

    [Fact]
    public void InsertTableRows_RejectsWhenAnotherTableSitsInTheShiftedBand()
    {
        var (_, sheet, ctx, table) = Build();
        sheet.SetCell(new CellAddress(sheet.Id, 9, 1), new TextValue("H1"));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 2), new TextValue("H2"));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 2), new NumberValue(2));
        Ok(new CreateStructuredTableCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 9, 1), new CellAddress(sheet.Id, 10, 2))), ctx);

        var outcome = new InsertTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.StructuredTables.First(t => t.Id == table.Id).Range.End.Row.Should().Be(4);
        Text(sheet, 7, 1).Should().Be("below");
    }

    [Fact]
    public void DeleteTableRows_Middle_ShiftsOnlyTableColumnsShrinksTableAndUndoes()
    {
        var (_, sheet, ctx, table) = Build();
        var command = new DeleteTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 1);

        Ok(command, ctx);

        sheet.StructuredTables.Single().Range.End.Row.Should().Be(3);
        sheet.GetCell(3, 1)!.Value.Should().Be(new NumberValue(40));
        sheet.GetCell(3, 3)!.FormulaText.Should().Be("A3+B3");
        Text(sheet, 6, 1).Should().Be("below");
        sheet.GetCell(7, 1).Should().BeNull();
        Text(sheet, 2, 4).Should().Be("outsideD2");
        Text(sheet, 3, 4).Should().Be("outsideD3");
        sheet.GetCell(2, 7)!.FormulaText.Should().Contain("#REF!");
        sheet.GetCell(3, 7)!.FormulaText.Should().Be("SUM(B2:B3)");

        command.Revert(ctx);

        sheet.StructuredTables.Single().Range.End.Row.Should().Be(4);
        sheet.GetCell(3, 1)!.Value.Should().Be(new NumberValue(30));
        Text(sheet, 7, 1).Should().Be("below");
        sheet.GetCell(2, 7)!.FormulaText.Should().Be("B3");
        sheet.GetCell(3, 7)!.FormulaText.Should().Be("SUM(B2:B4)");
    }

    [Fact]
    public void DeleteTableRows_AllDataRows_KeepsOneBlankDataRowLikeExcel()
    {
        var (_, sheet, ctx, table) = Build();
        var command = new DeleteTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 0, count: 3);

        Ok(command, ctx);

        sheet.StructuredTables.Single().Range.End.Row.Should().Be(2);
        StructuredTableShiftSupportAssert.IsBlankRow(sheet, 2, 1, 3);
        // Excel: rows below shift up by count - 1 (7 -> 5).
        Text(sheet, 5, 1).Should().Be("below");

        command.Revert(ctx);

        sheet.StructuredTables.Single().Range.End.Row.Should().Be(4);
        sheet.GetCell(2, 1)!.Value.Should().Be(new NumberValue(20));
        Text(sheet, 7, 1).Should().Be("below");
    }

    [Fact]
    public void DeleteTableRows_OnlyBlankDataRow_IsNoOp()
    {
        var (_, sheet, ctx, table) = Build();
        Ok(new DeleteTableRowsCommand(sheet.Id, table.Id, 0, 3), ctx);

        var outcome = new DeleteTableRowsCommand(sheet.Id, table.Id, 0).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void DeleteTableRows_RejectsRowsOutsideTable()
    {
        var (_, sheet, ctx, table) = Build();

        new DeleteTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 2, count: 2).Apply(ctx).Success.Should().BeFalse();
    }

    [Fact]
    public void InsertTableColumns_Left_ShiftsOnlyTableRowsNamesNewColumnAndUndoes()
    {
        var (_, sheet, ctx, table) = Build();
        var command = new InsertTableColumnsCommand(sheet.Id, table.Id, columnIndex: 1);

        Ok(command, ctx);

        var grown = sheet.StructuredTables.Single();
        grown.Range.End.Col.Should().Be(4);
        grown.Columns.Select(c => c.Name).Should().Equal("Qty", "Column1", "Price", "Total");
        Text(sheet, 1, 2).Should().Be("Column1");
        Text(sheet, 1, 3).Should().Be("Price");
        sheet.GetCell(2, 3)!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(2, 4)!.FormulaText.Should().Be("A2+C2");
        // Beside the table (same rows) shifts right; below the table does not move.
        Text(sheet, 2, 5).Should().Be("outsideD2");
        Text(sheet, 7, 2).Should().Be("belowB");
        sheet.GetCell(1, 8)!.FormulaText.Should().Be("SUM(Table1[Price])");
        grown.Columns[3].CalculatedColumnFormula.Should().Be("A2+C2");

        command.Revert(ctx);

        var restored = sheet.StructuredTables.Single();
        restored.Range.End.Col.Should().Be(3);
        restored.Columns.Select(c => c.Name).Should().Equal("Qty", "Price", "Total");
        Text(sheet, 1, 2).Should().Be("Price");
        Text(sheet, 2, 4).Should().Be("outsideD2");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("A2+B2");
    }

    [Fact]
    public void DeleteTableColumns_ShiftsOnlyTableRowsShrinksTableInvalidatesStructuredRefsAndUndoes()
    {
        var (_, sheet, ctx, table) = Build();
        var command = new DeleteTableColumnsCommand(sheet.Id, table.Id, columnIndex: 1);

        Ok(command, ctx);

        var shrunk = sheet.StructuredTables.Single();
        shrunk.Range.End.Col.Should().Be(2);
        shrunk.Columns.Select(c => c.Name).Should().Equal("Qty", "Total");
        Text(sheet, 1, 2).Should().Be("Total");
        sheet.GetCell(2, 2)!.FormulaText.Should().Contain("#REF!");
        Text(sheet, 2, 3).Should().Be("outsideD2");
        Text(sheet, 7, 2).Should().Be("belowB");
        sheet.GetCell(1, 6)!.FormulaText.Should().Contain("#REF!");

        command.Revert(ctx);

        var restored = sheet.StructuredTables.Single();
        restored.Range.End.Col.Should().Be(3);
        restored.Columns.Select(c => c.Name).Should().Equal("Qty", "Price", "Total");
        Text(sheet, 1, 2).Should().Be("Price");
        Text(sheet, 2, 4).Should().Be("outsideD2");
        sheet.GetCell(1, 7)!.FormulaText.Should().Be("SUM(Table1[Price])");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("A2+B2");
    }

    [Fact]
    public void DeleteTableColumns_RejectsDeletingEveryColumn()
    {
        var (_, sheet, ctx, table) = Build();

        new DeleteTableColumnsCommand(sheet.Id, table.Id, 0, 3).Apply(ctx).Success.Should().BeFalse();
        sheet.StructuredTables.Single().Range.End.Col.Should().Be(3);
    }

    [Fact]
    public void TableShiftCommands_ZeroCountIsNoOp()
    {
        var (_, sheet, ctx, table) = Build();

        new InsertTableRowsCommand(sheet.Id, table.Id, 0, 0).Apply(ctx).IsNoOp.Should().BeTrue();
        new DeleteTableRowsCommand(sheet.Id, table.Id, 0, 0).Apply(ctx).IsNoOp.Should().BeTrue();
        new InsertTableColumnsCommand(sheet.Id, table.Id, 0, 0).Apply(ctx).IsNoOp.Should().BeTrue();
        new DeleteTableColumnsCommand(sheet.Id, table.Id, 0, 0).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void TableShiftCommands_RejectUnknownTable()
    {
        var (_, sheet, ctx, _) = Build();

        new InsertTableRowsCommand(sheet.Id, 999, 0).Apply(ctx).Success.Should().BeFalse();
        new DeleteTableColumnsCommand(sheet.Id, 999, 0).Apply(ctx).Success.Should().BeFalse();
    }

    private static class StructuredTableShiftSupportAssert
    {
        internal static void IsBlankRow(Sheet sheet, uint row, uint firstCol, uint lastCol)
        {
            for (var col = firstCol; col <= lastCol; col++)
            {
                var cell = sheet.GetCell(row, col);
                (cell is null || (cell.FormulaText is null && cell.Value is BlankValue)).Should().BeTrue($"column {col} must be cleared");
            }
        }
    }
}
