using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R116-precision: FreeX rounds every arithmetic binary result (+,-,*,/,^) to 15 significant
/// decimal digits (FormulaEvaluator.Operators.cs RoundTo15SignificantDigits, see
/// R77_ArithmeticResult15SigRoundingTests), and SUM/SUMPRODUCT were explicitly patched to apply
/// the same rounding to their accumulated total (BuiltInFunctions.MathCore.Aggregates.cs,
/// BuiltInFunctions.StatisticalCore.Aggregates.cs, FormulaEvaluator.FastAggregates.cs) so that
/// SUM(range) stays interchangeable with the textually-expanded chain of + over the same cells.
///
/// AVERAGE/AVERAGEA, PRODUCT, and the STDEV/VAR/VARA/VARP/VARPA/DEVSQ family never received this
/// fix -- neither their accumulated total/sum-of-squares nor their final quotient/mean was
/// rounded, so e.g. AVERAGE(0.1,0.1,0.1) returned the raw double 0.10000000000000002 instead of
/// exactly 0.1, diverging from Excel's documented 15-significant-digit precision (and from
/// FreeX's own SUM(0.1,0.1,0.1)/3, which already came out exactly 0.1 via Sum()'s existing fix
/// plus the '/' operator's own rounding).
///
/// Covers both the literal-argument slow path (BuiltInFunctions.StatisticalCore.Aggregates.cs
/// Average()/AverageA(), BuiltInFunctions.MathCore.Aggregates.cs Product(),
/// BuiltInFunctions.StatisticalCore.Variance.cs Stdev()/VarS()/VarA()/VarP()/VarPA()/Devsq()) and
/// the range-only fast-aggregate path for AVERAGE/STDEV/VAR
/// (FormulaEvaluator.FastAggregates.cs EvaluateFastRangeOnlyAverage/EvaluateFastRangeOnlyVariance),
/// which is an identical sibling of the already-rounded EvaluateFastRangeOnlySum in the same file
/// and was the odd one out.
/// </summary>
public sealed class R116_AggregateFunctions15SigRoundingTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook Workbook, Sheet Sheet) MakeWb(params (uint row, uint col, ScalarValue val)[] cells)
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, r, c), v);
        return (wb, sheet);
    }

    [Fact]
    public void Average_LiteralArgs_RoundsToExactlyPointOne()
    {
        // Raw double arithmetic: 0.1+0.1+0.1 == 0.30000000000000004, and that total/3 ==
        // 0.10000000000000002 -- not 0.1. Excel (and FreeX's SUM(0.1,0.1,0.1)/3) evaluates
        // this to exactly 0.1.
        var sheet = MakeSheet();

        _eval.Evaluate("=AVERAGE(0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.1));
    }

    [Fact]
    public void Average_MinusPointOne_IsExactlyZero_MatchingSumDivideCount()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=AVERAGE(0.1,0.1,0.1)-0.1", sheet)
            .Should().Be(new NumberValue(0.0));

        // The manual equivalent already worked (Sum()'s existing rounding fix + the '/'
        // operator's own rounding); AVERAGE must now match it exactly.
        _eval.Evaluate("=SUM(0.1,0.1,0.1)/3", sheet)
            .Should().Be(new NumberValue(0.1));
    }

    [Fact]
    public void AverageA_LiteralArgs_RoundsToExactlyPointOne()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=AVERAGEA(0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.1));
    }

    [Fact]
    public void Average_RangeArgument_FastAggregatePath_RoundsToExactlyPointOne()
    {
        // A pure range reference (unlike literal scalar args above) routes through the
        // range-only fast-aggregate path (FormulaEvaluator.FastAggregates.cs
        // EvaluateFastRangeOnlyAverage), a structurally separate implementation from the
        // literal-args slow path exercised above -- it must be rounded too.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(0.1)),
            (2, 1, new NumberValue(0.1)),
            (3, 1, new NumberValue(0.1)));

        var result = _eval.Evaluate("=AVERAGE(A1:A3)", sheet, wb);

        result.Should().Be(new NumberValue(0.1));
    }

    [Fact]
    public void Product_LiteralArgs_RoundsResult()
    {
        // 0.1*0.1*0.1 as raw doubles is 0.0010000000000000002, not the shortest round-trip
        // value 0.001. Excel rounds every arithmetic result (including PRODUCT's) to 15
        // significant digits.
        var sheet = MakeSheet();

        _eval.Evaluate("=PRODUCT(0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.001));
    }

    [Fact]
    public void VarP_IdenticalValues_LiteralArgs_IsExactlyZero()
    {
        // VARP of three identical values must be exactly 0 -- mathematically the variance of
        // a constant sample is 0. Without a rounded mean, computing mean = (0.1+0.1+0.1)/3 via
        // raw double arithmetic yields a mean that differs from 0.1 by ~1.4e-17, producing a
        // spurious nonzero sum-of-squared-deviations (~1.9e-34) instead of exactly 0.
        var sheet = MakeSheet();

        _eval.Evaluate("=VARP(0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.0));
    }

    [Fact]
    public void VarP_IdenticalValues_RangeArgument_FastAggregatePath_IsExactlyZero()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(0.1)),
            (2, 1, new NumberValue(0.1)),
            (3, 1, new NumberValue(0.1)));

        var result = _eval.Evaluate("=VARP(A1:A3)", sheet, wb);

        result.Should().Be(new NumberValue(0.0));
    }

    [Fact]
    public void Stdev_IdenticalValues_LiteralArgs_IsExactlyZero()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=STDEV(0.1,0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.0));
    }

    [Fact]
    public void Var_IdenticalValues_LiteralArgs_IsExactlyZero()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=VAR(0.1,0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.0));
    }

    [Fact]
    public void VarA_IdenticalValues_IsExactlyZero()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=VARA(0.1,0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.0));
    }

    [Fact]
    public void VarPA_IdenticalValues_IsExactlyZero()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=VARPA(0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.0));
    }

    [Fact]
    public void Devsq_IdenticalValues_IsExactlyZero()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=DEVSQ(0.1,0.1,0.1)", sheet)
            .Should().Be(new NumberValue(0.0));
    }

    // --- No-regression siblings: ordinary (non-near-round) aggregate results must be
    // unaffected by the new rounding calls. ---

    [Fact]
    public void Average_OrdinaryIntegers_IsUnaffectedByRounding()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=AVERAGE(1,2,3,4,5)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Product_OrdinaryIntegers_IsUnaffectedByRounding()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=PRODUCT(2,3,4)", sheet).Should().Be(new NumberValue(24));
    }

    [Fact]
    public void VarP_OrdinaryValues_IsUnaffectedByRounding()
    {
        // VARP(1,2,3) == mean 2, sum of squared deviations (1+0+1)=2, /3 == 0.6666666666666666.
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=VARP(1,2,3)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(2.0 / 3.0, 1e-12);
    }

    [Fact]
    public void Stdev_OrdinarySample_MatchesKnownValue()
    {
        // STDEV.S(2,4,4,4,5,5,7,9) is the textbook example with sample stdev == 2.138089935...
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=STDEV(2,4,4,4,5,5,7,9)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(2.13808993529939d, 1e-9);
    }

    [Fact]
    public void Average_EmptyArgs_StillReturnsDivByZero()
    {
        var (wb, sheet) = MakeWb();

        var result = _eval.Evaluate("=AVERAGE(A1)", sheet, wb);

        result.Should().Be(ErrorValue.DivByZero);
    }

    private static Sheet MakeSheet()
    {
        return new Sheet(SheetId.New(), "S");
    }
}
