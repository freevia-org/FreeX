using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// P2 regression: Excel's YEARFRAC always returns a non-negative fraction
/// regardless of which of start_date/end_date is later. FreeX previously fed
/// the un-swapped (start, end) pair into each basis's day-count math, which
/// yielded a negative result whenever start &gt; end. Verifies YEARFRAC(a,b)
/// == YEARFRAC(b,a) (both positive) across every basis.
/// </summary>
public class YearfracReversedRangeTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static double Yearfrac(FormulaEvaluator eval, DateTime start, DateTime end, int basis)
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(start.ToOADate())),
            (1, 2, new NumberValue(end.ToOADate())));
        var result = eval.Evaluate($"=YEARFRAC(A1,B1,{basis})", sheet);
        result.Should().BeOfType<NumberValue>();
        return ((NumberValue)result).Value;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Yearfrac_ReversedArguments_MatchForwardArguments_AllBases(int basis)
    {
        var earlier = new DateTime(2022, 1, 1);
        var later = new DateTime(2024, 1, 1);

        double forward = Yearfrac(_eval, earlier, later, basis);
        double reversed = Yearfrac(_eval, later, earlier, basis);

        double.IsFinite(forward).Should().BeTrue();
        forward.Should().BeGreaterThan(0);
        reversed.Should().BeApproximately(forward, 1e-9);
    }

    [Fact]
    public void Yearfrac_Basis1_ReversedRange_ReturnsPositiveFiniteValue()
    {
        double value = Yearfrac(_eval, new DateTime(2024, 1, 1), new DateTime(2022, 1, 1), basis: 1);
        double.IsFinite(value).Should().BeTrue();
        value.Should().BeApproximately(2.0, 0.05);
    }

    [Fact]
    public void Yearfrac_Basis0_ReversedRange_ReturnsPositiveFiniteValue()
    {
        double value = Yearfrac(_eval, new DateTime(2024, 6, 15), new DateTime(2024, 1, 15), basis: 0);
        double.IsFinite(value).Should().BeTrue();
        value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Yearfrac_Basis2_ReversedRange_ReturnsPositiveFiniteValue()
    {
        double value = Yearfrac(_eval, new DateTime(2024, 6, 15), new DateTime(2024, 1, 15), basis: 2);
        double.IsFinite(value).Should().BeTrue();
        value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Yearfrac_Basis3_ReversedRange_ReturnsPositiveFiniteValue()
    {
        double value = Yearfrac(_eval, new DateTime(2024, 6, 15), new DateTime(2024, 1, 15), basis: 3);
        double.IsFinite(value).Should().BeTrue();
        value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Yearfrac_Basis4_ReversedRange_ReturnsPositiveFiniteValue()
    {
        double value = Yearfrac(_eval, new DateTime(2024, 6, 15), new DateTime(2024, 1, 15), basis: 4);
        double.IsFinite(value).Should().BeTrue();
        value.Should().BeGreaterThan(0);
    }
}
