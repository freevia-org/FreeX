using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R110-Core.Formula-1: mirrors R27's DeleteSheetOp fix (see
/// R27_RemoveSheetStructuredTableFormulaRefTests) for the sibling case where a row or column
/// DELETE -- not a whole-sheet delete -- fully consumes a structured table's range.
/// RowColumnShiftHelpers.ShiftStructuredTables already treats that as "a THIRD way a table's name
/// gets freed workbook-wide" (see its own R107-round2 comment, and
/// R107_RowDeleteFullyConsumingTableOrphansUnestablishedPivotCacheTests for the pivot-cache side of
/// that fix) and drops the table from <c>sheet.StructuredTables</c>, but until this fix no formula
/// rewrite pass was ever told which table names the delete had just freed: DeleteRowsCommand.Apply
/// and DeleteColumnsCommand.Apply built every <see cref="DeleteRowsOp"/>/<see cref="DeleteColsOp"/>
/// with <c>DeletedTableNames</c> left at its default null, so <see cref="FormulaRewriter"/> fell
/// through its <c>op is not RenameTableOp</c> check and returned every Table[...] structured
/// reference completely unchanged. Left as stale text, <c>StructuredReferenceResolver.Resolve</c>
/// can no longer find the table on the next recalculation and the formula evaluates to #NAME?
/// instead of the #REF! Excel shows for this exact class of event (a table's identity vanishing
/// workbook-wide).
///
/// Fixed by computing the fully-removed table names (RowColumnShiftHelpers.
/// FindStructuredTablesRemovedByRowDelete/ByColumnDelete, over the sheet's live StructuredTables
/// *before* anything mutates it) up front in both commands' Apply, and threading that list through
/// every DeleteRowsOp/DeleteColsOp built for a FormulaRewriter pass (cell formulas, named formulas,
/// CF/DV rule formulas, chart verbatim formulas) -- mirroring how DeleteSheetOp already carries its
/// own DeletedTableNames. FormulaRewriter.RewriteStructuredReference/
/// RewriteStructuredCurrentRowReference now check DeleteRowsOp/DeleteColsOp for a matching name the
/// same way they already checked DeleteSheetOp, converting the reference to #REF!.
///
/// All formula cells below are deliberately placed OUTSIDE the row/column band being deleted (so
/// the delete's own shift never relocates the formula cell itself), isolating the assertions to
/// exactly the structured-reference rewrite under test.
///
/// All tests drive the real product entry points (DeleteRowsCommand/DeleteColumnsCommand) end to
/// end and read back the live Cell.FormulaText FormulaRewriter actually wrote, never asserting on a
/// hand-built model or calling FormulaRewriter directly.
/// </summary>
public sealed class R110_StructuredReferenceRowColumnDeleteFullyConsumingTableTests
{
    // Table lives at rows 5-7 (not the sheet's very first rows) so every formula cell below can sit
    // at row 1 -- strictly ABOVE the row band a row-delete test removes -- and therefore never gets
    // relocated by the delete's own row shift.
    private static (Workbook Workbook, Sheet Sheet) CreateWorkbookWithTable(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new NumberValue(20));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 1)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        return (workbook, sheet);
    }

    // --- bug case: a row delete that fully consumes the table's range (same sheet) ---

    [Fact]
    public void DeleteRowsCommand_FullyConsumingTableRows_RewritesSameSheetStructuredReference_ToRef_AndUndoRestores()
    {
        var (workbook, sheet) = CreateWorkbookWithTable("DeleteRowsFullyConsumesTableSameSheetTest");

        var formulaCell = new CellAddress(sheet.Id, 1, 5);
        sheet.SetFormula(formulaCell, "SUM(SalesTable[Amount])");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 3);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.StructuredTables.Should().NotContain(t => t.Name == "SalesTable");
        sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(#REF!)",
            because: "deleting all of SalesTable's rows fully consumes its range, freeing its name " +
                     "workbook-wide exactly like a host-sheet delete does, so the surviving " +
                     "structured reference must become #REF!, not silently dangle to #NAME?");

        command.Revert(ctx);

        sheet.StructuredTables.Should().Contain(t => t.Name == "SalesTable");
        sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(SalesTable[Amount])");
    }

    // --- bug case: a row delete that fully consumes the table's range, referenced cross-sheet ---

    [Fact]
    public void DeleteRowsCommand_FullyConsumingTableRows_RewritesCrossSheetStructuredReference_ToRef()
    {
        var (workbook, sheet) = CreateWorkbookWithTable("DeleteRowsFullyConsumesTableCrossSheetTest");
        var report = workbook.AddSheet("Report");

        var formulaCell = new CellAddress(report.Id, 1, 1);
        report.SetFormula(formulaCell, "SUM(SalesTable[Amount])");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 3);

        command.Apply(ctx).Success.Should().BeTrue();

        report.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(#REF!)",
            because: "SalesTable no longer exists anywhere in the workbook once the row delete " +
                     "fully consumes its range, so a cross-sheet structured reference to it must " +
                     "go stale the same way it would for a deleted host sheet");
    }

    // --- bug case: a column delete that fully consumes the table's range ---

    [Fact]
    public void DeleteColumnsCommand_FullyConsumingTableColumns_RewritesStructuredReference_ToRef_AndUndoRestores()
    {
        var workbook = new Workbook("DeleteColumnsFullyConsumesTableTest");
        var sheet = workbook.AddSheet("Data");

        // Table lives in column C (col 3), not the sheet's leftmost column, so the formula cell
        // below (column A) sits strictly to the LEFT of the deleted column band and is never
        // relocated by the delete's own column shift.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            // Single-column table (C1:C2) so deleting column C fully consumes it.
            Range = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 2, 3)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        var formulaCell = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(formulaCell, "SUM(SalesTable[Amount])");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.StructuredTables.Should().NotContain(t => t.Name == "SalesTable");
        sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(#REF!)",
            because: "deleting all of SalesTable's columns fully consumes its range, freeing its " +
                     "name workbook-wide, so the surviving structured reference must become #REF!");

        command.Revert(ctx);

        sheet.StructuredTables.Should().Contain(t => t.Name == "SalesTable");
        sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(SalesTable[Amount])");
    }

    // --- no-regression sibling: a PARTIAL row delete (table survives, shrunk) must leave the
    // structured reference completely untouched -- it still resolves fine by name ---

    [Fact]
    public void DeleteRowsCommand_PartialRowDelete_LeavesStructuredReferenceUntouched()
    {
        var (workbook, sheet) = CreateWorkbookWithTable("DeleteRowsPartialTableTest");

        var formulaCell = new CellAddress(sheet.Id, 1, 5);
        sheet.SetFormula(formulaCell, "SUM(SalesTable[Amount])");

        var ctx = new TestCommandContext(workbook);
        // Deletes only row 6 (one of the table's two data rows, rows 5-7) -- the table survives,
        // shrunk to rows 5-6, not removed.
        var command = new DeleteRowsCommand(sheet.Id, startRow: 6, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.StructuredTables.Should().Contain(t => t.Name == "SalesTable");
        sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(SalesTable[Amount])",
            because: "a row delete that only shrinks the table (does not fully consume its range) " +
                     "must leave a structured reference to it completely untouched -- the table " +
                     "still exists and the reference still resolves by name");
    }

    // --- no-regression sibling: deleting unrelated rows elsewhere on the sheet (table untouched)
    // must leave the structured reference to a surviving table alone ---

    [Fact]
    public void DeleteRowsCommand_UnrelatedRows_LeavesStructuredReferenceToSurvivingTableUntouched()
    {
        var (workbook, sheet) = CreateWorkbookWithTable("DeleteRowsUnrelatedRowsTest");

        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("Scratch"));
        var formulaCell = new CellAddress(sheet.Id, 1, 5);
        sheet.SetFormula(formulaCell, "SUM(SalesTable[Amount])");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteRowsCommand(sheet.Id, startRow: 20, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.StructuredTables.Should().Contain(t => t.Name == "SalesTable");
        sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(SalesTable[Amount])",
            because: "deleting rows entirely outside the table's range must not disturb a " +
                     "structured reference to it");
    }
}
