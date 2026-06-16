using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    // CHOOSE with an array index_num and array/vector branches broadcasts: a 1xN index against
    // M-row column-vector branches produces an MxN array (the "stack columns" idiom that UNIQUE
    // and other dynamic-array functions rely on). Regression for contextures file 06
    // Spill Formulae!H29 = UNIQUE(CHOOSE({1,2},C5:C11,E5:E11)).
    [Fact]
    public void Choose_RowVectorIndexWithColumnBranches_StacksColumnsIntoMatrix()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(100)),
            (2, 1, new NumberValue(20)), (2, 2, new NumberValue(200)),
            (3, 1, new NumberValue(30)), (3, 2, new NumberValue(300)));

        // CHOOSE({1,2}, A1:A3, B1:B3) → 3x2 matrix: col 1 = A1:A3, col 2 = B1:B3.
        var result = _eval.Evaluate("=CHOOSE({1,2},A1:A3,B1:B3)", sheet)
            .Should().BeOfType<RangeValue>("an array index over column-vector branches must broadcast, not error").Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(10));
        result.Cells[1, 0].Should().Be(new NumberValue(20));
        result.Cells[2, 0].Should().Be(new NumberValue(30));
        result.Cells[0, 1].Should().Be(new NumberValue(100));
        result.Cells[1, 1].Should().Be(new NumberValue(200));
        result.Cells[2, 1].Should().Be(new NumberValue(300));
    }

    [Fact]
    public void Unique_OverChooseStackedColumns_DeduplicatesRows()
    {
        // Mirrors Spill Formulae!H29. Column data has a duplicate row (row 1 == row 4).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(11)),
            (2, 1, new NumberValue(12)), (2, 2, new NumberValue(13)),
            (3, 1, new NumberValue(10)), (3, 2, new NumberValue(11)));

        var result = _eval.Evaluate("=UNIQUE(CHOOSE({1,2},A1:A3,B1:B3))", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.ColCount.Should().Be(2);
        result.RowCount.Should().Be(2, "the (10,11) row appears twice and collapses to one");
        result.Cells[0, 0].Should().Be(new NumberValue(10));
        result.Cells[0, 1].Should().Be(new NumberValue(11));
        result.Cells[1, 0].Should().Be(new NumberValue(12));
        result.Cells[1, 1].Should().Be(new NumberValue(13));
    }
}
