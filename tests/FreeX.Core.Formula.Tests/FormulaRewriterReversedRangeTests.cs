using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for reversed-endpoint ranges (e.g. A5:A1, B3:A1) on the delete-row/column and
/// delete-cells shift-up/shift-left rewrite paths. Excel treats A5:A1 identically to A1:A5, so a
/// partial-band delete must SHRINK the surviving reference the same way it would for the normalized
/// range — not collapse it to #REF! merely because the raw (unnormalized) Start/End comparison made
/// the deleted band look like it covered the "whole" range.
/// </summary>
public class FormulaRewriterReversedRangeTests
{
    // ── DeleteRowsOp ──────────────────────────────────────────────────────────

    [Fact]
    public void DeleteRows_ReversedRowRange_PartialOverlap_ShrinksInsteadOfRef()
    {
        // =SUM(A5:A1) is equivalent to A1:A5. Deleting rows 2-3 leaves rows 1,4,5 (renumbered 1,2,3),
        // so Excel shrinks to A1:A3 — it must NOT collapse to #REF!.
        var result = FormulaRewriter.Rewrite("SUM(A5:A1)", new DeleteRowsOp("Sheet1", 2, 2), "Sheet1");
        result.Should().Be("SUM(A3:A1)");
    }

    [Fact]
    public void DeleteRows_ReversedRowRange_EntireRangeDeleted_BecomesRef()
    {
        // A5:A1 normalizes to A1:A5; deleting rows 1-5 removes the whole thing → #REF!.
        var result = FormulaRewriter.Rewrite("SUM(A5:A1)", new DeleteRowsOp("Sheet1", 1, 5), "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    // ── DeleteColsOp ──────────────────────────────────────────────────────────

    [Fact]
    public void DeleteCols_ReversedColRange_PartialOverlap_ShrinksInsteadOfRef()
    {
        // SUM(D1:B1) normalizes to B1:D1. Deleting column C (band [3,3], strictly inside) shrinks
        // the normalized range by one column to B1:C1 — written back in the original reversed
        // endpoint order as C1:B1, and must NOT collapse to #REF!.
        var result = FormulaRewriter.Rewrite("SUM(D1:B1)", new DeleteColsOp("Sheet1", 3, 1), "Sheet1");
        result.Should().Be("SUM(C1:B1)");
    }

    [Fact]
    public void DeleteCols_ReversedColRange_EntireRangeDeleted_BecomesRef()
    {
        // D1:B1 normalizes to B1:D1; deleting columns B-D removes the whole range → #REF!.
        var result = FormulaRewriter.Rewrite("SUM(D1:B1)", new DeleteColsOp("Sheet1", 2, 3), "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    // ── DeleteCellsShiftUpOp ──────────────────────────────────────────────────

    [Fact]
    public void DeleteCellsShiftUp_ReversedRowRange_PartialOverlap_ShrinksInsteadOfRef()
    {
        // SUM(A20:A11) normalizes to A11:A20. Deleting A10:A12 (shift-up, column band A..A) removes
        // rows 10-12; only row 11 of the range falls in the deleted band, row 20 survives — so the
        // range must shrink (to A10:A17), not collapse to #REF!.
        var op = new DeleteCellsShiftUpOp("Sheet1", DeletedStartRow: 10, DeletedEndRow: 12, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1, Count: 3);
        var result = FormulaRewriter.Rewrite("SUM(A20:A11)", op, "Sheet1");
        result.Should().Be("SUM(A17:A10)");
    }

    [Fact]
    public void DeleteCellsShiftUp_ReversedRowRange_EntireRangeInDeletedBand_BecomesRef()
    {
        var op = new DeleteCellsShiftUpOp("Sheet1", DeletedStartRow: 10, DeletedEndRow: 20, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1, Count: 11);
        var result = FormulaRewriter.Rewrite("SUM(A15:A11)", op, "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    // ── DeleteCellsShiftLeftOp ────────────────────────────────────────────────

    [Fact]
    public void DeleteCellsShiftLeft_ReversedColRange_PartialOverlap_ShrinksInsteadOfRef()
    {
        // SUM(T1:K1) normalizes to K1:T1. Deleting J1:L1 (shift-left, row band 1..1) removes
        // columns 10-12; only column K(=11) of the range falls in the deleted band, T(=20)
        // survives — so the range must shrink (to J1:Q1), not collapse to #REF!.
        var op = new DeleteCellsShiftLeftOp("Sheet1", BandStartRow: 1, BandEndRow: 1,
            DeletedStartCol: 10, DeletedEndCol: 12, BandEndCol: 16384, Count: 3);
        var result = FormulaRewriter.Rewrite("SUM(T1:K1)", op, "Sheet1");
        result.Should().Be("SUM(Q1:J1)");
    }

    [Fact]
    public void DeleteCellsShiftLeft_ReversedColRange_EntireRangeInDeletedBand_BecomesRef()
    {
        var op = new DeleteCellsShiftLeftOp("Sheet1", BandStartRow: 1, BandEndRow: 1,
            DeletedStartCol: 10, DeletedEndCol: 20, BandEndCol: 16384, Count: 11);
        var result = FormulaRewriter.Rewrite("SUM(O1:K1)", op, "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }
}
