using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for round-26 finding R26-sheet-lifecycle-deep-3 in FormulaRewriter.cs:
///
/// RewriteStructuredReference / RewriteStructuredCurrentRowReference (FormulaRewriter.cs) only ever
/// matched RenameTableOp -- a DeleteSheetOp fell straight through unchanged, so a cross-sheet
/// Table[...] formula referencing a table that lived on the deleted sheet kept its original text.
/// Since the table no longer exists anywhere in the workbook once its host sheet is gone,
/// StructuredReferenceResolver can't find it at recalc and falls through to #NAME?, whereas real
/// Excel (and FreeX's own ordinary cell/range-ref handling for DeleteSheetOp, see
/// RewriteSheetQualifiedRefDeleteSheet) shows #REF! for a reference whose target was structurally
/// removed.
///
/// Fixed by giving DeleteSheetOp an optional DeletedTableNames list (populated by the sheet-delete
/// command with the names of tables that lived on the deleted sheet) and having both structured-
/// reference rewrite paths turn a matching Table[...] / Table[@Column] reference into #REF!, the same
/// way the plain cell/range-ref path already does for ordinary sheet-qualified references.
/// </summary>
public class R26_FormulaRewriterStructuredReferenceDeleteSheetTests
{
    [Fact]
    public void DeleteSheet_StructuredReference_ToDeletedTable_BecomesRef()
    {
        // Bug case: TABLE1 lived on the deleted sheet ("Sheet1"); a formula on another sheet
        // referencing TABLE1[Amount] must become #REF!, not be left as dangling text that resolves
        // to #NAME? at recalc.
        var op = new DeleteSheetOp("Sheet1", new[] { "TABLE1" });
        FormulaRewriter.Rewrite("SUM(TABLE1[Amount])", op, "Sheet2")
            .Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteSheet_StructuredCurrentRowReference_ToDeletedTable_BecomesRef()
    {
        // Same bug, current-row selector shape: TABLE1[@Amount].
        var op = new DeleteSheetOp("Sheet1", new[] { "TABLE1" });
        FormulaRewriter.Rewrite("TABLE1[@Amount]*2", op, "Sheet2")
            .Should().Be("#REF!*2");
    }

    [Fact]
    public void DeleteSheet_StructuredReference_AndCrossSheetCellRef_BothBecomeRef()
    {
        // Combined shape: an ordinary cross-sheet cell ref and a structured ref to a table that
        // lived on the deleted sheet must both be rewritten by the same DeleteSheetOp pass, proving
        // the new structured-reference handling doesn't disturb the pre-existing cell-ref handling.
        var op = new DeleteSheetOp("Sheet1", new[] { "TABLE1" });
        FormulaRewriter.Rewrite("Sheet1!A1+TABLE1[Amount]", op, "Sheet2")
            .Should().Be("#REF!+#REF!");
    }

    [Fact]
    public void DeleteSheet_StructuredReference_ToUnrelatedTable_Unaffected()
    {
        // Sibling already-working case (no over-correction): deleting Sheet1 (whose only table was
        // TABLE1) must not touch a structured reference to a table that did NOT live on Sheet1 --
        // TABLE2 still exists elsewhere in the workbook.
        var op = new DeleteSheetOp("Sheet1", new[] { "TABLE1" });
        FormulaRewriter.Rewrite("SUM(TABLE2[Amount])", op, "Sheet2")
            .Should().BeNull(); // no change
    }

    [Fact]
    public void DeleteSheet_StructuredReference_NoDeletedTableNamesSupplied_Unaffected()
    {
        // Sibling already-working case: DeletedTableNames is optional/defaulted, so callers that
        // haven't been updated to populate it (existing behavior) must keep seeing structured
        // references left completely untouched, same as before this fix.
        var op = new DeleteSheetOp("Sheet1");
        FormulaRewriter.Rewrite("SUM(TABLE1[Amount])", op, "Sheet2")
            .Should().BeNull();
    }

    [Fact]
    public void DeleteSheet_UnqualifiedBareColumnReference_Unaffected()
    {
        // Sibling already-working case: a bare [Column] reference (no table name -- resolves
        // against whichever table the host cell belongs to) must never be treated as a reference to
        // a deleted table, regardless of which sheet/table was deleted.
        var op = new DeleteSheetOp("Sheet1", new[] { "TABLE1" });
        FormulaRewriter.Rewrite("SUBTOTAL(109,[Amount])", op, "Sheet1")
            .Should().BeNull();
    }

    [Fact]
    public void RenameTable_StructuredReference_StillRewritesTableName()
    {
        // Sibling already-working case: RenameTableOp's pre-existing structured-reference rewrite
        // must be completely unaffected by adding the DeleteSheetOp branch to the same method.
        var op = new RenameTableOp("TABLE1", "Table2");
        FormulaRewriter.Rewrite("SUM(TABLE1[Amount])", op, "Sheet2")
            .Should().Be("SUM(Table2[Amount])");
    }

    [Fact]
    public void RenameTable_StructuredCurrentRowReference_StillRewritesTableName()
    {
        var op = new RenameTableOp("TABLE1", "Table2");
        FormulaRewriter.Rewrite("TABLE1[@Amount]*2", op, "Sheet2")
            .Should().Be("Table2[@Amount]*2");
    }
}
