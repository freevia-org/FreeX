using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for review3 group J-tables findings: H17 (rename must rewrite structured
/// references), H50 (totals-row aggregates must skip hidden/filtered rows), H51 (Convert to Range
/// must clear the table's filter state), and H60 (resize must fill calculated columns into new rows).
/// </summary>
public sealed class StructuredTableCommandTestsJTablesReview3
{
    private static StructuredTableModel CreateSalesTable(Sheet sheet, uint endRow = 5) =>
        new()
        {
            Id = 7,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, endRow, 2)),
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount")
            }
        };

    private static void SeedTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(5));
    }

    // ── H17: rename rewrites structured references ─────────────────────────────

    [Fact]
    public void RenameStructuredTableCommand_RewritesStructuredReferencesAndUndoRestoresOriginal()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 4), "SUM(Table1[Amount])");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 5), "Table1[@Amount]*2");
        wb.NamedFormulas["TotalAmount"] = "SUM(Table1[Amount])";
        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "Sales");

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(1, 4)!.FormulaText.Should().Be("SUM(Sales[Amount])");
        sheet.GetCell(1, 5)!.FormulaText.Should().Be("Sales[@Amount]*2");
        wb.NamedFormulas["TotalAmount"].Should().Be("SUM(Sales[Amount])");

        command.Revert(ctx);

        sheet.GetCell(1, 4)!.FormulaText.Should().Be("SUM(Table1[Amount])");
        sheet.GetCell(1, 5)!.FormulaText.Should().Be("Table1[@Amount]*2");
        wb.NamedFormulas["TotalAmount"].Should().Be("SUM(Table1[Amount])");
    }

    [Fact]
    public void RenameStructuredTableCommand_LeavesUnrelatedTableReferencesUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        sheet.SetCell(new CellAddress(sheet.Id, 8, 1), new TextValue("Other"));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 4), "SUM(OtherTable[Amount])");
        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "Sales");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(1, 4)!.FormulaText.Should().Be("SUM(OtherTable[Amount])");
    }

    // ── H50: totals row skips effectively-hidden rows ───────────────────────────

    [Fact]
    public void RefreshStructuredTableTotalsCommand_ExcludesFilterHiddenRowsFromSumAverageCountMinMax()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);
        // Row 3 (South, 20) is filtered out — Excel's SUBTOTAL(109,...) must skip it.
        sheet.FilterHiddenRows.Add(3);
        var ctx = new TestCommandContext(wb);
        var command = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Unfiltered total would be 10+20+15+5=50; excluding filtered row 3 (20) gives 30.
        sheet.GetValue(6, 2).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void RefreshStructuredTableTotalsCommand_ExcludesFilterHiddenRowsFromCount()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowFunction: "count"),
                new StructuredTableColumnModel(2, "Amount")
            }
        };
        sheet.StructuredTables.Add(table);
        sheet.FilterHiddenRows.Add(3);
        sheet.HiddenRows.Add(5);
        var ctx = new TestCommandContext(wb);
        var command = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        // Rows 2 and 4 remain visible (row 3 filter-hidden, row 5 manually hidden) → count = 2.
        sheet.GetValue(6, 1).Should().Be(new NumberValue(2));
    }

    // ── H51: Convert to Range clears the table's filter-hidden state ───────────

    [Fact]
    public void ConvertStructuredTableToRangeCommand_ClearsFilterHiddenRowsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);

        // Simulate a structured-table column filter having hidden rows 3 and 5.
        sheet.FilterHiddenRows.Add(3);
        sheet.FilterHiddenRows.Add(5);
        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.FilterHiddenRows.Should().NotContain(3);
        sheet.FilterHiddenRows.Should().NotContain(5);

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        sheet.FilterHiddenRows.Should().Contain(3);
        sheet.FilterHiddenRows.Should().Contain(5);
    }

    [Fact]
    public void ConvertStructuredTableToRangeCommand_LeavesFilterHiddenRowsOutsideTableRangeUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        // Row 9 is hidden by something unrelated to this table (outside its range).
        sheet.FilterHiddenRows.Add(9);
        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(9);
    }

    [Fact]
    public void ConvertStructuredTableToRangeCommand_ClearsActiveValueFilterColumnsForTableColumnsOnly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        sheet.ActiveValueFilterColumns[1] = ["North"];
        sheet.ActiveValueFilterColumns[9] = ["Unrelated"];
        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ActiveValueFilterColumns.Should().NotContainKey(1);
        sheet.ActiveValueFilterColumns.Should().ContainKey(9);

        command.Revert(ctx);

        sheet.ActiveValueFilterColumns[1].Should().Equal("North");
        sheet.ActiveValueFilterColumns[9].Should().Equal("Unrelated");
    }

    // ── H60: resize fills calculated-column formulas into new rows ─────────────

    [Fact]
    public void ResizeStructuredTableCommand_FillsCalculatedColumnFormulaIntoNewRowsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount", CalculatedColumnFormula: "Table1[@Region]&\"!\"")
            }
        };
        sheet.StructuredTables.Add(table);
        // Pre-existing content in the grown rows (below the old table range) so BuildColumns/header
        // logic has something to read, and so we can prove the calculated column overwrites it.
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("East"));
        var ctx = new TestCommandContext(wb);
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var newRowCell = sheet.GetCell(6, 2);
        newRowCell.Should().NotBeNull();
        newRowCell!.FormulaText.Should().Be("Table1[@Region]&\"!\"");
        // Existing data rows must not be touched by the fill.
        sheet.GetCell(2, 2)!.FormulaText.Should().BeNull();
        sheet.GetValue(2, 2).Should().Be(new NumberValue(10));

        command.Revert(ctx);

        sheet.GetCell(6, 2).Should().BeNull();
        sheet.GetValue(6, 1).Should().Be(new TextValue("East"));
    }

    [Fact]
    public void ResizeStructuredTableCommand_DoesNotFillWhenNoRowsAreAdded()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount", CalculatedColumnFormula: "Table1[@Region]&\"!\"")
            }
        };
        sheet.StructuredTables.Add(table);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Extra"));
        var ctx = new TestCommandContext(wb);
        // Widen by one column only — row count is unchanged, so nothing should be filled.
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(6, 2).Should().BeNull();
    }
}
