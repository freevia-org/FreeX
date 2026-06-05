using FreeX.Core.Calc;
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
}
