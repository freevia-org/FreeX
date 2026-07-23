using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// Implicit intersection (legacy @): a range used in a scalar context resolves to the single cell that
// shares the formula's row (column-range) or column (row-range); off-axis is #VALUE!.
public class ImplicitIntersectionTests
{
    private static RangeValue RowRange(uint startRow, uint startCol, params double[] values)
    {
        var cells = new ScalarValue[1, values.Length];
        for (var c = 0; c < values.Length; c++) cells[0, c] = new NumberValue(values[c]);
        return new RangeValue(cells, startRow, startCol);
    }

    private static RangeValue ColRange(uint startRow, uint startCol, params double[] values)
    {
        var cells = new ScalarValue[values.Length, 1];
        for (var r = 0; r < values.Length; r++) cells[r, 0] = new NumberValue(values[r]);
        return new RangeValue(cells, startRow, startCol);
    }

    [Fact]
    public void SingleRowRange_MatchesFormulaColumn()
    {
        var range = RowRange(7, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10); // A7:J7
        ImplicitIntersection.Resolve(range, 59, 10).Should().Be(new NumberValue(10)); // formula in col J
        ImplicitIntersection.Resolve(range, 59, 1).Should().Be(new NumberValue(1));   // col A
        ImplicitIntersection.Resolve(range, 59, 11).Should().Be(ErrorValue.Value);    // off-axis
    }

    [Fact]
    public void SingleColumnRange_MatchesFormulaRow()
    {
        var range = ColRange(7, 7, 70, 80, 90, 100); // G7:G10
        ImplicitIntersection.Resolve(range, 8, 12).Should().Be(new NumberValue(80));  // formula in row 8
        ImplicitIntersection.Resolve(range, 10, 12).Should().Be(new NumberValue(100));
        ImplicitIntersection.Resolve(range, 11, 12).Should().Be(ErrorValue.Value);    // off-axis
    }

    [Fact]
    public void SingleCell_AlwaysResolvesRegardlessOfPosition()
    {
        var range = new RangeValue(new ScalarValue[1, 1] { { new NumberValue(42) } }, 3, 3);
        ImplicitIntersection.Resolve(range, 100, 100).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void TwoDimensionalRange_MatchesBothAxes()
    {
        var cells = new ScalarValue[3, 3];
        for (var r = 0; r < 3; r++) for (var c = 0; c < 3; c++) cells[r, c] = new NumberValue(r * 10 + c);
        var range = new RangeValue(cells, 2, 2); // B2:D4
        ImplicitIntersection.Resolve(range, 3, 3).Should().Be(new NumberValue(11)); // (r1,c1) -> cells[1,1]
        ImplicitIntersection.Resolve(range, 4, 2).Should().Be(new NumberValue(20)); // cells[2,0]
        ImplicitIntersection.Resolve(range, 5, 3).Should().Be(ErrorValue.Value);    // row off-axis
        ImplicitIntersection.Resolve(range, 3, 5).Should().Be(ErrorValue.Value);    // col off-axis
    }

    // R80-formula-array-cse-5-1: the AST-aware Resolve(FormulaNode, ...) overload used by RecalcEngine for
    // a legacy (non-CSE) Implicit-mode formula. A computed/constant array with no reference operand
    // anywhere in the formula -- e.g. "={1,2,3}" -- has no worksheet position to intersect against: its
    // RangeValue defaults StartRow=1/StartCol=1 (see ScalarValue.cs / EvaluateArrayConstant), which can
    // coincidentally collide with the formula cell's own row/col. Excel always shows the top-left element
    // regardless of the formula cell's position.
    [Fact]
    public void ArrayConstantFormula_ResolvesToTopLeft_RegardlessOfFormulaCellPosition()
    {
        var cells = new ScalarValue[1, 3] { { new NumberValue(1), new NumberValue(2), new NumberValue(3) } };
        var range = new RangeValue(cells, 1, 1); // IsSheetReference left false (default)
        var arrayConstantFormula = new ArrayConstantNode(new[] { new FormulaNode[] { new NumberNode(1), new NumberNode(2), new NumberNode(3) } });

        // Formula cell C1 (row 1, col 3): coincidentally collides with cells[0,2] == 3 under naive
        // coordinate intersection, but Excel (and now FreeX) shows the top-left element, 1.
        ImplicitIntersection.Resolve(arrayConstantFormula, range, 1, 3).Should().Be(new NumberValue(1));

        // Formula cell D1 (row 1, col 4): naive coordinate intersection would be out of range (#VALUE!),
        // but Excel still shows the top-left element, 1, regardless of the formula's position.
        ImplicitIntersection.Resolve(arrayConstantFormula, range, 1, 4).Should().Be(new NumberValue(1));
    }

    // No-regression sibling: when the formula's AST DOES contain a genuine reference (even nested inside
    // arithmetic, e.g. "=A7:J7*B15"), the resulting computed RangeValue's coordinate frame is inherited
    // from that real reference, so positional row/col intersection must still apply -- matching classic
    // Excel's automatic implicit intersection (and ImplicitIntersectionEvalTests's equivalent end-to-end
    // coverage). This must not regress to top-left-always just because the final RangeValue is a
    // synthesized/computed result (IsSheetReference == false after arithmetic broadcast).
    [Fact]
    public void FormulaContainingReference_StillUsesPositionalIntersection_NotTopLeft()
    {
        var range = RowRange(1, 1, 1, 2, 3); // e.g. the computed result of "=A1:C1*1", positioned at A1:C1
        var referenceContainingFormula = new BinaryOpNode(
            new RangeRefNode(new CellRefNode("A", 1), new CellRefNode("C", 1)),
            BinaryOperator.Multiply,
            new NumberNode(1));

        ImplicitIntersection.Resolve(referenceContainingFormula, range, 1, 3).Should().Be(new NumberValue(3)); // col C -> third element
        ImplicitIntersection.Resolve(referenceContainingFormula, range, 1, 1).Should().Be(new NumberValue(1)); // col A -> first element
        ImplicitIntersection.Resolve(referenceContainingFormula, range, 1, 4).Should().Be(ErrorValue.Value);   // off-axis -> #VALUE!
    }
}
