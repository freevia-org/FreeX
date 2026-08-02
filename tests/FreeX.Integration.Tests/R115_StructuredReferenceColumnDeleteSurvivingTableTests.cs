using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R115-formula-structuredref-coldelete-survivingtable-ref: sibling of R110
/// (R110_StructuredReferenceRowColumnDeleteFullyConsumingTableTests) for the OTHER outcome of a
/// column delete -- the table SURVIVES (only one of its interior columns falls inside the deleted
/// band), rather than being fully consumed.
///
/// Before this fix, FormulaRewriter.RewriteStructuredReference/RewriteStructuredCurrentRowReference
/// only ever rewrote a Table[Column] structured reference when the whole table was consumed
/// (MatchesDeletedTable -> #REF!) or the table was renamed; a plain DeleteColsOp that merely shrinks
/// a surviving table fell straight into the unconditional "return sr" for every DeleteColsOp, so a
/// formula elsewhere in the workbook naming the now-deleted column (e.g. =SUM(SalesTable[Amount])
/// after 'Amount' is deleted) kept its stale literal text forever. At the next recalculation,
/// StructuredReferenceResolver can no longer find 'Amount' in the table's (now-shrunk) Columns list
/// and the formula evaluates to #NAME?, whereas real Excel rewrites the reference to
/// SalesTable[#REF!] (-> #REF!) the instant the column disappears.
///
/// Fixed by computing, over the sheet's live StructuredTables *before* anything mutates it, the
/// column names removed from each table that SURVIVES the delete
/// (RowColumnShiftHelpers.FindStructuredTableColumnsRemovedByColumnDelete) and threading that map
/// through every DeleteColsOp built for a FormulaRewriter pass as DeletedColumnNamesByTable.
///
/// All tests drive the real product entry point (DeleteColumnsCommand) end to end and read back the
/// live Cell.FormulaText FormulaRewriter actually wrote, never asserting on a hand-built model or
/// calling FormulaRewriter directly.
/// </summary>
public sealed class R115_StructuredReferenceColumnDeleteSurvivingTableTests
{
    // Table lives at columns C:D (cols 3-4), not the sheet's leftmost columns, so every formula cell
    // below can sit in column A -- strictly to the LEFT of the deleted column band -- and is never
    // relocated by the delete's own column shift. Two data columns: "ID" (col 3) and "Amount" (col
    // 4), so deleting column D removes only 'Amount' and the table survives, shrunk to just "ID".
    private static (Workbook Workbook, Sheet Sheet) CreateWorkbookWithTwoColumnTable(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("ID"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "ID"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet);
    }

    // --- bug case: a column delete that shrinks the table (survives), removing the referenced
    // column ---

    [Fact]
    public void DeleteColumnsCommand_ColumnDeletedFromSurvivingTable_RewritesStructuredReference_ToRef_AndUndoRestores()
    {
        var (workbook, sheet) = CreateWorkbookWithTwoColumnTable("DeleteColumnsSurvivingTableTest");

        var amountFormulaCell = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(amountFormulaCell, "SUM(SalesTable[Amount])");

        var ctx = new TestCommandContext(workbook);
        // Deletes only column D ('Amount') -- the table survives, shrunk to just column C ('ID').
        var command = new DeleteColumnsCommand(sheet.Id, startCol: 4, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.StructuredTables.Should().Contain(t => t.Name == "SalesTable",
            because: "deleting only one of the table's two columns shrinks it -- it does not fully " +
                     "consume the table's range, so the table itself survives");
        var survivingTable = sheet.StructuredTables.Single(t => t.Name == "SalesTable");
        survivingTable.Columns.Select(c => c.Name).Should().NotContain("Amount");

        sheet.GetCell(amountFormulaCell)!.FormulaText.Should().Be("SUM(#REF!)",
            because: "the 'Amount' column no longer exists anywhere on SalesTable once this delete " +
                     "removes it, so the surviving structured reference naming it must become #REF!, " +
                     "not silently dangle to resolve as #NAME? at the next recalculation");

        command.Revert(ctx);

        sheet.StructuredTables.Should().Contain(t => t.Name == "SalesTable");
        sheet.StructuredTables.Single(t => t.Name == "SalesTable")
            .Columns.Select(c => c.Name).Should().Contain("Amount");
        sheet.GetCell(amountFormulaCell)!.FormulaText.Should().Be("SUM(SalesTable[Amount])");
    }

    [Fact]
    public void DeleteColumnsCommand_ColumnDeletedFromSurvivingTable_CurrentRowSelector_RewritesToRef()
    {
        var (workbook, sheet) = CreateWorkbookWithTwoColumnTable("DeleteColumnsSurvivingTableCurrentRowTest");

        // Current-row selector shape, referenced cross-sheet (no host-table context of its own).
        var report = workbook.AddSheet("Report");
        var formulaCell = new CellAddress(report.Id, 1, 1);
        report.SetFormula(formulaCell, "SalesTable[@Amount]*2");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteColumnsCommand(sheet.Id, startCol: 4, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        report.GetCell(formulaCell)!.FormulaText.Should().Be("#REF!*2",
            because: "Table[@Column] current-row structured references must be converted to #REF! " +
                     "the same way plain Table[Column] references are when their named column is " +
                     "deleted from a surviving table");
    }

    // --- no-regression sibling: a reference to the SURVIVING column on the same table must be left
    // completely untouched ---

    [Fact]
    public void DeleteColumnsCommand_ColumnDeletedFromSurvivingTable_LeavesReferenceToSurvivingColumnUntouched()
    {
        var (workbook, sheet) = CreateWorkbookWithTwoColumnTable("DeleteColumnsSurvivingColumnUntouchedTest");

        var idFormulaCell = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(idFormulaCell, "SUM(SalesTable[ID])");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteColumnsCommand(sheet.Id, startCol: 4, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(idFormulaCell)!.FormulaText.Should().Be("SUM(SalesTable[ID])",
            because: "a structured reference to a column that SURVIVES the delete must be left " +
                     "completely untouched -- it still exists and still resolves by name");
    }

    // --- no-regression sibling: deleting a column entirely outside the table's range must leave
    // every structured reference to the (untouched) table alone ---

    [Fact]
    public void DeleteColumnsCommand_UnrelatedColumn_LeavesStructuredReferenceToSurvivingTableUntouched()
    {
        var (workbook, sheet) = CreateWorkbookWithTwoColumnTable("DeleteColumnsUnrelatedColumnTest");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 20), new TextValue("Scratch"));
        var formulaCell = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(formulaCell, "SUM(SalesTable[Amount])");

        var ctx = new TestCommandContext(workbook);
        var command = new DeleteColumnsCommand(sheet.Id, startCol: 20, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.StructuredTables.Single(t => t.Name == "SalesTable")
            .Columns.Select(c => c.Name).Should().Contain("Amount");
        sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(SalesTable[Amount])",
            because: "deleting a column entirely outside the table's range must not disturb a " +
                     "structured reference to it");
    }
}
