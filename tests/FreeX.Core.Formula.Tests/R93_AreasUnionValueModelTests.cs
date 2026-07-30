using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R93-AREAS-union-value-model: R85 previously deferred the whole union-operator/multi-area
/// feature (see R85_AreasUnionReferenceTests) because FreeX's reference value model (RangeValue,
/// FreeX.Core.Model.ScalarValue) represents exactly one rectangular area, with no multi-area
/// variant, and the parser rejected "(A1:B2,D5)"-style union references outright.
///
/// This round adds a dedicated <see cref="UnionNode"/> AST node (FormulaNode.cs) and a
/// FreeX.Core.Formula-only <see cref="UnionValue"/> ScalarValue subtype (UnionValue.cs) -- NOT a
/// change to the shared Core.Model.ScalarValue hierarchy, keeping the fix scoped to the formula
/// engine as instructed. Wired: Parser (union-paren parsing), FormulaEvaluator (EvaluateUnionNode,
/// AREAS's own function body, SUM, and the CollectNumbers helper used by AVERAGE/STDEV/VAR/
/// PERCENTILE/etc.), FormulaRewriter (row/col-shift recursion into each area), and
/// FormulaSerializer (round-trip back to "(area1,area2,...)" text).
///
/// Confirmed via BEFORE/AFTER runs (see report): before this change, AREAS((A1:B2,D5,F1:F10)) and
/// SUM((A1:A2,B1:B2)) both returned #VALUE! (a parse-time rejection of the comma-in-parens shape);
/// after, they return the Excel-correct 3 and 33 respectively.
/// </summary>
public sealed class R93_AreasUnionValueModelTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Workbook MakeWorkbook(out Sheet sheet, params (uint row, uint col, ScalarValue val)[] cells)
    {
        var workbook = new Workbook("Test");
        sheet = workbook.AddSheet("S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), val);
        return workbook;
    }

    // --- Enumerated path 1: single range (already worked; no-regression) -----------------------

    [Fact]
    public void Areas_SingleRange_ReturnsOne()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS(A1:B2)", sheet, workbook).Should().Be(new NumberValue(1));
    }

    // --- Enumerated path 2: multi-area union (the fix) ------------------------------------------

    [Fact]
    public void Areas_TwoAreaUnion_ReturnsTwo()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS((A1:B2,C3:D4))", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Areas_ThreeAreaUnion_WithBareCellArea_ReturnsThree()
    {
        // D5 alone (a bare CellRefNode, not a RangeRefNode) must count as one area too.
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS((A1:B2,D5,F1:F10))", sheet, workbook).Should().Be(new NumberValue(3));
    }

    // --- Enumerated path 3: intersection (space operator) -- already worked; no-regression ------

    [Fact]
    public void Areas_IntersectionOfTwoRanges_ReturnsOne()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS(A1:B2 B1:C3)", sheet, workbook).Should().Be(new NumberValue(1));
    }

    // --- Enumerated path 4: defined name resolving to each shape --------------------------------

    [Fact]
    public void Areas_DefinedNameResolvingToSingleRange_ReturnsOne()
    {
        var workbook = MakeWorkbook(out var sheet);
        workbook.NamedRanges["MyRange"] =
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));

        _eval.Evaluate("=AREAS(MyRange)", sheet, workbook).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Areas_DefinedNameResolvingToUnion_ReturnsAreaCount()
    {
        // A name whose RefersTo text is itself a union (e.g. Name Manager's
        // "=Sheet1!A1:B2,Sheet1!D5") resolves through the same named-FORMULA path ordinary
        // formula text does (TryEvaluateNamedFormula -> the normal parser/evaluator), so it
        // benefits from the UnionNode/UnionValue support with no extra wiring.
        var workbook = MakeWorkbook(out var sheet);
        workbook.NamedFormulas["MyUnion"] = "(A1:B2,D5)";

        _eval.Evaluate("=AREAS(MyUnion)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    // --- Enumerated path 5: whole-column/whole-row reference ------------------------------------

    [Theory]
    [InlineData("=AREAS(A:A)")]
    [InlineData("=AREAS(1:1)")]
    public void Areas_FullColumnOrRow_ReturnsOne(string formula)
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate(formula, sheet, workbook).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Areas_UnionOfFullColumns_ReturnsTwo()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS((A:A,B:B))", sheet, workbook).Should().Be(new NumberValue(2));
    }

    // --- Enumerated path 6: error cases ----------------------------------------------------------

    [Fact]
    public void Areas_NonReferenceArgument_ReturnsValueError()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS(1)", sheet, workbook).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Areas_UnionWithNonReferenceArea_ReturnsValueError()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS((A1:B2,1))", sheet, workbook).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Areas_UnionWithMissingSheetArea_ReturnsRefError()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS((A1:B2,Missing!D5))", sheet, workbook).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Areas_MissingSheetSingleReference_ReturnsRefError_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS(Missing!A:A)", sheet, workbook).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Areas_BareUnionAsFormulaBody_ReturnsValueError()
    {
        // Excel: entering "=(A1:B2,D5)" directly in a cell (no enclosing reference-taking
        // function) is #VALUE! -- a union is only meaningful as a function argument.
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=(A1:B2,D5)", sheet, workbook).Should().Be(ErrorValue.Value);
    }

    // --- Sibling consumer: SUM over a union (task explicitly calls this out) -------------------

    [Fact]
    public void Sum_TwoAreaUnion_SumsAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(10)), // B1
            (2u, 2u, new NumberValue(20))  // B2
        );

        _eval.Evaluate("=SUM((A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(33));
    }

    [Fact]
    public void Sum_UnionWithErrorCellInAnArea_PropagatesError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 2u, ErrorValue.DivByZero)
        );

        _eval.Evaluate("=SUM((A1:A1,B1:B1))", sheet, workbook).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Average_TwoAreaUnion_AveragesAcrossBothAreas()
    {
        // Exercises CollectNumbers' UnionValue handling (shared by AVERAGE/STDEV/VAR/PERCENTILE/...).
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(2)),  // A1
            (1u, 2u, new NumberValue(4)),  // B1
            (1u, 3u, new NumberValue(9))   // C1
        );

        _eval.Evaluate("=AVERAGE((A1:A1,B1:C1))", sheet, workbook).Should().Be(new NumberValue(5));
    }

    // --- No-regression: ordinary parenthesized expressions and redundant single-area parens -----

    [Fact]
    public void OrdinaryParenthesizedArithmetic_StillEvaluatesCorrectly_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=(1+2)*3", sheet, workbook).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void RedundantSingleAreaParens_StillEvaluatesToOne_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet);
        _eval.Evaluate("=AREAS((A1:B2))", sheet, workbook).Should().Be(new NumberValue(1));
    }

    // --- Rewriter/serializer round-trip: a union formula survives a row insert -------------------

    [Fact]
    public void FormulaRewriter_RowInsertAboveUnionAreas_ShiftsEveryArea()
    {
        var rewritten = FormulaRewriter.Rewrite(
            "=AREAS((A1:B2,D5,F1:F10))", new InsertRowsOp("Sheet1", 1, 1), "Sheet1");

        rewritten.Should().Be("AREAS((A2:B3,D6,F2:F11))");
    }

    [Fact]
    public void FormulaSerializer_RoundTripsUnionFormula()
    {
        var eval = new FormulaEvaluator();
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("S");
        var ast = FormulaEvaluator.ParseFormula("=AREAS((A1:B2,D5,F1:F10))");
        var text = FormulaSerializer.Serialize(ast);

        text.Should().Be("AREAS((A1:B2,D5,F1:F10))");
        eval.Evaluate(text, sheet, workbook).Should().Be(new NumberValue(3));
    }
}
