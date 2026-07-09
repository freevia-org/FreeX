using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-20 statistical-function findings:
/// - R20-statistical-functions-1: RANK.AVG must accept a single-cell reference argument
///   (like RANK.EQ does) instead of returning #VALUE!.
/// - R20-statistical-functions-2: HYPERGEOM.DIST must enforce the documented lower-bound
///   domain check and return #NUM! for impossible sample_s values.
/// - R20-statistical-functions-3: FREQUENCY must bin positionally in the order the
///   bins_array was supplied, not after sorting it.
/// </summary>
public class R20_statistical_Tests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void RankAvg_SingleCellReferenceArgument_ReturnsOne()
    {
        // A1 = 5. Sole value in the "range" -> trivially rank 1, matching RANK.EQ's behavior
        // for the identical scenario (RANK.EQ(5,A1,0) already returns 1 pre-fix).
        var sheet = MakeSheet((1, 1, new NumberValue(5)));

        _eval.Evaluate("=RANK.AVG(5,A1,0)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void RankAvg_SingleCellReferenceArgument_MatchesRankEqForSameInput()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)));

        var rankEq = _eval.Evaluate("=RANK.EQ(5,A1,0)", sheet);
        var rankAvg = _eval.Evaluate("=RANK.AVG(5,A1,0)", sheet);

        rankEq.Should().Be(new NumberValue(1));
        rankAvg.Should().Be(rankEq);
    }

    [Fact]
    public void HypergeomDist_ImpossibleSampleSuccesses_ReturnsNumError()
    {
        // sample_size=9, population_successes=2, population_size=10 -> population has only
        // 8 non-successes, so drawing 9 items with 0 successes is impossible.
        // sample_s must be >= max(0, sample_size - population_size + population_successes) = 1.
        _eval.Evaluate("=HYPERGEOM.DIST(0,9,2,10,FALSE)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void HypergeomDist_ValidSampleSuccessesAtLowerBound_ReturnsProbability()
    {
        // sample_s = 1 is the smallest valid value for this scenario and should compute
        // a normal (non-error) probability.
        var result = _eval.Evaluate("=HYPERGEOM.DIST(1,9,2,10,FALSE)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
    }

    [Fact]
    public void Frequency_UnsortedBinsArray_BinsPositionallyWithoutSorting()
    {
        // data = {5, 15, 25}; bins supplied out of order as {20, 10} (B1=20, B2=10).
        // Excel does not sort bins_array — it bins positionally in the order supplied.
        // Data value 5 and 15 are both <= bins[0]=20 so land in bin 0; 25 exceeds both
        // bins[0]=20 and bins[1]=10 so it falls into the overflow bin.
        // Expected column: {2, 0, 1} (NOT the sorted-bins result of {1, 1, 1}).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(15)),
            (3, 1, new NumberValue(25)),
            (1, 2, new NumberValue(20)),
            (2, 2, new NumberValue(10)));

        var result = _eval.Evaluate("=FREQUENCY(A1:A3,B1:B2)", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.ColCount.Should().Be(1);
        range.At(1, 1).Should().Be(new NumberValue(2));
        range.At(2, 1).Should().Be(new NumberValue(0));
        range.At(3, 1).Should().Be(new NumberValue(1));
    }
}
