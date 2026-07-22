using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R72-io-external-links-4-2: FormulaSerializer's NamedRangeNode case wrote only
/// <see cref="NamedRangeNode.Name"/>, dropping <see cref="NamedRangeNode.SheetQualifier"/>
/// entirely. Whenever <see cref="FormulaRewriter.Rewrite"/> reserializes a whole formula (because
/// ANY node in it changed -- e.g. an unrelated cell reference shifted from an insert-row), a
/// sheet/external-qualified defined-name reference like <c>=[1]Sheet1!ExchangeRate*A1</c> silently
/// lost its qualifier and became <c>=ExchangeRate*A2</c> -- a completely different (and often wrong)
/// local name lookup. The fix writes the qualifier (quoted per the same
/// <see cref="SheetNameFormatter"/> rules <see cref="CellRefNode"/>/<see cref="RangeRefNode"/> use)
/// before the name whenever it's present.
/// </summary>
public sealed class R72_NamedRangeSheetQualifierSerializationTests
{
    [Fact]
    public void Serialize_NamedRangeNode_WithExternalBracketSheetQualifier_EmitsQuotedQualifier()
    {
        var node = new NamedRangeNode("ExchangeRate", SheetQualifier: "[1]Sheet1");

        var serialized = FormulaSerializer.Serialize(node);

        // Bracketed text always needs quoting (SheetNameFormatter.NeedsQuoting rejects '[').
        serialized.Should().Be("'[1]Sheet1'!ExchangeRate");
    }

    [Fact]
    public void Serialize_NamedRangeNode_WithExternalBracketSheetQualifier_RoundTripsThroughParser()
    {
        // Use an already-uppercase name: the Lexer normalizes a plain named-range identifier's
        // case on read (Lexer.ReadIdentifierOrRef's ToUpperInvariantIfNeeded), so a mixed-case
        // Name would legitimately come back upper-cased on reparse — orthogonal to what this test
        // checks (that the SheetQualifier itself survives the round trip unchanged).
        var node = new NamedRangeNode("TAXRATE", SheetQualifier: "[1]Sheet1");
        var serialized = FormulaSerializer.Serialize(node);

        var tokens = new Lexer("=" + serialized).Tokenize();
        var reparsed = new Parser(tokens).Parse();

        reparsed.Should().BeOfType<NamedRangeNode>().Which.Should().BeEquivalentTo(node);
    }

    [Fact]
    public void Serialize_NamedRangeNode_WithoutSheetQualifier_Unchanged()
    {
        var node = new NamedRangeNode("MyName");

        FormulaSerializer.Serialize(node).Should().Be("MyName");
    }

    [Fact]
    public void Rewrite_ExternalSheetQualifiedNamedRange_KeepsQualifier_WhenUnrelatedRefShifts()
    {
        // Insert 1 row before row 1 on "Sheet1" (the formula's own host sheet) shifts the plain
        // A1 reference to A2, forcing FormulaRewriter to reserialize the WHOLE formula — the
        // external-qualified ExchangeRate reference must survive that reserialization unchanged
        // in meaning (previously it was silently deleted, becoming a bare "ExchangeRate*A2").
        var result = FormulaRewriter.Rewrite(
            "[1]Sheet1!ExchangeRate*A1",
            new InsertRowsOp("Sheet1", 1, 1),
            "Sheet1");

        result.Should().NotBeNull();
        // NamedRange identifiers are normalized to upper case on lex (Lexer.ReadIdentifierOrRef),
        // so re-lexing the rewritten formula text upper-cases "ExchangeRate" — orthogonal to what
        // this test checks (that the "[1]Sheet1" qualifier itself survives the reserialization).
        result.Should().Be("'[1]Sheet1'!EXCHANGERATE*A2");
    }

    [Fact]
    public void Rewrite_LocalSheetQualifiedNamedRange_KeepsQualifier_WhenUnrelatedRefShifts()
    {
        var result = FormulaRewriter.Rewrite(
            "Sheet2!MyRange+A1",
            new InsertRowsOp("Sheet1", 1, 1),
            "Sheet1");

        result.Should().NotBeNull();
        result.Should().Be("Sheet2!MYRANGE+A2");
    }
}
