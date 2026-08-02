using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for round-115 finding R115-formula-structuredref-coldelete-survivingtable-ref in
/// FormulaRewriter.cs:213/236:
///
/// RewriteStructuredReference / RewriteStructuredCurrentRowReference only ever rewrote a
/// Table[Column] structured reference when the WHOLE table was consumed by a delete
/// (MatchesDeletedTable -> #REF!) or the table was renamed. A DeleteColsOp that only shrinks a
/// table -- deleting one of its interior columns while the table itself survives -- fell straight
/// into the unconditional `return sr`/`return scr` for every DeleteColsOp, so a formula elsewhere in
/// the workbook naming the now-gone column (e.g. =SUM(Table1[Amount]) after 'Amount' is deleted)
/// kept its stale literal text forever. At evaluation time StructuredReferenceResolver can no longer
/// find the column, and the caller maps that to ErrorValue.Name -- #NAME? -- instead of the #REF!
/// real Excel shows the instant the column disappears (mirroring how Excel rewrites =A1 to =#REF!
/// when the cell A1 lived in is deleted).
///
/// Fixed by giving DeleteColsOp an optional DeletedColumnNamesByTable map (populated by
/// DeleteColumnsCommand.Apply via RowColumnShiftHelpers.FindStructuredTableColumnsRemovedByColumnDelete
/// with the names of columns removed from each table that SURVIVES the delete) and having both
/// structured-reference rewrite paths turn a matching Table[Column] / Table[@Column] reference into
/// #REF!, the same way MatchesDeletedTable already does for a fully-consumed table.
/// </summary>
public class R115_FormulaRewriterStructuredReferenceColumnDeleteTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Table1LostAmount =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Table1"] = new[] { "Amount" }
        };

    [Fact]
    public void DeleteCols_StructuredReference_ToColumnRemovedFromSurvivingTable_BecomesRef()
    {
        // Bug case: Table1 survives the delete (still has ID) but lost its 'Amount' column -- a
        // formula elsewhere naming Table1[Amount] must become #REF!, not be left as dangling text
        // that resolves to #NAME? at recalc.
        var op = new DeleteColsOp("Sheet1", 2, 1, DeletedColumnNamesByTable: Table1LostAmount);
        FormulaRewriter.Rewrite("SUM(Table1[Amount])", op, "Sheet2")
            .Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteCols_StructuredCurrentRowReference_ToColumnRemovedFromSurvivingTable_BecomesRef()
    {
        // Same bug, current-row selector shape: Table1[@Amount].
        var op = new DeleteColsOp("Sheet1", 2, 1, DeletedColumnNamesByTable: Table1LostAmount);
        FormulaRewriter.Rewrite("Table1[@Amount]*2", op, "Sheet2")
            .Should().Be("#REF!*2");
    }

    [Fact]
    public void DeleteCols_StructuredCurrentRowReference_BracketedColumnName_BecomesRef()
    {
        // Table1[@[Sales Amount]] shorthand -- ColumnName carries the literal bracket wrap
        // ("[Sales Amount]"); the lookup must unwrap it the same way the resolver does.
        var deleted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Table1"] = new[] { "Sales Amount" }
        };
        var op = new DeleteColsOp("Sheet1", 2, 1, DeletedColumnNamesByTable: deleted);
        FormulaRewriter.Rewrite("Table1[@[Sales Amount]]", op, "Sheet2")
            .Should().Be("#REF!");
    }

    [Fact]
    public void DeleteCols_StructuredReference_ToSurvivingColumn_Unaffected()
    {
        // Sibling already-working case (no over-correction): Table1 lost 'Amount' but a reference to
        // a DIFFERENT, still-live column on the same table must not be touched.
        var op = new DeleteColsOp("Sheet1", 2, 1, DeletedColumnNamesByTable: Table1LostAmount);
        FormulaRewriter.Rewrite("SUM(Table1[ID])", op, "Sheet2")
            .Should().BeNull(); // no change
    }

    [Fact]
    public void DeleteCols_StructuredReference_ToUnrelatedTable_Unaffected()
    {
        // Sibling already-working case: the delete only shrank Table1 -- a reference to a different
        // table (not present in the map at all) must be left untouched.
        var op = new DeleteColsOp("Sheet1", 2, 1, DeletedColumnNamesByTable: Table1LostAmount);
        FormulaRewriter.Rewrite("SUM(Table2[Amount])", op, "Sheet2")
            .Should().BeNull();
    }

    [Fact]
    public void DeleteCols_StructuredReference_NoDeletedColumnNamesSupplied_Unaffected()
    {
        // Sibling already-working case: DeletedColumnNamesByTable is optional/defaulted, so callers
        // that haven't been updated to populate it (existing behavior, e.g. InsertColsOp-adjacent
        // paths or older call sites) must keep seeing structured references left completely
        // untouched, same as before this fix.
        var op = new DeleteColsOp("Sheet1", 2, 1);
        FormulaRewriter.Rewrite("SUM(Table1[Amount])", op, "Sheet2")
            .Should().BeNull();
    }

    [Fact]
    public void DeleteCols_StructuredReference_SectionSelector_Unaffected()
    {
        // Sibling already-working case: a special section selector ("#Data") is not a plain column
        // name and must never be misclassified as one, even when its table lost a same-named-ish
        // column -- the '#' guard in ReferencesDeletedColumn must reject it.
        var op = new DeleteColsOp("Sheet1", 2, 1, DeletedColumnNamesByTable: Table1LostAmount);
        FormulaRewriter.Rewrite("SUM(Table1[#Data])", op, "Sheet2")
            .Should().BeNull();
    }

    [Fact]
    public void DeleteCols_StructuredReference_ColumnRangeSelector_Unaffected()
    {
        // Sibling already-working case: a column-range selector ("[[Amount]:[Total]]") needs
        // resolver-level parsing to know which columns it names -- the ':' guard must reject it
        // rather than wrongly collapsing a still partially-valid selector to #REF!.
        var op = new DeleteColsOp("Sheet1", 2, 1, DeletedColumnNamesByTable: Table1LostAmount);
        FormulaRewriter.Rewrite("SUM(Table1[[Amount]:[Total]])", op, "Sheet2")
            .Should().BeNull();
    }

    [Fact]
    public void DeleteCols_WholeTableConsumed_StillTakesPriorityOverColumnMap()
    {
        // Sibling already-working case: when BOTH DeletedTableNames and DeletedColumnNamesByTable
        // are populated (a delete that fully consumes Table1's range), the existing MatchesDeletedTable
        // whole-table #REF! path must still fire first, unaffected by the new column-level check.
        var op = new DeleteColsOp(
            "Sheet1", 2, 1,
            DeletedTableNames: new[] { "Table1" },
            DeletedColumnNamesByTable: Table1LostAmount);
        FormulaRewriter.Rewrite("SUM(Table1[Amount])", op, "Sheet2")
            .Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void RenameTable_StructuredReference_StillRewritesTableName_UnaffectedByColumnCheck()
    {
        // Sibling already-working case: RenameTableOp's pre-existing structured-reference rewrite is
        // a completely different RewriteOperation type, so it's untouched by the new DeleteColsOp
        // column-name check (which only ever inspects DeleteColsOp).
        var op = new RenameTableOp("Table1", "Table2");
        FormulaRewriter.Rewrite("SUM(Table1[Amount])", op, "Sheet2")
            .Should().Be("SUM(Table2[Amount])");
    }
}
