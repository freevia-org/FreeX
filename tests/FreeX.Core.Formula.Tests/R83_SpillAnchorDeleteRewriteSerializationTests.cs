using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R83-calc-spill-dependency-5-1: deleting a spill anchor's own row/column
/// (or, for the A1#:B5 shape, its end cell) rewrites the wrapped CellRefNode/NamedRangeNode
/// argument of the internal ANCHORARRAY(...) node to ErrorNode(#REF!) (see
/// FormulaRewriter.RewriteCellRefDeleteRows and friends), but none of FormulaSerializer's four
/// ANCHORARRAY-specific patterns matched an ErrorNode argument, so serialization fell through to
/// the generic function-call case and printed the literal, non-Excel text
/// "SUM(ANCHORARRAY(#REF!))" instead of Excel's own "SUM(#REF!)". Persisting that text (e.g. to
/// .xlsx) writes a &lt;f&gt; element Excel itself does not recognize (#NAME?/repair prompt on
/// open), and re-parsing it for recalculation hit EvaluateAnchorArray's default branch, yielding
/// #VALUE! instead of the #REF! Excel would show. The fix adds an ANCHORARRAY-with-ErrorNode-
/// argument serializer case that drops the wrapper and prints just the error.
/// </summary>
public sealed class R83_SpillAnchorDeleteRewriteSerializationTests
{
    [Fact]
    public void Rewrite_DeleteAnchorRow_SerializesAsPlainRefError_NotAnchorArrayWrapper()
    {
        // A1 is the spill anchor referenced via A1# inside SUM(A1#). Deleting row 1 (the anchor's
        // own row) must collapse the whole spill reference to a bare #REF!, matching Excel --
        // never the internal "ANCHORARRAY(#REF!)" wrapper text.
        var result = FormulaRewriter.Rewrite(
            "SUM(A1#)",
            new DeleteRowsOp("Sheet1", 1, 1),
            "Sheet1");

        result.Should().NotBeNull();
        result.Should().Be("SUM(#REF!)");
    }

    [Fact]
    public void Rewrite_DeleteUnrelatedRow_AnchorReferenceShiftsAndStaysWrapped()
    {
        // Sibling no-regression case: deleting a row that is NOT the anchor's own row must still
        // shift the anchor reference and reserialize through the normal ANCHORARRAY(CellRefNode)
        // spill-anchor pattern (i.e. stay a live "<ref>#" spill reference), confirming the new
        // ErrorNode case does not interfere with the ordinary rewrite path.
        var result = FormulaRewriter.Rewrite(
            "SUM(A5#)",
            new DeleteRowsOp("Sheet1", 1, 1),
            "Sheet1");

        result.Should().NotBeNull();
        result.Should().Be("SUM(A4#)");
    }

    [Fact]
    public void Serialize_AnchorArrayWithErrorNodeArgument_EmitsBareError()
    {
        // Direct serializer-level check for the single-argument A1# shape.
        var node = new FunctionCallNode("ANCHORARRAY", [new ErrorNode(ErrorValue.Ref)]);

        FormulaSerializer.Serialize(node).Should().Be("#REF!");
    }

    [Fact]
    public void Serialize_AnchorArrayRangeShape_WithErrorNodeAnchor_EmitsBareError()
    {
        // The A1#:B5 shape (spill range used as the start endpoint of a larger range): when the
        // anchor itself is deleted, the whole reference collapses to #REF! too -- not
        // "ANCHORARRAY(#REF!,B5)" and not "#REF!#:B5".
        var node = new FunctionCallNode(
            "ANCHORARRAY",
            [new ErrorNode(ErrorValue.Ref), new CellRefNode("B", 5)]);

        FormulaSerializer.Serialize(node).Should().Be("#REF!");
    }

    [Fact]
    public void Serialize_AnchorArrayRangeShape_WithErrorNodeEnd_KeepsSurvivingAnchor()
    {
        // R84-meta-2: the A1#:B5 shape where only the END cell (B5) was invalidated by a delete
        // (e.g. deleting B5's own row) while the anchor A1 was untouched. A1# alone is still a
        // complete, valid reference -- Excel keeps it and only replaces the invalidated endpoint,
        // exactly like its ordinary two-endpoint range behavior (A1:C5 -> A1:#REF! when only
        // C5's row is deleted) -- so this must serialize as "A1#:#REF!", never a bare "#REF!"
        // that silently discards the still-live spill anchor.
        var node = new FunctionCallNode(
            "ANCHORARRAY",
            [new CellRefNode("A", 1), new ErrorNode(ErrorValue.Ref)]);

        FormulaSerializer.Serialize(node).Should().Be("A1#:#REF!");
    }

    [Fact]
    public void Rewrite_DeleteEndCellRowOnly_KeepsSurvivingAnchorInRangeShape()
    {
        // No-regression sibling at the FormulaRewriter level: deleting B5's own row (not A1's)
        // must rewrite SUM(A1#:B5) to SUM(A1#:#REF!), preserving the still-valid spill anchor,
        // not collapsing the whole reference to a bare #REF!.
        var result = FormulaRewriter.Rewrite(
            "SUM(A1#:B5)",
            new DeleteRowsOp("Sheet1", 5, 1),
            "Sheet1");

        result.Should().NotBeNull();
        result.Should().Be("SUM(A1#:#REF!)");
    }
}
