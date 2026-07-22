using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R72-calc-array-broadcast-4-1: the @ (implicit intersection) operator used to classify a
/// NamedRangeNode operand as reference-like PURELY by AST shape (FormulaEvaluator.Operators.cs's
/// ImplicitIntersectionOp(FormulaNode, ScalarValue, IEvalContext)), so =@MySeq where MySeq is
/// bound to the formula "SEQUENCE(3)" (a computed array, RangeValue.IsSheetReference == false)
/// wrongly positionally-intersected it against the formula cell's own row instead of returning
/// the array's top-left element as Excel does (and as the AST-equivalent =@SEQUENCE(3) already
/// did). The fix consults the resolved RangeValue.IsSheetReference provenance flag for the
/// NamedRangeNode case (which also covers a LAMBDA parameter bound to a computed array, since a
/// LAMBDA parameter reference is parsed as a NamedRangeNode too) instead of trusting AST shape
/// alone.
/// </summary>
public sealed class R72_AtOperatorNamedComputedArrayTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void AtOperator_OnNameBoundToComputedArray_ReturnsTopLeftElement_NotPositionalIntersection()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        wb.NamedFormulas["MySeq"] = "SEQUENCE(3)";

        // Formula cell deliberately placed at row 2: under the old (buggy) AST-shape-only
        // classification, =@MySeq would positionally intersect row 2 against the 3-row computed
        // array => 2, instead of the correct Excel behaviour (top-left element of a computed
        // array, regardless of formula-cell position) => 1.
        var result = _evaluator.Evaluate("=@MySeq", sheet, wb, currentCell: new CellAddress(sheet.Id, 2, 1));

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void AtOperator_OnLambdaParameterBoundToComputedArray_ReturnsTopLeftElement()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // A LAMBDA parameter reference is parsed as a NamedRangeNode too, so it hit the same
        // AST-shape bug: =LAMBDA(x,@x)(SEQUENCE(3)) at row 2 used to positionally intersect
        // instead of returning the top-left element.
        var result = _evaluator.Evaluate("=LAMBDA(x,@x)(SEQUENCE(3))", sheet, wb, currentCell: new CellAddress(sheet.Id, 2, 1));

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void AtOperator_OnNameBoundToRealRange_StillPositionallyIntersects()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        wb.NamedFormulas["MyRange"] = "$A$1:$A$3";

        // Sibling no-regression check: a name genuinely bound to a worksheet range must keep
        // positionally intersecting against the formula cell's own row, exactly as before.
        var result = _evaluator.Evaluate("=@MyRange", sheet, wb, currentCell: new CellAddress(sheet.Id, 2, 5));

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void AtOperator_OnBareSequenceCall_Unchanged_ReturnsTopLeftElement()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // Sibling no-regression check: the FunctionCallNode path (already correct before this
        // fix) must remain unaffected.
        var result = _evaluator.Evaluate("=@SEQUENCE(3)", sheet, wb, currentCell: new CellAddress(sheet.Id, 2, 1));

        result.Should().Be(new NumberValue(1));
    }
}
