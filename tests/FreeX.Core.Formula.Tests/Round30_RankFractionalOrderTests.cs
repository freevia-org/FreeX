using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R30-formula-statistical-inverse-3: RankScalar truncated a fractional, nonzero
// `order` argument (e.g. 0.5) to int BEFORE the order == 0 check, so any order
// strictly between -1 and 1 (but not exactly 0) was misclassified as the
// descending case. Excel treats any nonzero order as ascending.
public partial class FunctionLibraryTests
{
    [Fact]
    public void Rank_FractionalNonZeroOrder_UsesAscending()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)));
        // order=0.5 is nonzero => ascending; rank of 1 among {1,2,3,4} ascending = 1.
        _eval.Evaluate("=RANK.EQ(1,A1:A4,0.5)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Rank_ZeroOrder_StillUsesDescending()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)));
        // order=0 (the default/explicit) => descending; rank of 1 among {1,2,3,4} descending = 4.
        _eval.Evaluate("=RANK.EQ(1,A1:A4,0)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Rank_IntegerOneOrder_StillUsesAscending()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)));
        // order=1 (already-working integer case) => ascending; rank of 1 = 1.
        _eval.Evaluate("=RANK.EQ(1,A1:A4,1)", sheet).Should().Be(new NumberValue(1));
    }
}
