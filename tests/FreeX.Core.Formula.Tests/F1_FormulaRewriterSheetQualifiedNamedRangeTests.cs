using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for finding freex-defined-names-scope F1: a sheet-qualified defined-name
/// reference (e.g. the "Sheet2" in <c>=Sheet2!TaxRate</c> -- a <see cref="NamedRangeNode"/> with
/// <see cref="NamedRangeNode.SheetQualifier"/> set, produced by
/// <c>Parser.ParseSheetQualifiedReference</c>) fell into <see cref="FormulaRewriter"/>.RewriteNode's
/// "_ => node" catch-all, so its SheetQualifier text was never rewritten by
/// <see cref="RenameSheetOp"/> or <see cref="DeleteSheetOp"/> -- unlike an ordinary
/// <see cref="CellRefNode"/>/<see cref="RangeRefNode"/> sheet-qualified reference (e.g.
/// <c>=Sheet2!A1</c>), which IS rewritten. Renaming or deleting the qualified sheet left the stale
/// sheet name in the formula text forever, silently pointing at whatever future sheet happened to
/// reuse that name. Fixed by adding a <c>RewriteNamedRange</c> case that rewrites
/// <see cref="NamedRangeNode.SheetQualifier"/> the same way <c>RewriteCellRefRenameSheet</c> /
/// <c>RewriteSheetQualifiedRefDeleteSheet</c> already rewrite a CellRefNode's SheetName.
/// </summary>
public class F1_FormulaRewriterSheetQualifiedNamedRangeTests
{
    [Fact]
    public void RenameSheet_SheetQualifiedNamedRange_FollowsRename()
    {
        // The Lexer uppercases bare-identifier NamedRange tokens (same normalization the R66
        // suite documents for a NamedRangeEndpointNode's name endpoint), so "TaxRate" round-trips
        // as "TAXRATE" -- only the sheet-qualifier half is under test here.
        var result = FormulaRewriter.Rewrite(
            "Sheet2!TaxRate", new RenameSheetOp("Sheet2", "Sheet2Renamed"), "Sheet1");

        result.Should().Be("Sheet2Renamed!TAXRATE");
    }

    [Fact]
    public void RenameSheet_SheetQualifiedNamedRange_InsideFunctionArgs_FollowsRename()
    {
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet2!TaxRate,1)", new RenameSheetOp("Sheet2", "Sheet2Renamed"), "Sheet1");

        result.Should().Be("SUM(Sheet2Renamed!TAXRATE,1)");
    }

    [Fact]
    public void DeleteSheet_SheetQualifiedNamedRange_CollapsesToRefError()
    {
        var result = FormulaRewriter.Rewrite(
            "Sheet2!TaxRate", new DeleteSheetOp("Sheet2"), "Sheet1");

        result.Should().Be("#REF!");
    }

    [Fact]
    public void DeleteSheet_SheetQualifiedNamedRange_InsideFunctionArgs_CollapsesToRefError()
    {
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet2!TaxRate,1)", new DeleteSheetOp("Sheet2"), "Sheet1");

        result.Should().Be("SUM(#REF!,1)");
    }

    // ── No-regression siblings ──────────────────────────────────────────────────────────────

    [Fact]
    public void RenameSheet_SheetQualifiedCellRef_StillFollowsRename()
    {
        // The already-correct sibling case named directly in the finding: an ordinary
        // sheet-qualified CellRefNode must keep working exactly as before this fix.
        var result = FormulaRewriter.Rewrite(
            "Sheet2!A1", new RenameSheetOp("Sheet2", "Sheet2Renamed"), "Sheet1");

        result.Should().Be("Sheet2Renamed!A1");
    }

    [Fact]
    public void DeleteSheet_SheetQualifiedCellRef_StillCollapsesToRefError()
    {
        var result = FormulaRewriter.Rewrite(
            "Sheet2!A1", new DeleteSheetOp("Sheet2"), "Sheet1");

        result.Should().Be("#REF!");
    }

    [Fact]
    public void RenameSheet_UnqualifiedNamedRange_StillUnchanged()
    {
        // A bare (unqualified) name carries no sheet text to rewrite -- SheetQualifier is null,
        // so RewriteNamedRange must leave it alone (Rewrite returns null: no change made).
        var result = FormulaRewriter.Rewrite(
            "TaxRate", new RenameSheetOp("Sheet2", "Sheet2Renamed"), "Sheet1");

        result.Should().BeNull();
    }

    [Fact]
    public void RenameSheet_SheetQualifiedNamedRange_DifferentSheet_Unchanged()
    {
        // The qualifier names a sheet other than the one being renamed -- must be left untouched.
        var result = FormulaRewriter.Rewrite(
            "Sheet3!TaxRate", new RenameSheetOp("Sheet2", "Sheet2Renamed"), "Sheet1");

        result.Should().BeNull();
    }

    [Fact]
    public void InsertRows_SheetQualifiedNamedRange_Unchanged()
    {
        // A NamedRangeNode has no row/col coordinates of its own, so a plain row/col
        // insert/delete op (as opposed to Rename/Delete-sheet) must still leave it untouched.
        var result = FormulaRewriter.Rewrite(
            "Sheet2!TaxRate", new InsertRowsOp("Sheet2", 1, 1), "Sheet1");

        result.Should().BeNull();
    }
}
