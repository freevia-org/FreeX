using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R22-cell-reference-rewrite-1: deleting rows/cols that only partially overlap a full-row or
/// full-column reference must SHRINK the reference to the surviving span (like the bounded-range
/// path's ShiftOrClampForDelete), not collapse the WHOLE reference to #REF! just because one
/// endpoint alone fell inside the deleted band.
/// </summary>
public class FormulaRewriterFullRangeDeletePartialOverlapTests
{
    [Fact]
    public void DeleteRows_FullRowRange_MiddleBandRemoved_ShrinksToSurvivingSpan()
    {
        // SUM(7:10): delete rows 5-8 removes rows 7-8 (inside the range) but rows 9-10 survive,
        // sliding up to become rows 5-6. Real Excel shrinks the reference to SUM(5:6).
        var result = FormulaRewriter.Rewrite("SUM(7:10)", new DeleteRowsOp("Sheet1", 5, 4), "Sheet1");
        result.Should().Be("SUM(5:6)");
    }

    [Fact]
    public void DeleteRows_FullRowRange_StartInDeletedBand_Shrinks()
    {
        // Delete only row 1 of SUM(1:3): rows 2-3 survive, sliding up to become 1:2.
        var result = FormulaRewriter.Rewrite("SUM(1:3)", new DeleteRowsOp("Sheet1", 1, 1), "Sheet1");
        result.Should().Be("SUM(1:2)");
    }

    [Fact]
    public void DeleteRows_FullRowRange_EndInDeletedBand_Shrinks()
    {
        // SUM(7:10): delete rows 9-12 removes row 9-10 (the range's end) but rows 7-8 survive
        // above the deleted band and are untouched. Shrinks to SUM(7:8).
        var result = FormulaRewriter.Rewrite("SUM(7:10)", new DeleteRowsOp("Sheet1", 9, 4), "Sheet1");
        result.Should().Be("SUM(7:8)");
    }

    [Fact]
    public void DeleteRows_FullRowRange_EntireRangeDeleted_BecomesRef()
    {
        // Deleting the whole 5-6 band that the reference spans still correctly errors out.
        var result = FormulaRewriter.Rewrite("SUM(5:6)", new DeleteRowsOp("Sheet1", 5, 2), "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteRows_FullRowRange_ReversedEndpointOrder_ShrinksLikeNormalizedRange()
    {
        // Excel treats 3:1 the same as 1:3 — deleting row 1 leaves the surviving rows 2-3,
        // sliding up to 1-2 (still expressed in the original reversed endpoint order).
        var result = FormulaRewriter.Rewrite("SUM(3:1)", new DeleteRowsOp("Sheet1", 1, 1), "Sheet1");
        result.Should().Be("SUM(2:1)");
    }

    [Fact]
    public void DeleteCols_FullColumnRange_MiddleBandRemoved_ShrinksToSurvivingSpan()
    {
        // SUM(G:J): delete columns E-H removes columns G-H (inside the range) but columns I-J
        // survive, sliding left to become E-F. Real Excel shrinks the reference to SUM(E:F).
        var result = FormulaRewriter.Rewrite("SUM(G:J)", new DeleteColsOp("Sheet1", 5, 4), "Sheet1");
        result.Should().Be("SUM(E:F)");
    }

    [Fact]
    public void DeleteCols_FullColumnRange_StartInDeletedBand_Shrinks()
    {
        // Delete only column A of SUM(A:C): columns B-C survive, sliding left to become A:B.
        var result = FormulaRewriter.Rewrite("SUM(A:C)", new DeleteColsOp("Sheet1", 1, 1), "Sheet1");
        result.Should().Be("SUM(A:B)");
    }

    [Fact]
    public void DeleteCols_FullColumnRange_EntireRangeDeleted_BecomesRef()
    {
        var result = FormulaRewriter.Rewrite("SUM(B:C)", new DeleteColsOp("Sheet1", 2, 2), "Sheet1");
        result.Should().Be("SUM(#REF!)");
    }
}
