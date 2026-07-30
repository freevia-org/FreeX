using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R94: renaming a table column is done by editing its header cell (FreeX has no dedicated
// "rename column" command, exactly like real Excel). That ordinary cell edit updates only the
// sheet's header text -- nothing syncs it back into StructuredTableColumnModel.Name (see
// StructuredReferenceResolver.ColumnHeaderText for the matching gap on the resolve side, and
// ResizeStructuredTableCommand.BuildColumns which only fills in a name for a *blank* header).
// Before this fix, RefreshStructuredTableTotalsCommand baked the stale stored Name into the
// generated SUBTOTAL(...) structured reference, producing a formula that resolves to #NAME?
// the moment the header no longer matches the model.
public sealed class R94_StructuredTableTotalsHeaderRenameTests
{
    private static void SeedTotalsTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Orders"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), BlankValue.Instance);
    }

    private static StructuredTableModel BuildSalesTable(Sheet sheet) => new()
    {
        Id = 3,
        Name = "Sales",
        DisplayName = "Sales",
        Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
        TotalsRowShown = true,
        Columns =
        {
            new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
            new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
            new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "count")
        }
    };

    [Fact]
    public void RefreshStructuredTableTotalsCommand_AfterHeaderCellRename_UsesLiveHeaderTextNotStaleStoredName()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = BuildSalesTable(sheet);
        sheet.StructuredTables.Add(table);

        // Simulate the ONLY way a user can rename a table column in FreeX: typing new text into
        // the header cell. This is an ordinary cell edit -- it does NOT touch
        // StructuredTableColumnModel.Name (there is no dedicated rename command in the codebase).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));

        // The stored model name is now stale relative to what the user actually sees.
        table.Columns[1].Name.Should().Be("Sales");

        var ctx = new TestCommandContext(wb);
        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Before the fix this asserted "SUBTOTAL(109,[Sales])" -- the stale name -- which would
        // resolve to #NAME? in real Excel because the header cell no longer reads "Sales".
        sheet.GetCell(5, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Revenue])");
    }

    [Fact]
    public void RefreshStructuredTableTotalsCommand_WithoutHeaderRename_StillUsesColumnNameAsBefore()
    {
        // No-regression sibling: when the header cell text still matches the stored model name
        // (the common, non-renamed case), behavior is unchanged.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = BuildSalesTable(sheet);
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);
        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(5, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Sales])");
        sheet.GetCell(5, 3)!.FormulaText.Should().Be("SUBTOTAL(103,[Orders])");
    }

    [Fact]
    public void RefreshStructuredTableTotalsCommand_HeaderlessTable_FallsBackToStoredColumnName()
    {
        // Sibling: a table with HeaderRowCount == 0 has no header row cell to read at all, so the
        // stored model name remains authoritative there (mirrors
        // StructuredReferenceResolver.ColumnHeaderText's own headerless fallback).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(15));

        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Headerless",
            DisplayName = "Headerless",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 0,
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Column1", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);
        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");
    }
}
