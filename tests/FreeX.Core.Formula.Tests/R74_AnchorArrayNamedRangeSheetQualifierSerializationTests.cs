using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R74-meta-1: the r72 fix (R72-io-external-links-4-2) taught FormulaSerializer's plain
/// <c>case NamedRangeNode nr</c> to write <see cref="NamedRangeNode.SheetQualifier"/> via
/// <c>WriteNamedRangeName</c>, but the two ANCHORARRAY(NamedRangeNode) spill-anchor branches
/// (backing the A1# operator over a named-range anchor, e.g. <c>MyDynArr#</c> /
/// <c>MyDynArr#:B5</c>) still wrote a bare <c>anchorName.Name</c> / <c>rangeAnchorName.Name</c>,
/// silently dropping the qualifier. Whenever <see cref="FormulaRewriter.Rewrite"/> reserializes a
/// whole formula (because ANY node in it changed -- e.g. an unrelated cell reference shifted from
/// an insert-row), a sheet-qualified dynamic-array spill reference like
/// <c>=Sheet2!MyDynArr#+A1</c> silently lost its qualifier and became <c>=MyDynArr#+A2</c> -- a
/// completely different (and often wrong) local name lookup. The fix routes both branches through
/// the same <c>WriteNamedRangeName</c> helper the plain NamedRangeNode case already uses.
/// </summary>
public sealed class R74_AnchorArrayNamedRangeSheetQualifierSerializationTests
{
    [Fact]
    public void Serialize_AnchorArrayOverNamedRange_WithSheetQualifier_EmitsQualifier()
    {
        var node = new FunctionCallNode("ANCHORARRAY", [new NamedRangeNode("MyDynArr", SheetQualifier: "Sheet2")]);

        FormulaSerializer.Serialize(node).Should().Be("Sheet2!MyDynArr#");
    }

    [Fact]
    public void Serialize_AnchorArrayOverNamedRange_WithoutSheetQualifier_Unchanged()
    {
        // Sibling no-regression case: an unqualified spill-anchor name must still serialize with
        // no qualifier prefix at all -- this fix must not add one where none existed.
        var node = new FunctionCallNode("ANCHORARRAY", [new NamedRangeNode("MyDynArr")]);

        FormulaSerializer.Serialize(node).Should().Be("MyDynArr#");
    }

    [Fact]
    public void Serialize_AnchorArrayRangeAnchorOverNamedRange_WithSheetQualifier_EmitsQualifier()
    {
        // The A1#:B5 shape (spill range used as the start endpoint of a larger range) over a named
        // anchor -- the qualifier must survive on this branch too.
        var node = new FunctionCallNode(
            "ANCHORARRAY",
            [new NamedRangeNode("MyDynArr", SheetQualifier: "Sheet2"), new CellRefNode("B", 5)]);

        FormulaSerializer.Serialize(node).Should().Be("Sheet2!MyDynArr#:B5");
    }

    [Fact]
    public void Parse_SheetQualifiedAnchorArrayNamedRange_RoundTripsThroughSerializer()
    {
        // Use an already-uppercase name: the Lexer normalizes a plain named-range identifier's
        // case on read, so a mixed-case Name would legitimately come back upper-cased on reparse —
        // orthogonal to what this test checks (that the SheetQualifier itself survives).
        var tokens = new Lexer("=Sheet2!MYDYNARR#").Tokenize();
        var ast = new Parser(tokens).Parse();

        ast.Should().BeOfType<FunctionCallNode>()
            .Which.Arguments.Should().ContainSingle()
            .Which.Should().BeOfType<NamedRangeNode>()
            .Which.SheetQualifier.Should().Be("Sheet2");

        FormulaSerializer.Serialize(ast).Should().Be("Sheet2!MYDYNARR#");
    }

    [Fact]
    public void Rewrite_SheetQualifiedAnchorArrayNamedRange_KeepsQualifier_WhenUnrelatedRefShifts()
    {
        // Insert 1 row before row 1 on "Sheet1" (the formula's own host sheet) shifts the plain A1
        // reference to A2, forcing FormulaRewriter to reserialize the WHOLE formula -- the
        // sheet-qualified spill-anchor reference must survive that reserialization unchanged in
        // meaning (previously it was silently rewritten to an unqualified, wrong-scope name).
        // A named-range identifier immediately followed by '#' preserves its defined display case
        // through the lexer/serializer (Lexer.ReadIdentifierOrRef) rather than being upper-cased
        // like an ordinary bare named-range reference, so "MyDynArr" stays mixed-case here.
        var result = FormulaRewriter.Rewrite(
            "Sheet2!MyDynArr#+A1",
            new InsertRowsOp("Sheet1", 1, 1),
            "Sheet1");

        result.Should().NotBeNull();
        result.Should().Be("Sheet2!MyDynArr#+A2");
    }

    [Fact]
    public void Rewrite_UnqualifiedAnchorArrayNamedRange_Unchanged_WhenUnrelatedRefShifts()
    {
        // Sibling no-regression case for the rewrite path: an unqualified spill-anchor name must
        // still reserialize with no qualifier prefix after an unrelated shift.
        var result = FormulaRewriter.Rewrite(
            "MyDynArr#+A1",
            new InsertRowsOp("Sheet1", 1, 1),
            "Sheet1");

        result.Should().NotBeNull();
        result.Should().Be("MyDynArr#+A2");
    }
}
