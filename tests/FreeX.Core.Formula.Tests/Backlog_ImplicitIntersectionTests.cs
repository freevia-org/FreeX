using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Backlog item implicit-at: the @ (implicit intersection) operator applied to a
/// COMPUTED/dynamic-array result (e.g. a function call like SEQUENCE(3)) must return the array's
/// TOP-LEFT element, matching Excel — there is no worksheet row/col to positionally intersect
/// against because the array isn't anchored to cells. @ applied to a genuine REFERENCE expression
/// (a bare range, full row/column, or named range) must keep the pre-existing positional-intersection
/// behavior against the formula cell's own row/col.
///
/// Prior attempts discriminated on RangeValue.SheetName being null, which also matched same-sheet
/// reference-backed ranges (a bare same-sheet range reference doesn't set SheetName either) and broke
/// real-Excel-fixture Integration tests. The fix here discriminates on the operand's AST node shape
/// (FormulaEvaluator.Operators.cs's ImplicitIntersectionOp overload taking the FormulaNode), reusing
/// the same reference-node whitelist FormulaEvaluator.cs's EvaluateSpilling already uses to separate
/// "reference-like" top-level nodes from computed ones.
/// </summary>
public sealed class Backlog_implicit_at_Tests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void At_OnComputedDynamicArray_ReturnsTopLeftElement_NotPositionalIntersection()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        // Formula cell deliberately placed at row 2 so that, under the old (buggy) positional
        // intersection logic, @SEQUENCE(3) would have picked row 2 => 2 instead of the correct
        // top-left => 1.
        var result = _evaluator.Evaluate("=@SEQUENCE(3)", sheet, currentCell: new CellAddress(sheet.Id, 2, 1));

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void At_OnComputedDynamicArray_2D_ReturnsTopLeftElement()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        // SEQUENCE(2,2) => {1,2;3,4}. Formula cell offset so positional intersection (if wrongly
        // applied) would not land on the top-left cell either.
        var result = _evaluator.Evaluate("=@SEQUENCE(2,2)", sheet, currentCell: new CellAddress(sheet.Id, 5, 5));

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void At_OnReferenceRange_StillPositionallyIntersectsByFormulaCell()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        // Reference-backed range: @ must keep positionally intersecting against the formula cell's
        // row, exactly as before this fix (regression guard for the reference-backed case).
        var result = _evaluator.Evaluate("=@A1:A3", sheet, currentCell: new CellAddress(sheet.Id, 2, 5));

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void At_OnReferenceRange_OffAxis_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        // Formula cell's row/col falls outside the referenced range entirely -> #VALUE!, unchanged
        // reference-backed behavior.
        var result = _evaluator.Evaluate("=@A1:C1", sheet, currentCell: new CellAddress(sheet.Id, 9, 9));

        result.Should().Be(ErrorValue.Value);
    }
}
