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
    public void AutomaticMode_UsesSquareRootBinCountAndCountsEveryValue()
    {
        var bins = HistogramBinPlanner.Compute(OneToTen, new HistogramBinningModel());

        // sqrt(10) -> ceil = 4 bins.
        bins.Should().HaveCount(4);
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
        bins.Single(b => b.Kind == HistogramBinKind.Normal && b == bins[1]).Label.Should().Contain("–");
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
