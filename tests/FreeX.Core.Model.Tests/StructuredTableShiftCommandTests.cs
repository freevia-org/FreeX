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

    // ---- Filtered tables (expected values recorded from real Excel via COM on the same layout) ----

    // Filters Price (table column 1) to {2, 4}: data row 3 (Price 3) becomes filter-hidden.
    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx, StructuredTableModel Table) BuildFiltered()
    {
        var built = Build();
        Ok(new FilterCommand(built.Sheet.Id, built.Table.Range, 1, ["2", "4"]), built.Ctx);
        built.Sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
        return (built.Wb, built.Sheet, built.Ctx, built.Sheet.StructuredTables.Single());
    }

    [Fact]
    public void InsertTableRows_InMiddleOfFilteredTable_IsRefusedLikeExcel()
    {
        var (_, sheet, ctx, table) = BuildFiltered();

        // Excel: "Can't move cells in a filtered range or table." -- even when nothing is hidden.
        var outcome = new InsertTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("filtered");
        sheet.StructuredTables.Single().Range.End.Row.Should().Be(4);
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
    }

    [Fact]
    public void DeleteTableRows_OfFilteredTable_IsRefusedLikeExcel()
    {
        var (_, sheet, ctx, table) = BuildFiltered();

        new DeleteTableRowsCommand(sheet.Id, table.Id, 0).Apply(ctx).Success.Should().BeFalse();
        new DeleteTableRowsCommand(sheet.Id, table.Id, 2).Apply(ctx).Success.Should().BeFalse();
        sheet.StructuredTables.Single().Range.End.Row.Should().Be(4);
    }

    [Fact]
    public void InsertTableRows_AppendedToFilteredTable_IsAllowedAndKeepsHiddenRowsAndUndoes()
    {
        var (_, sheet, ctx, table) = BuildFiltered();
        var command = new InsertTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 3);

        Ok(command, ctx);

        sheet.StructuredTables.Single().Range.End.Row.Should().Be(5);
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
        sheet.IsRowEffectivelyHidden(5).Should().BeFalse();

        command.Revert(ctx);
        sheet.StructuredTables.Single().Range.End.Row.Should().Be(4);
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
    }

    [Fact]
    public void InsertAndDeleteTableRows_DoNotShiftManualHiddenRowsOrRowHeights()
    {
        var (_, sheet, ctx, table) = Build();
        sheet.HiddenRows.Add(3);
        sheet.RowHeights[4] = 40;

        var insert = new InsertTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 1);
        Ok(insert, ctx);
        sheet.HiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
        sheet.RowHeights.Should().ContainSingle().Which.Should().Be(new KeyValuePair<uint, double>(4, 40));

        Ok(new DeleteTableRowsCommand(sheet.Id, table.Id, 0), ctx);
        sheet.HiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
        sheet.RowHeights[4].Should().Be(40);
    }

    [Fact]
    public void InsertTableColumns_InFilteredTable_ReindexesFilterKeepsHiddenRowsAndUndoes()
    {
        var (_, sheet, ctx, table) = BuildFiltered();
        var command = new InsertTableColumnsCommand(sheet.Id, table.Id, columnIndex: 0);

        Ok(command, ctx);

        var grown = sheet.StructuredTables.Single();
        grown.FilterColumns.Should().ContainSingle().Which.ColumnId.Should().Be(2);
        sheet.ActiveValueFilterColumns.Keys.Should().BeEquivalentTo(new uint[] { 3 });
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3 });

        command.Revert(ctx);
        sheet.StructuredTables.Single().FilterColumns.Single().ColumnId.Should().Be(1);
        sheet.ActiveValueFilterColumns.Keys.Should().BeEquivalentTo(new uint[] { 2 });
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
    }

    [Fact]
    public void DeleteTableColumns_UnfilteredColumn_KeepsFilterOnSurvivorAndSameHiddenRows()
    {
        var (_, sheet, ctx, table) = BuildFiltered();

        Ok(new DeleteTableColumnsCommand(sheet.Id, table.Id, columnIndex: 0), ctx);

        sheet.StructuredTables.Single().FilterColumns.Single().ColumnId.Should().Be(0);
        sheet.ActiveValueFilterColumns.Keys.Should().BeEquivalentTo(new uint[] { 1 });
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
    }

    [Fact]
    public void DeleteTableColumns_FilteredColumn_DropsCriterionUnhidesRowsAndUndoes()
    {
        var (_, sheet, ctx, table) = BuildFiltered();
        var command = new DeleteTableColumnsCommand(sheet.Id, table.Id, columnIndex: 1);

        Ok(command, ctx);

        // Excel: FilterMode becomes false and every row that only that column hid is visible again.
        sheet.StructuredTables.Single().FilterColumns.Should().BeEmpty();
        sheet.ActiveValueFilterColumns.Should().BeEmpty();
        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.ValueFilterHiddenRows.Should().BeEmpty();

        command.Revert(ctx);
        sheet.StructuredTables.Single().FilterColumns.Single().ColumnId.Should().Be(1);
        sheet.ActiveValueFilterColumns.Keys.Should().BeEquivalentTo(new uint[] { 2 });
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3 });
    }

    [Fact]
    public void DeleteTableColumns_FilteredColumnWithSecondFilter_ReappliesSurvivingFilter()
    {
        var (_, sheet, ctx, table) = Build();
        // Qty filter {20, 30} hides row 4; Price filter {2, 4} hides row 3. Deleting Price leaves row 4 hidden.
        Ok(new FilterCommand(sheet.Id, table.Range, 0, ["20", "30"]), ctx);
        Ok(new FilterCommand(sheet.Id, sheet.StructuredTables.Single().Range, 1, ["2", "4"]), ctx);
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3, 4 });

        Ok(new DeleteTableColumnsCommand(sheet.Id, table.Id, columnIndex: 1), ctx);

        sheet.StructuredTables.Single().FilterColumns.Single().ColumnId.Should().Be(0);
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 4 });
    }

    [Fact]
    public void TableShift_SortStateFollowsKeyColumnAndRowsAndDropsWithDeletedKeyColumn()
    {
        var (_, sheet, ctx, table) = Build();
        var state = table.CaptureCopyState() with
        {
            NativeSortStateXml = "<sortState xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ref=\"A2:C4\">" +
                                 "<sortCondition ref=\"C2:C4\"/></sortState>"
        };
        sheet.StructuredTables[0] = StructuredTableModel.FromCopyState(state);

        var addColumn = new InsertTableColumnsCommand(sheet.Id, table.Id, 0);
        Ok(addColumn, ctx);
        var afterColumn = sheet.StructuredTables.Single().NativeSortStateXml!;
        afterColumn.Should().Contain("ref=\"B2:D4\"").And.Contain("ref=\"D2:D4\"");

        Ok(new InsertTableRowsCommand(sheet.Id, table.Id, 1), ctx);
        sheet.StructuredTables.Single().NativeSortStateXml!.Should().Contain("ref=\"B2:D5\"").And.Contain("ref=\"D2:D5\"");

        // Appending at the end does not extend the key range (Excel left C2:C6 untouched).
        Ok(new InsertTableRowsCommand(sheet.Id, table.Id, 4), ctx);
        sheet.StructuredTables.Single().NativeSortStateXml!.Should().Contain("ref=\"D2:D5\"");

        // Deleting the key column removes the only sort condition, so the whole state goes (SortFields.Count = 0).
        Ok(new DeleteTableColumnsCommand(sheet.Id, table.Id, 3), ctx);
        sheet.StructuredTables.Single().NativeSortStateXml.Should().BeNull();
    }

    [Fact]
    public void InsertAndDeleteTableRows_WithTotalsRow_ShiftsTotalsRowAndRowsBelowLikeExcel()
    {
        var (_, sheet, ctx, table) = Build(totals: true);
        var totalsRow = sheet.StructuredTables.Single().Range.End.Row;
        uint BelowRow() => Enumerable.Range(1, 15).Select(r => (uint)r).Single(r => Text(sheet, r, 1) == "below");
        var below = BelowRow();

        // Excel: the totals row and everything below it in the table's columns move down together.
        Ok(new InsertTableRowsCommand(sheet.Id, table.Id, dataRowIndex: 1), ctx);
        sheet.StructuredTables.Single().Range.End.Row.Should().Be(totalsRow + 1);
        sheet.StructuredTables.Single().TotalsRowShown.Should().BeTrue();
        BelowRow().Should().Be(below + 1);

        Ok(new DeleteTableRowsCommand(sheet.Id, table.Id, 0), ctx);
        sheet.StructuredTables.Single().Range.End.Row.Should().Be(totalsRow);
        sheet.StructuredTables.Single().TotalsRowShown.Should().BeTrue();
        BelowRow().Should().Be(below);
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
