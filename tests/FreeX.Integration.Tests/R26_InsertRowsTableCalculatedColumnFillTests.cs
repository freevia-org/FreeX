using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R26-table-structured-ref-deep-2: InsertRowsCommand grows a structured table's Range (via
/// RowColumnShiftHelpers.ShiftAddressBearingRowsUp -> ShiftStructuredTables) when the insert point
/// falls inside the table's body, but previously never auto-filled the calculated column's formula
/// into the newly-inserted row -- leaving it blank instead of matching Excel's real behavior of
/// always extending a calculated column into a row inserted inside the table.
/// </summary>
public sealed class R26_InsertRowsTableCalculatedColumnFillTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    // ShiftStructuredTables rebuilds sheet.StructuredTables from scratch on every insert/delete (it
    // clones each table into a brand-new StructuredTableModel instance), so any StructuredTableModel
    // reference captured before Apply/Revert goes stale -- always re-fetch the live table by Id.
    private static StructuredTableModel LiveTable(Sheet sheet, int tableId) =>
        sheet.StructuredTables.Single(t => t.Id == tableId);

    // Table1 spans A1:B4 (A1:B1 header; column B is a calculated column holding "A2*2" — anchored
    // to the table's first data row, row 2 — over data rows 2-4: A2=1/B2=2, A3=2/B3=4, A4=3/B4=6).
    private static void BuildCalculatedColumnTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Double"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "A2*2");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 2), "A3*2");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(3));
        sheet.SetFormula(new CellAddress(sheet.Id, 4, 2), "A4*2");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Category"),
                new StructuredTableColumnModel(2, "Double", CalculatedColumnFormula: "A2*2")
            }
        };
        sheet.StructuredTables.Add(table);
    }

    // Bug case: inserting a row inside the table's body (before row 3, i.e. between the two existing
    // data rows) must auto-fill the calculated column into the new row, matching Excel, instead of
    // leaving it blank.
    [Fact]
    public void InsertRowInsideTableBody_AutoFillsCalculatedColumnIntoNewRow()
    {
        var (_, sheet, ctx) = Setup();
        BuildCalculatedColumnTable(sheet);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // The table grows to keep the header fixed and extend the data body by one row.
        LiveTable(sheet, 1).Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)));

        // The fix under test: the newly-inserted row 3 must have the calculated column's formula
        // auto-filled, row-shifted from the anchor (row 2) to row 3.
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.FormulaText.Should().Be("A3*2",
            "Excel always extends a table's calculated column into a row inserted inside the table");

        // The new row's non-calculated column must stay blank -- only the calculated column is
        // auto-filled, exactly like Excel.
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1)).Should().BeNull();

        // Sibling case (regression guard): the physically-relocated rows below the insert point must
        // still have their own formulas correctly row-shifted by the ordinary insert-rewrite path --
        // unrelated to (and not disturbed by) the new fill logic.
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.Value.Should().Be(new NumberValue(1));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be("A2*2");
        sheet.GetCell(new CellAddress(sheet.Id, 4, 1))!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(new CellAddress(sheet.Id, 4, 2))!.FormulaText.Should().Be("A4*2");
        sheet.GetCell(new CellAddress(sheet.Id, 5, 1))!.Value.Should().Be(new NumberValue(3));
        sheet.GetCell(new CellAddress(sheet.Id, 5, 2))!.FormulaText.Should().Be("A5*2");

        // The newly-filled cell must be surfaced as an affected cell so the recalc pipeline picks it
        // up (mirrors how ResizeStructuredTableCommand reports its own grown cells).
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 3, 2));

        // Undo must remove the auto-filled cell and restore the table/sheet to their pre-insert state.
        command.Revert(ctx);
        LiveTable(sheet, 1).Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.FormulaText.Should().Be("A3*2");
        sheet.GetCell(new CellAddress(sheet.Id, 4, 1))!.Value.Should().Be(new NumberValue(3));
        sheet.GetCell(new CellAddress(sheet.Id, 4, 2))!.FormulaText.Should().Be("A4*2");
        sheet.GetCell(new CellAddress(sheet.Id, 5, 1)).Should().BeNull();
    }

    // Sibling/already-working case: inserting a row ABOVE the table entirely (the table shifts down
    // as a whole rather than growing) must NOT trigger any calculated-column fill -- there is no new
    // row inside the table's body, just the whole table relocating.
    [Fact]
    public void InsertRowAboveTable_ShiftsTableWithoutFillingAnyRow()
    {
        var (_, sheet, ctx) = Setup();
        BuildCalculatedColumnTable(sheet);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // The whole table shifts down by one row; it does not grow.
        LiveTable(sheet, 1).Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 2)));

        // No row was inserted into the table's body, so nothing should have been auto-filled beyond
        // the table's original three data rows (now relocated to rows 3-5).
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 1, 2)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.FormulaText.Should().Be("A3*2");
        sheet.GetCell(new CellAddress(sheet.Id, 4, 2))!.FormulaText.Should().Be("A4*2");
        sheet.GetCell(new CellAddress(sheet.Id, 5, 2))!.FormulaText.Should().Be("A5*2");

        command.Revert(ctx);
        LiveTable(sheet, 1).Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)));
    }

    // Sibling/already-working case: inserting a row entirely below the table must leave the table's
    // range and calculated column completely untouched.
    [Fact]
    public void InsertRowBelowTable_LeavesTableAndCalculatedColumnUntouched()
    {
        var (_, sheet, ctx) = Setup();
        BuildCalculatedColumnTable(sheet);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 10, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        LiveTable(sheet, 1).Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be("A2*2");
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.FormulaText.Should().Be("A3*2");
        sheet.GetCell(new CellAddress(sheet.Id, 4, 2))!.FormulaText.Should().Be("A4*2");
    }

    // Multi-row insert inside the body: every newly-inserted row must get its own row-shifted
    // formula, not just the first one.
    [Fact]
    public void InsertMultipleRowsInsideTableBody_FillsEveryNewRow()
    {
        var (_, sheet, ctx) = Setup();
        BuildCalculatedColumnTable(sheet);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        LiveTable(sheet, 1).Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2)));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.FormulaText.Should().Be("A3*2");
        sheet.GetCell(new CellAddress(sheet.Id, 4, 2))!.FormulaText.Should().Be("A4*2");
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 4, 1)).Should().BeNull();
        // The originally-second data row (old row 3) is now at row 5, old row 4 is now at row 6.
        sheet.GetCell(new CellAddress(sheet.Id, 5, 2))!.FormulaText.Should().Be("A5*2");
        sheet.GetCell(new CellAddress(sheet.Id, 6, 2))!.FormulaText.Should().Be("A6*2");
    }
}
