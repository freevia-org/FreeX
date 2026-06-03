using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void DynamicArrayFunctions_AcceptSpilledScalarControlArguments()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(1)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)), (2,3,new NumberValue(3)),
            (1,4,new NumberValue(5)), (1,5,new NumberValue(6)), (1,6,new NumberValue(7)));

        var toCol = _eval.Evaluate("=TOCOL(A1:B2,,SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        toCol.Cells[0, 0].Should().Be(new NumberValue(1));
        toCol.Cells[1, 0].Should().Be(new NumberValue(3));
        toCol.Cells[2, 0].Should().Be(new NumberValue(2));
        toCol.Cells[3, 0].Should().Be(new NumberValue(4));

        var wrapped = _eval.Evaluate("=WRAPROWS(D1:F1,SEQUENCE(1,,2))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        wrapped.RowCount.Should().Be(2);
        wrapped.ColCount.Should().Be(2);
        wrapped.Cells[1, 0].Should().Be(new NumberValue(7));

        var expanded = _eval.Evaluate("=EXPAND(A1:A1,SEQUENCE(1,,2),SEQUENCE(1,,2),0)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        expanded.RowCount.Should().Be(2);
        expanded.ColCount.Should().Be(2);
        expanded.Cells[1, 1].Should().Be(new NumberValue(0));

        var uniqueByColumn = _eval.Evaluate("=UNIQUE(A1:C2,SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        uniqueByColumn.RowCount.Should().Be(2);
        uniqueByColumn.ColCount.Should().Be(2);
        uniqueByColumn.Cells[0, 0].Should().Be(new NumberValue(1));
        uniqueByColumn.Cells[0, 1].Should().Be(new NumberValue(2));
    }
}
