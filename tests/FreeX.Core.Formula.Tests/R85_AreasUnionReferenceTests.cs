using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-85 backlog item "AREAS-union": Excel's AREAS(reference) counts the number of areas
/// (contiguous ranges) in a reference, and a multi-area reference is written using the union
/// operator behind an extra set of parens, e.g. AREAS((A1:B2,D5,F1:F10)) = 3.
///
/// Investigation found AREAS itself is already correct for every reference shape FreeX's engine
/// can actually represent (a plain range, a bare single cell, a full row/column, and a reference
/// redundantly wrapped in an extra pair of parens all correctly return 1; a non-reference
/// argument correctly returns #VALUE!). The gap is that the union operator (a bare ',' inside a
/// parenthesized reference, e.g. (A1:B2,D5)) is not parsed at all -- FreeX's reference value model
/// (RangeValue, see FreeX.Core.Model.ScalarValue) represents exactly one rectangular area tied to
/// a single SheetName/StartRow/StartCol; there is no multi-area variant. Adding one is a large,
/// cross-cutting change: a new AST node, a new ScalarValue kind, and updates to every one of the
/// 15+ files that pattern-match on RangeRefNode-shaped fast paths, plus FormulaRewriter's row/
/// col-shift logic and FormulaSerializer's round-trip -- squarely the "large/risky" case this
/// backlog round is instructed to defer rather than force.
///
/// What WAS fixed (surgical, zero behavior change at the evaluation level): the union-paren shape
/// used to fail with a generic "Expected CloseParen but got Comma" parser message -- an accidental
/// byproduct of Parser.ParsePrimary's plain grouped-expression case, indistinguishable from any
/// other malformed formula. It still surfaces as #VALUE! (FormulaEvaluator.Evaluate's top-level
/// FormulaParseException handler treats every unparseable formula, this one included, as #VALUE!
/// rather than a crash -- exactly the "a proper error... rather than a wrong number" contract this
/// round calls for), but the parser now recognizes the shape explicitly and reports a specific,
/// documented "union references are not supported" message instead of the generic one, so the
/// deferral is an intentional, discoverable decision rather than an accident.
///
/// The union-operator/multi-area-value-model feature itself remains STILL-DEFERRED.
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

    // --- The fix: the union-paren shape now gets a specific, documented parser message ---------

    [Fact]
    public void UnionReferenceInParens_ParserThrows_WithExplicitUnsupportedMessage()
    {
        // Pre-fix: this threw FormulaParseException with the generic
        // "Expected CloseParen but got Comma ...' at position ..." message -- indistinguishable
        // from any other malformed-formula parse failure. Post-fix: the parser recognizes the
        // union-paren shape explicitly and names it in the message.
        var formula = "=AREAS((A1:B2,D5,F1:F10))";

        Action act = () => new Parser(new Lexer(formula).Tokenize()).Parse();

        act.Should().Throw<FormulaParseException>()
            .WithMessage("*Union references*not supported*");
    }

    [Fact]
    public void SimpleUnionParens_ParserThrows_WithExplicitUnsupportedMessage()
    {
        // Sibling shape: a plain 2-area union, not nested inside a function call at all.
        var formula = "=(A1:B2,C3)";

        Action act = () => new Parser(new Lexer(formula).Tokenize()).Parse();

        act.Should().Throw<FormulaParseException>()
            .WithMessage("*Union references*not supported*");
    }

    [Fact]
    public void Areas_UnionReference_TopLevelEval_StillReturnsValueError_NoRegression()
    {
        // The observable evaluation-level result for the whole formula must NOT change: still a
        // graceful #VALUE! (via FormulaEvaluator.Evaluate's top-level FormulaParseException
        // catch), never a crash and never a silently wrong area count.
        var sheet = MakeSheet();

        _eval.Evaluate("=AREAS((A1:B2,D5,F1:F10))", sheet).Should().Be(ErrorValue.Value);
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
