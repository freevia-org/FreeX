using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R33-formula-array-spill-internals-2-2: SINGLE() (FormulaEvaluator.Functions.cs's EvaluateSingle)
/// only has the already-evaluated scalar in hand, not the operand AST node, so it reaches the
/// scalar-only ImplicitIntersectionOp(ScalarValue, IEvalContext) overload in
/// FormulaEvaluator.Operators.cs. That overload used to always positionally intersect the operand
/// against the formula cell, even for a COMPUTED array (e.g. SEQUENCE(3)) that isn't anchored to any
/// worksheet cell — producing #VALUE! instead of Excel's top-left element. The fix mirrors the
/// AST-aware @ operator's overload: discriminate on RangeValue.IsSheetReference (set only when the
/// range was materialized directly from a genuine worksheet reference) rather than always resolving
/// positionally.
/// </summary>
public sealed class R33_SingleFunctionImplicitIntersectionTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void Single_OnComputedDynamicArray_ReturnsTopLeftElement_NotPositionalIntersection()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        // Formula cell deliberately placed at row 5 so that, under the old (buggy) positional
        // intersection logic, SINGLE(SEQUENCE(3)) would try to intersect row 5 against the 3-row
        // array (out of range) => #VALUE! instead of the correct top-left => 1.
        var result = _evaluator.Evaluate("=SINGLE(SEQUENCE(3))", sheet, currentCell: new CellAddress(sheet.Id, 5, 4));

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Single_OnReferenceRange_StillPositionallyIntersectsByFormulaCell()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        // Reference-backed range: SINGLE() must keep positionally intersecting against the formula
        // cell's row, exactly as before this fix (sibling regression guard for the reference case).
        var result = _evaluator.Evaluate("=SINGLE(A1:A3)", sheet, currentCell: new CellAddress(sheet.Id, 2, 5));

        result.Should().Be(new NumberValue(20));
    }
}
