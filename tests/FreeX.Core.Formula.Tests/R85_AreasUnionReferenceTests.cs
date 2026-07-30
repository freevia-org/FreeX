using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-85 backlog item "AREAS-union": Excel's AREAS(reference) counts the number of areas
/// (contiguous ranges) in a reference, and a multi-area reference is written using the union
/// operator behind an extra set of parens, e.g. AREAS((A1:B2,D5,F1:F10)) = 3.
///
/// R85 found AREAS itself already correct for every reference shape FreeX's engine could
/// represent at the time (a plain range, a bare single cell, a full row/column, and a reference
/// redundantly wrapped in an extra pair of parens all correctly return 1; a non-reference argument
/// correctly returns #VALUE!), but deferred the union operator itself (a bare ',' inside a
/// parenthesized reference, e.g. (A1:B2,D5)): FreeX's reference value model (RangeValue, see
/// FreeX.Core.Model.ScalarValue) represents exactly one rectangular area, with no multi-area
/// variant, and the parser rejected the shape outright with a documented "not supported" message.
///
/// R93-AREAS-union-value-model implements the union operator without touching the shared
/// Core.Model.ScalarValue hierarchy: a new UnionNode AST node (FormulaNode.cs) and a
/// FreeX.Core.Formula-only UnionValue (carrying one RangeValue per area) that only ever exists
/// transiently during evaluation -- never stored in a cell or serialized. AREAS((A1:B2,D5,F1:F10))
/// now correctly evaluates to 3 (see the tests below, updated from R85's "deliberately still
/// #VALUE!" assertions to match: per this round's ground-truth rule, a deliberately-authored
/// existing test that only locks in a previous round's SCOPE DEFERRAL -- not a genuine behavioral
/// decision -- must yield once Excel's real behavior is actually implemented).
/// </summary>
public sealed class R85_AreasUnionReferenceTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }

    // --- R93 fix: the union-paren shape now parses into a UnionNode and evaluates correctly -----

    [Fact]
    public void UnionReferenceInParens_ParsesSuccessfully()
    {
        // Pre-R93: this threw a documented FormulaParseException ("Union references ... are not
        // supported"). Post-R93: the parser builds a UnionNode instead of rejecting the shape.
        var formula = "=AREAS((A1:B2,D5,F1:F10))";

        Action act = () => new Parser(new Lexer(formula).Tokenize()).Parse();

        act.Should().NotThrow();
    }

    [Fact]
    public void SimpleUnionParens_ParsesSuccessfully()
    {
        // Sibling shape: a plain 2-area union, not nested inside a function call at all.
        var formula = "=(A1:B2,C3)";

        Action act = () => new Parser(new Lexer(formula).Tokenize()).Parse();

        act.Should().NotThrow();
    }

    [Fact]
    public void Areas_UnionReference_TopLevelEval_ReturnsAreaCount()
    {
        // Ground truth: real Excel's AREAS((A1:B2,D5,F1:F10)) is 3 (three comma-separated areas).
        // R85 deliberately locked in #VALUE! here as a documented scope deferral, not a genuine
        // behavioral decision -- R93 implements the union operator, so this must now match Excel.
        var sheet = MakeSheet();

        _eval.Evaluate("=AREAS((A1:B2,D5,F1:F10))", sheet).Should().Be(new NumberValue(3));
    }

    // --- No-regression: ordinary (non-union) parenthesized expressions are unaffected ----------

    [Fact]
    public void OrdinaryParenthesizedArithmetic_StillEvaluatesCorrectly_NoRegression()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=(1+2)*3", sheet).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void RedundantSingleAreaParens_AroundRange_StillEvaluatesToOne_NoRegression()
    {
        // A single reference wrapped in an extra (redundant, but valid) pair of parens -- e.g. a
        // user who always writes AREAS((ref)) out of habit -- is NOT a union and must keep working.
        var sheet = MakeSheet();

        _eval.Evaluate("=AREAS((A1:B2))", sheet).Should().Be(new NumberValue(1));
    }

    // --- No-regression: AREAS is already correct for every reference shape the engine has -------

    [Theory]
    [InlineData("=AREAS(B2:C4)")]
    [InlineData("=AREAS(A:A)")]
    [InlineData("=AREAS(1:1)")]
    [InlineData("=AREAS(A1)")]
    public void Areas_SingleAreaShapes_ReturnOne_NoRegression(string formula)
    {
        var sheet = MakeSheet();

        _eval.Evaluate(formula, sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Areas_TooManyArguments_ReturnsValueError_NoRegression()
    {
        // AREAS takes exactly one reference argument; passing a second one directly (without the
        // union-grouping parens Excel requires) is simply too many arguments -- already correctly
        // rejected today via the function's declared (1,1) arity, independent of union support.
        var sheet = MakeSheet();

        _eval.Evaluate("=AREAS(A1:B2,D5)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Areas_NonReferenceArgument_ReturnsValueError_NoRegression()
    {
        _eval.Evaluate("=AREAS(1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }
}
