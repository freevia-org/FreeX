using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for the MED finding at FormulaRewriter.cs:310: a 3-D sheet-span reference
/// (e.g. Sheet1:Sheet3!A1, EndSheetName set) was passed through <see cref="FormulaRewriter.Rewrite"/>
/// completely unchanged for <see cref="DeleteSheetOp"/> -- unlike an ordinary sheet-qualified
/// reference, which <c>RewriteSheetQualifiedRefDeleteSheet</c> permanently freezes to
/// <c>#REF!</c> when its sheet is deleted. Leaving the span's text untouched meant the caller
/// (<c>SheetCommands.RewriteNamedFormulasForDeletedSheet</c> / ...ScopedNamedFormulasForDeletedSheet
/// in FreeX.Core.Commands, both of which route stored RefersTo text through this exact
/// <see cref="FormulaRewriter.Rewrite"/> entry point) saw <c>rewritten == original</c> and skipped
/// the update, so a defined name's RefersTo kept reading the literal deleted-sheet name forever --
/// and because <c>TryExpandSheetSpanAggregateRange</c> re-resolves both span endpoints by sheet NAME
/// on every recalculation (not by a stable sheet id), the name would silently reattach to any future
/// sheet that happened to reuse the deleted name. The fix mirrors
/// <c>RewriteSheetQualifiedRefDeleteSheet</c>: when either span endpoint names the deleted sheet, the
/// whole span collapses to <c>#REF!</c> and <c>changed</c> is set so the caller persists the rewrite.
/// </summary>
public class R94_FormulaRewriterThreeDSpanDeleteSheetTests
{
    [Fact]
    public void DeleteSheet_SpanStartSheet_CollapsesWholeSpanToRefError()
    {
        // Sheet1 is the span's start sheet -- deleting it must freeze the ENTIRE span (not just
        // the start endpoint) to #REF!, exactly like an ordinary sheet-qualified ref.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new DeleteSheetOp("Sheet1"),
            "Host");

        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteSheet_SpanEndSheet_CollapsesWholeSpanToRefError()
    {
        // Sheet3 is the span's end sheet -- deleting it must also freeze the whole span, not
        // leave the (now-stale) start sheet name dangling in the formula text.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1:B2)",
            new DeleteSheetOp("Sheet3"),
            "Host");

        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteSheet_SpanNotNamingDeletedSheet_LeavesSpanUntouched()
    {
        // Sheet2 is neither endpoint of the span (it's a sheet the span passes THROUGH), so the
        // span text must be left exactly as-is -- Rewrite returns null (no change), matching how
        // callers (e.g. SheetCommands.RewriteNamedFormulasForDeletedSheet) treat "unchanged" as
        // "leave the original text untouched".
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet3!A1)",
            new DeleteSheetOp("Sheet2"),
            "Host");

        result.Should().BeNull();
    }

    [Fact]
    public void DeleteSheet_NonSpanSheetQualifiedRef_StillRewritesToRefError()
    {
        // Sibling already-working case: an ordinary (non-3-D-span) sheet-qualified reference must
        // keep collapsing to #REF! exactly as before -- this fix must not regress the non-span path
        // that RewriteSheetQualifiedRefDeleteSheet already handled correctly.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1!A1)",
            new DeleteSheetOp("Sheet1"),
            "Host");

        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void DeleteSheet_SpanBothEndpointsSameSheet_CollapsesToRefError()
    {
        // Single-sheet "span" form (Sheet1:Sheet1!A1, e.g. from a name that was authored as a
        // bare span over one sheet) must also collapse when that sheet is deleted.
        var result = FormulaRewriter.Rewrite(
            "SUM(Sheet1:Sheet1!A1)",
            new DeleteSheetOp("Sheet1"),
            "Host");

        result.Should().Be("SUM(#REF!)");
    }
}
