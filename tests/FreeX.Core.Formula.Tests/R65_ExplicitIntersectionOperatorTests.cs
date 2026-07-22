using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R65-formula-reference-ops-6-1: Excel's EXPLICIT INTERSECTION operator -- a plain space
/// directly between two reference operands (e.g. <c>=SUM(A1:C3 B2:D4)</c>, which intersects to
/// <c>B2:C3</c>) -- was not implemented at all. Lexer.SkipWhitespace silently discarded the space,
/// there was no Intersection token, and Parser had no intersection precedence level, so the whole
/// formula failed to parse and every caller saw #VALUE! (the FormulaParseException -> #VALUE!
/// fallback in FormulaEvaluator.Evaluate). Fixed by: emitting a TokenType.Intersection token
/// whenever whitespace directly separates two raw CellRef/NamedRange tokens (Lexer's
/// InsertIntersectionTokens); adding Parser.ParseIntersection (precedence tighter than
/// arithmetic/unary, looser than the ':' range operator, matching Excel's reference-operator
/// table); and evaluating the overlap rectangle of the two operands (or #NULL! when they don't
/// overlap, matching Excel's error for a genuinely disjoint intersection).
/// </summary>
public sealed class R65_ExplicitIntersectionOperatorTests
{
    private readonly FormulaEvaluator _eval = new();

    // A1:D4 filled with row*10+col: A1=11,B1=12,C1=13,D1=14, A2=21,B2=22,C2=23,D2=24,
    // A3=31,B3=32,C3=33,D3=34, A4=41,B4=42,C4=43,D4=44.
    // A1:C3 ∩ B2:D4 = B2:C3 = {22,23,32,33} = 110.
    private static Sheet MakeGridSheet()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        for (uint r = 1; r <= 4; r++)
            for (uint c = 1; c <= 4; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(r * 10 + c));
        return sheet;
    }

    [Fact]
    public void Sum_OverIntersectionOfTwoRanges_ReturnsSumOfOverlap()
    {
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(A1:C3 B2:D4)", sheet).Should().Be(new NumberValue(110));
    }

    [Fact]
    public void DisjointIntersection_ReturnsNullError()
    {
        // Column A (col 1) and column C (col 3) never overlap -> #NULL!, matching Excel's error
        // for a genuinely disjoint intersection.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=A1:A2 C1:C2", sheet).Should().Be(ErrorValue.Null);
    }

    [Fact]
    public void DisjointIntersection_AsAggregateArgument_ShortCircuitsWholeFunctionToNullError()
    {
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(A1:A2 C1:C2)", sheet).Should().Be(ErrorValue.Null);
    }

    [Fact]
    public void SingleCellIntersection_OfSameCell_ReturnsThatCellsValue()
    {
        // Two single-cell "ranges" that happen to be the exact same cell overlap trivially.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(B2 B2)", sheet).Should().Be(new NumberValue(22));
    }

    // --- No-regression siblings -------------------------------------------------------------

    [Fact]
    public void OrdinaryWhitespaceAroundCommaArguments_StillParsesNormally()
    {
        // Whitespace around a comma/paren in a function argument list must never be mistaken for
        // the intersection operator -- LHS/RHS of every such gap is a Comma/OpenParen/CloseParen
        // token, never two adjacent CellRef/NamedRange tokens.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM( A1 , B1 )", sheet).Should().Be(new NumberValue(23)); // 11 + 12
    }

    [Fact]
    public void WhitespaceAroundUnaryMinus_StillParsesAsSubtraction()
    {
        // A space before a unary-minus-prefixed reference must still parse as ordinary
        // subtraction, not an intersection attempt (RHS of the gap is a Minus token, not a
        // CellRef/NamedRange token).
        var sheet = MakeGridSheet();

        _eval.Evaluate("=A1 -B1", sheet).Should().Be(new NumberValue(-1)); // 11 - 12
    }

    [Fact]
    public void PlainRangeReference_WithoutIntersection_StillWorks()
    {
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(A1:B2)", sheet).Should().Be(new NumberValue(66)); // 11+12+21+22
    }

    [Fact]
    public void Lexer_DoesNotEmitIntersectionToken_BetweenCommaAndArgument()
    {
        var tokens = new Lexer("=SUM( A1 , B1 )").Tokenize();

        tokens.Should().NotContain(t => t.Type == TokenType.Intersection);
    }

    [Fact]
    public void Lexer_EmitsIntersectionToken_BetweenTwoRangeEndpoints()
    {
        var tokens = new Lexer("=SUM(A1:C3 B2:D4)").Tokenize();

        tokens.Should().Contain(t => t.Type == TokenType.Intersection);
    }
}
