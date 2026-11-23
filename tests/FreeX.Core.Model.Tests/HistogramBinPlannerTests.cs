using System.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed class HistogramBinPlannerTests
{
    private static readonly double[] OneToTen = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    [Fact]
    public void NoValues_ProducesNoBins()
    {
        HistogramBinPlanner.Compute([], new HistogramBinningModel())
            .Should().BeEmpty();
    }

    [Fact]
    public void BinCountMode_PartitionsRangeIntoRequestedEqualWidthBins()
    {
        var bins = HistogramBinPlanner.Compute(
            OneToTen,
            new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: 3));

        bins.Should().HaveCount(3);
        bins.All(b => b.Kind == HistogramBinKind.Normal).Should().BeTrue();
        // Range 1..10 split into 3 equal bins of width 3.
        bins[0].Min.Should().BeApproximately(1, 1e-9);
        bins[0].Max.Should().BeApproximately(4, 1e-9);
        bins[2].Max.Should().BeApproximately(10, 1e-9);
        // Every value is counted exactly once.
        bins.Sum(b => b.Count).Should().Be(OneToTen.Length);
        // The maximum value lands in the last bin, not a phantom overflow bin.
        bins[^1].Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BinWidthMode_DerivesBinCountFromWidthAndCoversTheMaximum()
    {
        var bins = HistogramBinPlanner.Compute(
            OneToTen,
            new HistogramBinningModel(HistogramBinningMode.BinWidth, BinWidth: 2.5));

        // Range 9 / width 2.5 -> ceil = 4 bins.
        bins.Should().HaveCount(4);
        bins[0].Min.Should().BeApproximately(1, 1e-9);
        bins.Sum(b => b.Count).Should().Be(OneToTen.Length);
        // Last bin must include the maximum value.
        bins[^1].Max.Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void AutomaticMode_UsesScottRuleAndCountsEveryValue()
    {
        var bins = HistogramBinPlanner.Compute(OneToTen, new HistogramBinningModel());

        // Excel (COM ChartGroup): Scott width 4.9, ceil(9 / 4.9) = 2 bins.
        bins.Should().HaveCount(2);
        bins.Sum(b => b.Count).Should().Be(OneToTen.Length);
    }

    [Fact]
    public void OverflowThreshold_CollectsValuesAboveThresholdIntoASingleOverflowBin()
    {
        var bins = HistogramBinPlanner.Compute(
            OneToTen,
            new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: 2, OverflowThreshold: 6));

        bins.Should().ContainSingle(b => b.Kind == HistogramBinKind.Overflow);
        var overflow = bins.Single(b => b.Kind == HistogramBinKind.Overflow);
        // Values strictly greater than 6: 7,8,9,10 -> 4.
        overflow.Count.Should().Be(4);
        overflow.Min.Should().BeApproximately(6, 1e-9);
        // Normal bins cover only up to the overflow threshold and count the remaining 6 values.
        bins.Where(b => b.Kind == HistogramBinKind.Normal).Sum(b => b.Count).Should().Be(6);
        bins.Last().Kind.Should().Be(HistogramBinKind.Overflow);
    }

    [Fact]
    public void UnderflowThreshold_CollectsValuesAtOrBelowThresholdIntoASingleUnderflowBin()
    {
        var bins = HistogramBinPlanner.Compute(
            OneToTen,
            new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: 2, UnderflowThreshold: 3));

        bins.First().Kind.Should().Be(HistogramBinKind.Underflow);
        var underflow = bins.First();
        // Values at or below 3: 1,2,3 -> 3.
        underflow.Count.Should().Be(3);
        underflow.Max.Should().BeApproximately(3, 1e-9);
        bins.Sum(b => b.Count).Should().Be(OneToTen.Length);
    }

    [Fact]
    public void OverflowAndUnderflow_PartitionValuesIntoThreeSectionsWithoutDoubleCounting()
    {
        var bins = HistogramBinPlanner.Compute(
            OneToTen,
            new HistogramBinningModel(
                HistogramBinningMode.BinCount,
                BinCount: 2,
                UnderflowThreshold: 2,
                OverflowThreshold: 8));

        bins.First().Kind.Should().Be(HistogramBinKind.Underflow);
        bins.Last().Kind.Should().Be(HistogramBinKind.Overflow);
        bins.First().Count.Should().Be(2);  // 1,2
        bins.Last().Count.Should().Be(2);   // 9,10
        bins.Where(b => b.Kind == HistogramBinKind.Normal).Sum(b => b.Count).Should().Be(6); // 3..8
        bins.Sum(b => b.Count).Should().Be(OneToTen.Length);
    }

    // Ground truth measured from real Excel (ChartGroup.BinWidthValue / BinsCountValue, automatic binning).
    [Theory]
    [InlineData(new double[] { 2, 3, 3, 4, 4, 4, 5, 5, 6, 7, 7, 8, 9, 9, 10, 11, 12, 12, 15, 18 }, 5.6, 3)]
    [InlineData(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 4.9, 2)]
    [InlineData(new double[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230, 240, 250 }, 88, 3)]
    [InlineData(new double[] { 0.1, 0.2, 0.25, 0.4, 0.5, 0.55, 0.9, 1.0, 1.4, 1.9, 2.2, 3.1 }, 1.4, 3)]
    [InlineData(new double[] { 1000, 1200, 1500, 1700, 2100, 2500, 3000, 3100, 4800, 9000, 12000, 15000 }, 7100, 2)]
    [InlineData(new double[] { 5, 5, 5, 5, 6, 6, 7 }, 1.4, 2)]
    [InlineData(new double[] { -5, -3, -2, 0, 1, 2, 4, 8, 9, 15, 22, 30, 31 }, 18, 2)]
    [InlineData(new double[] { 1, 50, 3, 77, 12, 34, 56, 89, 23, 45, 67, 90, 11, 13, 99, 100, 2, 4, 6, 8, 200, 150, 175, 60, 66, 70, 71, 72, 73, 74, 75 }, 56, 4)]
    public void AutomaticMode_MatchesExcelMeasuredBinWidthAndCount(double[] values, double excelWidth, int excelCount)
    {
        var bins = HistogramBinPlanner.Compute(values, new HistogramBinningModel());

        bins.Should().HaveCount(excelCount);
        (bins[0].Max - bins[0].Min).Should().BeApproximately(excelWidth, 1e-6);
        bins.Sum(b => b.Count).Should().Be(values.Length);
    }

    [Fact]
    public void AutomaticMode_UsesRightClosedBinsAndExcelLabels()
    {
        double[] values = [2, 3, 3, 4, 4, 4, 5, 5, 6, 7, 7, 8, 9, 9, 10, 11, 12, 12, 15, 18];
        var bins = HistogramBinPlanner.Compute(values, new HistogramBinningModel());

        // Excel renders [2, 7.6] / (7.6, 13.2] / (13.2, 18.8] with 11 / 7 / 2 values.
        bins.Select(b => b.Label).Should().Equal("[2, 7.6]", "(7.6, 13.2]", "(13.2, 18.8]");
        bins.Select(b => b.Count).Should().Equal(11, 7, 2);
    }

    [Fact]
    public void AllEqualValues_ProduceASingleBinHoldingEveryValue()
    {
        var bins = HistogramBinPlanner.Compute(
            [5, 5, 5, 5],
            new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: 4));

        bins.Should().HaveCount(1);
        bins[0].Count.Should().Be(4);
    }

    [Fact]
    public void Labels_DescribeNormalUnderflowAndOverflowBins()
    {
        var bins = HistogramBinPlanner.Compute(
            OneToTen,
            new HistogramBinningModel(
                HistogramBinningMode.BinCount,
                BinCount: 2,
                UnderflowThreshold: 2,
                OverflowThreshold: 8));

        bins.First().Label.Should().StartWith("≤");
        bins.Last().Label.Should().StartWith(">");
        bins.Single(b => b.Kind == HistogramBinKind.Normal && b == bins[1]).Label.Should().StartWith("(").And.EndWith("]");
    }

    [Fact]
    public void InvalidBinWidthOrCount_FallsBackToAutomatic()
    {
        // Non-positive width / count are ignored rather than throwing.
        var byWidth = HistogramBinPlanner.Compute(
            OneToTen, new HistogramBinningModel(HistogramBinningMode.BinWidth, BinWidth: 0));
        var byCount = HistogramBinPlanner.Compute(
            OneToTen, new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: 0));

        byWidth.Sum(b => b.Count).Should().Be(OneToTen.Length);
        byCount.Sum(b => b.Count).Should().Be(OneToTen.Length);
        byWidth.Should().NotBeEmpty();
        byCount.Should().NotBeEmpty();
    }
}
