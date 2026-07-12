using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for Finding R30-commands-structural-3dref-2: a 3-D sheet-span reference
/// (e.g. Sheet1:Sheet3!A1, EndSheetName set) was passed through <see cref="FormulaRewriter.Rewrite"/>
/// completely untouched for every structural op, including RenameSheetOp. Renaming an endpoint sheet
/// of the span left the formula text pointing at a sheet name that no longer exists, so the next
/// recalc would (once the accompanying forced-recalc fix from R30-commands-structural-3dref-1 lands)
/// resolve to #REF! instead of tracking the rename the way Excel does. RenameSheetOp is purely
/// textual (unlike row/col shifts, which need per-sheet math the span doesn't support yet), so the
/// fix updates rr.SheetName/rr.EndSheetName directly when either matches the renamed sheet, without
/// touching the still-unimplemented row/col-shift or delete-contract span paths.
/// </summary>
public class R30_FormulaRewriterThreeDSpanRenameSheetTests
{
    [Fact]
    public void RenameSheet_SpanStartSheet_RewritesSpanStartOnly()
    {
        // Sheet1 is the span's start sheet -- renaming it to "Data" must update just that endpoint,
        // leaving the end sheet (Sheet3) and the cell part (A1) untouched.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new RenameSheetOp("Sheet1", "Data"),
            "Host");

        result.Should().Be("SUM(Data:Sheet3!A1)");
    }

    [Fact]
    public void RenameSheet_SpanEndSheet_RewritesSpanEndOnly()
    {
        // Sheet3 is the span's end sheet -- renaming it must update just that endpoint.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1:B2)",
            new RenameSheetOp("Sheet3", "Data"),
            "Host");

        result.Should().Be("SUM(Sheet1:Data!A1:B2)");
    }

    [Fact]
    public void RenameSheet_SpanNotNamingRenamedSheet_LeavesSpanUntouched()
    {
        // Neither endpoint of the span is the renamed sheet, so the whole reference (and the
        // formula) is left exactly as-is -- Rewrite returns null (no change).
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new RenameSheetOp("Sheet2", "Data"),
            "Host");

        result.Should().BeNull();
    }

    [Fact]
    public void RenameSheet_NonSpanSheetQualifiedRef_StillRewrites()
    {
        // Sibling already-working case: an ordinary (non-3-D-span) sheet-qualified reference must
        // keep rewriting exactly as before -- this fix must not regress the non-span path.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1!A1)",
            new RenameSheetOp("Sheet1", "Data"),
            "Host");

        result.Should().Be("SUM(Data!A1)");
    }
}
