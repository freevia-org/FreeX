using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for finding F1 (defined-names-span-rowcol-shift): a 3-D sheet-span reference
/// (e.g. <c>Sheet1:Sheet3!A1</c>, <see cref="RangeRefNode.EndSheetName"/> set) was passed through
/// <see cref="FormulaRewriter.Rewrite"/> completely UNTOUCHED for <see cref="InsertRowsOp"/>,
/// <see cref="DeleteRowsOp"/>, <see cref="InsertColsOp"/>, and <see cref="DeleteColsOp"/> -- unlike
/// an ordinary single-sheet range, which shifts/shrinks/collapses exactly like a plain cell
/// reference. This meant a workbook-scoped defined name whose RefersTo is a sheet span (e.g.
/// <c>MySpan = 'Sheet1:Sheet3!$A$1'</c>, routed through this exact <see cref="FormulaRewriter.Rewrite"/>
/// entry point by <c>RowColumnShiftHelpers.RewriteNamedFormulas</c> in FreeX.Core.Commands) kept
/// pointing at the stale pre-edit address forever after a row/column insert or delete on either of
/// the span's two NAMED endpoint sheets, instead of shifting (or collapsing to <c>#REF!</c> on a
/// full delete) the way every other named-formula/cell-formula reference already does.
///
/// The fix handles a row/col insert or delete whose target sheet is one of the span's two named
/// endpoints (<c>rr.SheetName</c> or <c>rr.EndSheetName</c>) by reusing the identical shift/shrink
/// math the plain (non-span) range path already uses. A structural edit on a sheet that lies
/// STRICTLY BETWEEN the two named endpoints in workbook tab order (e.g. Sheet2 of a Sheet1:Sheet3
/// span) remains deliberately unhandled -- FormulaRewriter has no workbook/tab-order context, only
/// a bare op.SheetName string, so it cannot safely tell whether such a sheet is actually inside the
/// span without risking a wrong guess; see the TODO(H28 3-D sheet-span refs) comment at
/// FormulaRewriter.RewriteRange for the full rationale.
/// </summary>
public class F1_FormulaRewriterThreeDSpanRowColShiftTests
{
    // ── Insert rows ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InsertRows_SpanStartSheet_ShiftsSingleCellSpanAddress()
    {
        // Sheet1 is the span's start (named) endpoint -- inserting a row above row 1 on Sheet1
        // must shift the span's shared address from A1 to A2, exactly like an ordinary
        // Sheet1!A1 reference would.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new InsertRowsOp("Sheet1", BeforeRow: 1, Count: 1),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!A2)");
    }

    [Fact]
    public void InsertRows_SpanEndSheet_AlsoShiftsAddress()
    {
        // Sheet3 is the span's END (named) endpoint -- an insert targeting Sheet3 must shift the
        // shared address exactly the same way as an insert targeting the start sheet, since one
        // address serves every sheet in the span.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new InsertRowsOp("Sheet3", BeforeRow: 1, Count: 1),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!A2)");
    }

    [Fact]
    public void InsertRows_SpanRange_ShiftsBothEndpoints()
    {
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A5:A10)",
            new InsertRowsOp("Sheet1", BeforeRow: 1, Count: 2),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!A7:A12)");
    }

    // ── Delete rows ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteRows_SpanStartSheet_ShrinksRange()
    {
        // Deleting rows 3-4 out of a 1..10 range shrinks it to 1..8, mirroring
        // RewriteRangeDeleteRows' band-shrink behaviour for a plain range.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1:A10)",
            new DeleteRowsOp("Sheet1", StartRow: 3, Count: 2),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!A1:A8)");
    }

    [Fact]
    public void DeleteRows_SpanEndSheet_FullyConsumedSingleCell_CollapsesToRefError()
    {
        // The exact scenario from the finding's probe: a single-cell span (Sheet1:Sheet3!A1,
        // matching the Name Manager's 'MySpan' = 'Sheet1:Sheet3!$A$1' example) whose sole cell is
        // deleted must degrade to #REF!, matching every other deleted-reference path in the
        // codebase, rather than being left silently pointing at now-unrelated data.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new DeleteRowsOp("Sheet3", StartRow: 1, Count: 1),
            "Host");

        result.Should().Be("SUM(#REF!)");
    }

    // ── Insert / delete columns ──────────────────────────────────────────────────────────────

    [Fact]
    public void InsertCols_SpanStartSheet_ShiftsSingleCellSpanAddress()
    {
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new InsertColsOp("Sheet1", BeforeCol: 1, Count: 1),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!B1)");
    }

    [Fact]
    public void DeleteCols_SpanEndSheet_ShrinksRange()
    {
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1:J1)",
            new DeleteColsOp("Sheet3", StartCol: 3, Count: 2),
            "Host");

        result.Should().Be("SUM(Sheet1:Sheet3!A1:H1)");
    }

    // ── Sibling: scope boundary deliberately left alone (no false claim of full fix) ───────────

    [Fact]
    public void InsertRows_SpanInteriorSheet_NotAnAmedEndpoint_LeavesSpanUntouched()
    {
        // Sheet2 is neither the span's start nor end NAME (it's a sheet the span passes through
        // by tab order) -- FormulaRewriter has no workbook/tab-order context to know Sheet2 falls
        // between Sheet1 and Sheet3, so it must conservatively leave the span untouched rather
        // than guess. This is the documented, intentional scope boundary of the fix, not a
        // regression: Rewrite returns null (no change) exactly as it did before the fix for every
        // row/col op on a span.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new InsertRowsOp("Sheet2", BeforeRow: 1, Count: 1),
            "Host");

        result.Should().BeNull();
    }

    [Fact]
    public void InsertRows_SpanUnrelatedSheet_LeavesSpanUntouched()
    {
        // A completely unrelated sheet's insert must not touch the span at all -- sibling
        // no-regression case for the ordinary "op sheet doesn't match either endpoint" path.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new InsertRowsOp("SheetUnrelated", BeforeRow: 1, Count: 1),
            "Host");

        result.Should().BeNull();
    }

    [Fact]
    public void InsertRows_NonSpanSheetQualifiedRef_StillShiftsAsBefore()
    {
        // Sibling already-working case: an ordinary (non-3-D-span) sheet-qualified reference must
        // keep shifting exactly as before -- this fix must not regress the non-span path.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1!A1)",
            new InsertRowsOp("Sheet1", BeforeRow: 1, Count: 1),
            "Host");

        result.Should().Be("SUM(Sheet1!A2)");
    }
}
