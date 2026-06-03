using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Large_FirstLargest()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        _eval.Evaluate("=LARGE(A1:A4,1)", sheet).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Large_Small_And_Rank_TreatScalarArraysAsSingleItemArrays()
    {
        _eval.Evaluate("=LARGE(5,1)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=SMALL(5,1)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=RANK(5,5)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Large_SecondLargest()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        _eval.Evaluate("=LARGE(A1:A4,2)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Large_DuplicateValues_CountEachOccurrence()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(8)),
            (2, 1, new NumberValue(8)),
            (3, 1, new NumberValue(5)));

        _eval.Evaluate("=LARGE(A1:A3,2)", sheet).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Large_KRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        AssertColumn(_eval.Evaluate("=LARGE(A1:A4,B1:B2)", sheet), new NumberValue(8), new NumberValue(5));
    }

    [Fact]
    public void Large_OutOfRange_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)));
        _eval.Evaluate("=LARGE(A1:A1,5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Large_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(8)));
        _eval.Evaluate("=LARGE(A1:A3,1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Large_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=LARGE(NA(),1)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Large_DateTimeRange_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (2, 1, new NumberValue(10)));

        _eval.Evaluate("=LARGE(A1:A2,1)", sheet).Should().Be(new NumberValue(date.Value));
    }

    [Fact]
    public void Large_NonFiniteK_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=LARGE(A1:A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }


    [Fact]
    public void Small_FirstSmallest()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        _eval.Evaluate("=SMALL(A1:A4,1)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Small_DuplicateValues_CountEachOccurrence()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(1)));

        _eval.Evaluate("=SMALL(A1:A3,2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Small_OutOfRange_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)));
        _eval.Evaluate("=SMALL(A1:A1,5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Small_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(8)));
        _eval.Evaluate("=SMALL(A1:A3,1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Small_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=SMALL(NA(),1)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Small_NonFiniteK_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=SMALL(A1:A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Small_KRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        AssertColumn(_eval.Evaluate("=SMALL(A1:A4,B1:B2)", sheet), new NumberValue(1), new NumberValue(3));
    }

    [Fact]
    public void Sumproduct_DirectErrorArgument_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SUMPRODUCT(NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumproduct_NonnumericRangeEntries_AreTreatedAsZero()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("5")),
            (2, 1, new TextValue("x")),
            (1, 2, new NumberValue(10)),
            (2, 2, new NumberValue(20)));

        _eval.Evaluate("=SUMPRODUCT(A1:A2,B1:B2)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Sumproduct_DirectTextEntry_IsTreatedAsZero()
    {
        _eval.Evaluate("=SUMPRODUCT(\"5\",2)", MakeSheet()).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Sumproduct_DateTimeRangeEntry_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (1, 2, new NumberValue(2)));

        _eval.Evaluate("=SUMPRODUCT(A1:A1,B1:B1)", sheet).Should().Be(new NumberValue(date.Value * 2));
    }


    [Fact]
    public void Sumproduct_OverflowingProduct_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (1, 2, new NumberValue(1E308)));
        _eval.Evaluate("=SUMPRODUCT(A1:A1,B1:B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rank_DescendingOrder()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        // rank of 5 in descending = 2 (8>5>3>1)
        _eval.Evaluate("=RANK(5,A1:A4,0)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Rank_AscendingOrder()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        // rank of 5 in ascending = 3 (1<3<5<8)
        _eval.Evaluate("=RANK(5,A1:A4,1)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Rank_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(8)));
        _eval.Evaluate("=RANK(5,A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Rank_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=RANK(5,NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Rank_OrderError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(8)));
        _eval.Evaluate("=RANK(5,A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Rank_NonFiniteOrder_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (2, 1, new NumberValue(8)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=RANK(5,A1:A2,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rank_NonFiniteNumber_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (2, 1, new NumberValue(8)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=RANK(B1,A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rank_NumberAndOrderRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)),
            (1, 2, new NumberValue(5)),
            (2, 2, new NumberValue(5)),
            (1, 3, new NumberValue(0)),
            (2, 3, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=RANK(B1:B2,A1:A4,C1:C2)", sheet), new NumberValue(2), new NumberValue(3));
    }


    [Fact]
    public void Stdev_SampleStdDev()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(4)),
            (3, 1, new NumberValue(4)),
            (4, 1, new NumberValue(4)),
            (5, 1, new NumberValue(5)),
            (6, 1, new NumberValue(5)),
            (7, 1, new NumberValue(7)),
            (8, 1, new NumberValue(9)));
        // Sample stddev of {2,4,4,4,5,5,7,9} ≈ 2.138
        var result = _eval.Evaluate("=STDEV(A1:A8)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(2.138, 0.001);
    }


    [Fact]
    public void Stdev_OverflowingVariance_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(-1E308)));
        _eval.Evaluate("=STDEV(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Stdev_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(6)));
        _eval.Evaluate("=STDEV(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Stdev_DirectLogical_IncludesValue()
    {
        ((NumberValue)_eval.Evaluate("=STDEV(TRUE,3)", MakeSheet())).Value
            .Should().BeApproximately(Math.Sqrt(2), 1e-10);
    }

    [Fact]
    public void Stdev_ReferencedLogical_IgnoresValue()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new NumberValue(3)));

        _eval.Evaluate("=STDEV(A1:A2)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Median_OddCount()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(5)));
        _eval.Evaluate("=MEDIAN(A1:A3)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Median_EvenCount_AveragesMiddle()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)));
        _eval.Evaluate("=MEDIAN(A1:A4)", sheet).Should().Be(new NumberValue(2.5));
    }

    [Fact]
    public void Median_OverflowingAverage_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=MEDIAN(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Median_DirectLogical_IncludesValue()
    {
        _eval.Evaluate("=MEDIAN(TRUE,3)", MakeSheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Median_DirectNumericText_IncludesValue()
    {
        _eval.Evaluate("=MEDIAN(\"4\",2)", MakeSheet()).Should().Be(new NumberValue(3));
    }


    [Fact]
    public void Median_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=MEDIAN(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void VarS_ThreeValues_ReturnsSampleVariance()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        // mean=4, var.s = ((4+0+4)/2) = 4
        _eval.Evaluate("=VAR(A1:A3)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact] public void VarP_ThreeValues_ReturnsPopulationVariance()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        // mean=4, var.p = (4+0+4)/3 = 8/3
        ((NumberValue)_eval.Evaluate("=VAR.P(A1:A3)", sheet)).Value
            .Should().BeApproximately(8.0 / 3.0, 1e-10);
    }

    [Fact] public void VarP_DirectLogical_IncludesValue()
    {
        ((NumberValue)_eval.Evaluate("=VAR.P(TRUE,3)", MakeSheet())).Value
            .Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void VarP_OverflowingVariance_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(-1E308)));
        _eval.Evaluate("=VAR.P(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void StdevP_ThreeValues_ReturnsStdDev()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        ((NumberValue)_eval.Evaluate("=STDEV.P(A1:A3)", sheet)).Value
            .Should().BeApproximately(Math.Sqrt(8.0 / 3.0), 1e-10);
    }

    [Fact]
    public void StdevP_OverflowingVariance_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(-1E308)));
        _eval.Evaluate("=STDEV.P(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Percentile_Median_Returns4()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        _eval.Evaluate("=PERCENTILE(A1:A3,0.5)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Percentile_And_Quartile_TreatScalarArraysAsSingleItemArrays()
    {
        _eval.Evaluate("=PERCENTILE(5,0)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=PERCENTILE(5,1)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=QUARTILE(5,0)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=QUARTILE(5,4)", MakeSheet()).Should().Be(new NumberValue(5));
    }

    [Fact] public void Percentile_RangeError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,ErrorValue.NA),(3,1,new NumberValue(6)));
        _eval.Evaluate("=PERCENTILE(A1:A3,0.5)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentile_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=PERCENTILE(NA(),0.5)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Percentile_OverflowingInterpolation_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(-1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=PERCENTILE(A1:A2,0.5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentile_NonFiniteK_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=PERCENTILE(A1:A2,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentile_KRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)), (4, 1, new NumberValue(4)),
            (1, 2, new NumberValue(0)), (2, 2, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=PERCENTILE(A1:A4,B1:B2)", sheet), new NumberValue(1), new NumberValue(4));
    }

    [Fact] public void PercentileExc_Middle_ReturnsInterpolated()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        // PERCENTILE.EXC([1,2,3,4], 0.4): rank = 0.4*5-1 = 1, index 1 → value 2
        _eval.Evaluate("=PERCENTILE.EXC(A1:A4,0.4)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void PercentileExc_RangeError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        _eval.Evaluate("=PERCENTILE.EXC(A1:A4,0.4)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void PercentileExc_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=PERCENTILE.EXC(NA(),0.4)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void PercentileExc_KRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)), (4, 1, new NumberValue(4)),
            (1, 2, new NumberValue(0.4)), (2, 2, new NumberValue(0.6)));

        AssertColumn(_eval.Evaluate("=PERCENTILE.EXC(A1:A4,B1:B2)", sheet), new NumberValue(2), new NumberValue(3));
    }

    [Fact]
    public void ExponDist_LeadingOneCellLambdaRange_BroadcastsAcrossXArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (1, 2, new NumberValue(0.5)));

        AssertApproxColumn(
            _eval.Evaluate("=EXPON.DIST(A1:A2,B1:B1,FALSE)", sheet),
            ((NumberValue)_eval.Evaluate("=EXPON.DIST(A1,B1,FALSE)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=EXPON.DIST(A2,B1,FALSE)", sheet)).Value);
    }

    [Fact]
    public void NormSDist_LeadingOneCellCumulativeRange_BroadcastsAcrossZArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1)),
            (2, 1, new NumberValue(1)),
            (1, 2, new BoolValue(true)));

        AssertApproxColumn(
            _eval.Evaluate("=NORM.S.DIST(A1:A2,B1:B1)", sheet),
            ((NumberValue)_eval.Evaluate("=NORM.S.DIST(A1,B1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=NORM.S.DIST(A2,B1)", sheet)).Value);
    }

    [Fact] public void Quartile_Q1_Returns25th()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        // QUARTILE([1,2,3,4], 1) = 25th percentile = 1.75
        ((NumberValue)_eval.Evaluate("=QUARTILE(A1:A4,1)", sheet)).Value
            .Should().BeApproximately(1.75, 1e-10);
    }

    [Fact] public void Quartile_RangeError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        _eval.Evaluate("=QUARTILE(A1:A4,1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Quartile_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=QUARTILE(NA(),1)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Quartile_OverflowingInterpolation_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(-1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=QUARTILE(A1:A2,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Quartile_NonFiniteQuart_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=QUARTILE(A1:A2,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Quartile_QuartRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)), (4, 1, new NumberValue(4)),
            (1, 2, new NumberValue(0)), (2, 2, new NumberValue(4)));

        AssertColumn(_eval.Evaluate("=QUARTILE(A1:A4,B1:B2)", sheet), new NumberValue(1), new NumberValue(4));
    }

    [Fact] public void Geomean_TwoNumbers_ReturnsGeometricMean()
    {
        var sheet = MakeSheet((1,1,new NumberValue(4)),(2,1,new NumberValue(9)));
        // geomean(4,9) = sqrt(36) = 6
        _eval.Evaluate("=GEOMEAN(A1:A2)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact] public void Geomean_DirectLogical_IncludesValue()
    {
        _eval.Evaluate("=GEOMEAN(TRUE,4)", MakeSheet()).Should().Be(new NumberValue(2));
    }

    [Fact] public void Geomean_ReferencedLogical_IgnoresValue()
    {
        var sheet = MakeSheet((1,1,new BoolValue(true)),(2,1,new NumberValue(4)));
        _eval.Evaluate("=GEOMEAN(A1:A2)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact] public void Harmean_TwoNumbers_ReturnsHarmonicMean()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(4)));
        // harmean(1,4) = 2/(1+0.25) = 1.6
        ((NumberValue)_eval.Evaluate("=HARMEAN(A1:A2)", sheet)).Value
            .Should().BeApproximately(1.6, 1e-10);
    }

    [Fact] public void Harmean_DirectLogical_IncludesValue()
    {
        ((NumberValue)_eval.Evaluate("=HARMEAN(TRUE,4)", MakeSheet())).Value
            .Should().BeApproximately(1.6, 1e-10);
    }

    [Fact] public void Avedev_ThreeValues_ReturnsAvgAbsDev()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        // mean=4, deviations=2,0,2 → avg=4/3
        ((NumberValue)_eval.Evaluate("=AVEDEV(A1:A3)", sheet)).Value
            .Should().BeApproximately(4.0 / 3.0, 1e-10);
    }

    [Fact] public void Avedev_DirectNumericText_IncludesValue()
    {
        ((NumberValue)_eval.Evaluate("=AVEDEV(\"1\",3)", MakeSheet())).Value
            .Should().BeApproximately(1, 1e-10);
    }

    [Fact]
    public void Avedev_OverflowingDeviation_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(-1E308)));
        _eval.Evaluate("=AVEDEV(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Avedev_ReferencedLogical_IgnoresValue()
    {
        var sheet = MakeSheet((1,1,new BoolValue(true)),(2,1,new NumberValue(3)));
        _eval.Evaluate("=AVEDEV(A1:A2)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact] public void Mode_ReturnsValueWithHighestFrequency()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(2)),(4,1,new NumberValue(3)));
        _eval.Evaluate("=MODE(A1:A4)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void Mode_DirectLogical_IncludesValue()
    {
        _eval.Evaluate("=MODE(TRUE,TRUE,2)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact] public void Mode_DirectNumericText_IncludesValue()
    {
        _eval.Evaluate("=MODE(\"2\",2,3)", MakeSheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Mode_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=MODE(\"1E309\",\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Mode_AllUnique_ReturnsNA()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)));
        _eval.Evaluate("=MODE(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentrank_FindsRank()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)),(5,1,new NumberValue(5)));
        // PERCENTRANK([1..5], 3) = 0.5
        _eval.Evaluate("=PERCENTRANK(A1:A5,3)", sheet).Should().Be(new NumberValue(0.5));
    }

    [Fact] public void Percentrank_InterpolatesWhenValueNotInArray()
    {
        // Excel PERCENTRANK interpolates between adjacent values when x is not an
        // exact array member but falls between min and max. For [1,2,3,4,5], the rank
        // of 3.5 is halfway between rank(3)=0.5 and rank(4)=0.75, i.e. 0.625
        // (truncated to 3 significant digits → 0.625).
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (4,1,new NumberValue(4)),(5,1,new NumberValue(5)));
        _eval.Evaluate("=PERCENTRANK(A1:A5,3.5)", sheet).Should().Be(new NumberValue(0.625));
    }

    [Fact] public void Percentrank_OutsideRange_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)));
        _eval.Evaluate("=PERCENTRANK(A1:A3,10)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentrank_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)),(5,1,new NumberValue(5)));
        _eval.Evaluate("=PERCENTRANK(A1:A5,3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentrank_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=PERCENTRANK(NA(),3)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentrank_SignificanceError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)));
        _eval.Evaluate("=PERCENTRANK(A1:A3,2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Percentrank_OverflowingSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)));
        _eval.Evaluate("=PERCENTRANK(A1:A3,2,400)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentrank_NonFiniteX_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=PERCENTRANK(A1:A2,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentrank_NonFiniteSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=PERCENTRANK(A1:A2,1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentrank_XAndSignificanceRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)), (5, 1, new NumberValue(5)),
            (1, 2, new NumberValue(2)), (2, 2, new NumberValue(4)),
            (1, 3, new NumberValue(3)), (2, 3, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=PERCENTRANK(A1:A5,B1:B2,C1:C2)", sheet), new NumberValue(0.25), new NumberValue(0.75));
    }

    [Fact] public void Correl_PerfectPositive_Returns1()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        ((NumberValue)_eval.Evaluate("=CORREL(A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(1.0, 1e-10);
    }

    [Fact] public void Correl_IgnoresNonnumericPairs()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(2)),(2,1,new TextValue("x")),(3,1,new NumberValue(6)),
            (1,2,new NumberValue(1)),(2,2,new NumberValue(2)),(3,2,new NumberValue(3)));
        ((NumberValue)_eval.Evaluate("=CORREL(A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(1.0, 1e-10);
    }

    [Fact] public void Correl_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        _eval.Evaluate("=CORREL(A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Correl_FirstRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)));
        _eval.Evaluate("=CORREL(NA(),A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Correl_SecondRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)));
        _eval.Evaluate("=CORREL(A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Correl_OverflowingVariance_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1E308)), (2, 1, new NumberValue(1E308)),
            (1, 2, new NumberValue(-1E308)), (2, 2, new NumberValue(1E308)));

        _eval.Evaluate("=CORREL(A1:A2,B1:B2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Forecast_LinearTrend_PredictsCorrectly()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        // FORECAST(8, known_y=A1:A3=[1,2,3], known_x=B1:B3=[2,4,6]) → predict y at x=8 → 4
        ((NumberValue)_eval.Evaluate("=FORECAST(8,A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(4.0, 1e-10);
    }

    [Fact] public void Forecast_IgnoresNonnumericPairs()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(2)),(2,1,new TextValue("x")),(3,1,new NumberValue(6)),
            (1,2,new NumberValue(1)),(2,2,new NumberValue(2)),(3,2,new NumberValue(3)));
        ((NumberValue)_eval.Evaluate("=FORECAST.LINEAR(4,A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(8.0, 1e-10);
    }

    [Fact] public void Forecast_KnownYRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        _eval.Evaluate("=FORECAST(8,A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Forecast_KnownXRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,ErrorValue.NA),(3,2,new NumberValue(6)));
        _eval.Evaluate("=FORECAST(8,A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Forecast_KnownYArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(2)), (2, 1, new NumberValue(4)));
        _eval.Evaluate("=FORECAST(8,NA(),A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Forecast_KnownXArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)));
        _eval.Evaluate("=FORECAST(8,A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }


    [Fact]
    public void Forecast_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(2)), (2, 2, new NumberValue(4)), (3, 2, new NumberValue(6)),
            (4, 1, new TextValue("1E309")));

        _eval.Evaluate("=FORECAST(A4,A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Subtotal_FuncNum9_Sum_NoHiddenRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);
        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Subtotal_FuncNumOneCellRange_IsScalarized()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(9)),
            (1, 2, new NumberValue(10)),
            (2, 2, new NumberValue(20)));

        _eval.Evaluate("=SUBTOTAL(A1:A1,B1:B2)", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Subtotal_FuncNum1_Average_NoHiddenRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        var result = _eval.Evaluate("=SUBTOTAL(1,A1:A3)", sheet);
        result.Should().Be(new NumberValue(20));
    }

    [Theory]
    [InlineData(2, 8d)]
    [InlineData(6, 201_600d)]
    [InlineData(7, 2.138089935299395d)]
    [InlineData(8, 2d)]
    [InlineData(10, 4.571428571428571d)]
    [InlineData(11, 4d)]
    public void Subtotal_StatisticalAndProductFunctions_ReturnExpectedNumericResults(int functionNumber, double expected)
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(4)),
            (3, 1, new NumberValue(4)),
            (4, 1, new NumberValue(4)),
            (5, 1, new NumberValue(5)),
            (6, 1, new NumberValue(5)),
            (7, 1, new NumberValue(7)),
            (8, 1, new NumberValue(9)));

        var result = _eval.Evaluate($"=SUBTOTAL({functionNumber},A1:A8)", sheet);

        result.Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(expected, 1e-12);
    }

    [Fact]
    public void Subtotal_FuncNum4_Max_NoHiddenRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        var result = _eval.Evaluate("=SUBTOTAL(4,A1:A3)", sheet);
        result.Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Subtotal_FuncNum5_Min_NoHiddenRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        var result = _eval.Evaluate("=SUBTOTAL(5,A1:A3)", sheet);
        result.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Subtotal_FuncNum109_SumExcludesGroupHiddenRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        // Mark row 2 as group-hidden
        sheet.GroupHiddenRows.Add(2);
        var result = _eval.Evaluate("=SUBTOTAL(109,A1:A3)", sheet);
        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum9_IncludesGroupHiddenRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        // With funcNum=9 (not 109), hidden rows are NOT excluded
        sheet.GroupHiddenRows.Add(2);
        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);
        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Subtotal_FuncNum9_ExcludesFilterHiddenRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        sheet.FilterHiddenRows.Add(2);

        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);

        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum9_IncludesManualHiddenRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        sheet.HiddenRows.Add(2);

        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);

        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Subtotal_FuncNum9_IgnoresNestedSubtotalFormulaCell()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(10)
        });

        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);

        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum9_ExcludesFilterHiddenRowsOnReferencedSheet()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(10));
        data.SetCell(new CellAddress(data.Id, 2, 1), new NumberValue(20));
        data.SetCell(new CellAddress(data.Id, 3, 1), new NumberValue(30));
        data.FilterHiddenRows.Add(2);

        var result = _eval.Evaluate("=SUBTOTAL(9,Data!A1:A3)", sheet, wb);

        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum3_CountaIncludesTextCells()
    {
        // COUNTA should count text cells too, not just numbers
        var sheet = MakeSheet(
            (1, 1, new TextValue("hello")),
            (2, 1, new NumberValue(42)));
        // row 3 is blank (not set)
        var result = _eval.Evaluate("=SUBTOTAL(3,A1:A3)", sheet);
        result.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Subtotal_FuncNum4_EmptyRange_ReturnsDivByZero()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=SUBTOTAL(4,B1:B3)", sheet);
        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Subtotal_SumRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(30)));

        _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Subtotal_CountaRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("hello")),
            (2, 1, ErrorValue.NA));

        _eval.Evaluate("=SUBTOTAL(3,A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Subtotal_ExcludedHiddenRowError_IsIgnored()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(30)));
        sheet.GroupHiddenRows.Add(2);

        _eval.Evaluate("=SUBTOTAL(109,A1:A3)", sheet).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_SumDateRange_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (2, 1, new NumberValue(10)));

        _eval.Evaluate("=SUBTOTAL(9,A1:A2)", sheet).Should().Be(new NumberValue(date.Value + 10));
    }

    [Fact]
    public void Subtotal_OverflowingSum_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=SUBTOTAL(9,A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Subtotal_OverflowingAverage_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=SUBTOTAL(1,A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Subtotal_OverflowingProduct_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=SUBTOTAL(6,A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(11)]
    public void Subtotal_OverflowingStatisticalFunction_PreservesNumError(int functionNumber)
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));

        _eval.Evaluate($"=SUBTOTAL({functionNumber},A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }
}
