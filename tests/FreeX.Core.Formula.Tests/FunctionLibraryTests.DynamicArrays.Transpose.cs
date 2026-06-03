using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Transpose_Range_ReturnsTransposedMatrix()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));

        var result = _eval.Evaluate("=TRANSPOSE(A1:C2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.At(1, 1).Should().Be(new NumberValue(1));
        rv.At(1, 2).Should().Be(new NumberValue(4));
        rv.At(3, 1).Should().Be(new NumberValue(3));
        rv.At(3, 2).Should().Be(new NumberValue(6));
    }
}
