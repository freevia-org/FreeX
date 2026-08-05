using System.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed class WaterfallBarPlannerTests
{
    [Fact]
    public void Planner_IsOwnedBySharedDrawing()
    {
        typeof(WaterfallBarPlanner).Assembly.GetName().Name.Should().Be("Free.Shared.Drawing");
    }

    [Fact]
    public void NoValues_ProducesNoBars()
    {
        WaterfallBarPlanner.Compute(
            [],
            totalIndices: null,
            WaterfallNullTotalsPolicy.LastPointIsTotal).Should().BeEmpty();
    }

    [Fact]
    public void Increases_StackCumulativelyFromZero()
    {
        var bars = WaterfallBarPlanner.Compute(
            [10, 20, 30],
            totalIndices: [],
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars.Should().HaveCount(3);
        bars.Should().AllSatisfy(b => b.Kind.Should().Be(WaterfallBarKind.Increase));
        bars[0].Bottom.Should().Be(0);
        bars[0].Top.Should().Be(10);
        bars[1].Bottom.Should().Be(10);
        bars[1].Top.Should().Be(30);
        bars[2].Bottom.Should().Be(30);
        bars[2].Top.Should().Be(60);
    }

    [Fact]
    public void Decrease_DropsFromTheRunningTotalAndIsClassifiedAsDecrease()
    {
        var bars = WaterfallBarPlanner.Compute(
            [10, -4],
            totalIndices: [],
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars[1].Kind.Should().Be(WaterfallBarKind.Decrease);
        // The decrease bar spans the drop from 10 down to 6 (drawn low..high).
        bars[1].Bottom.Should().Be(6);
        bars[1].Top.Should().Be(10);
    }

    [Fact]
    public void TotalPoint_DrawsFromZeroToTheCumulativeRunningValueIgnoringItsOwnCellValue()
    {
        // Third point is flagged total; its own cell value (99) must be ignored — a total column
        // spans 0..(cumulative of the prior increases/decreases) = 0..30, NOT running+99.
        var bars = WaterfallBarPlanner.Compute(
            [10, 20, 99],
            totalIndices: [2],
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars[2].Kind.Should().Be(WaterfallBarKind.Total);
        bars[2].Bottom.Should().Be(0);
        bars[2].Top.Should().Be(30);
    }

    [Fact]
    public void TotalPoint_DoesNotAdvanceTheRunningTotalSoLaterPointsContinueFromIt()
    {
        var bars = WaterfallBarPlanner.Compute(
            [10, 20, 0, 5],
            totalIndices: [2],
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars[2].Kind.Should().Be(WaterfallBarKind.Total);
        bars[2].Top.Should().Be(30);
        // The point after the total continues from the running 30, not from 0.
        bars[3].Kind.Should().Be(WaterfallBarKind.Increase);
        bars[3].Bottom.Should().Be(30);
        bars[3].Top.Should().Be(35);
    }

    [Fact]
    public void NullTotalIndices_TreatsTheLastPointAsTheTotalForBackwardCompatibility()
    {
        var bars = WaterfallBarPlanner.Compute(
            [10, 20, 30],
            totalIndices: null,
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars[0].Kind.Should().Be(WaterfallBarKind.Increase);
        bars[1].Kind.Should().Be(WaterfallBarKind.Increase);
        bars[2].Kind.Should().Be(WaterfallBarKind.Total);
        // Last point as a total spans 0..(cumulative of the first two) = 0..30.
        bars[2].Bottom.Should().Be(0);
        bars[2].Top.Should().Be(30);
    }

    [Fact]
    public void NullTotalIndices_NoTotalsPolicyLeavesEveryPointAsAStep()
    {
        var bars = WaterfallBarPlanner.Compute(
            [10, 20, 30],
            totalIndices: null,
            WaterfallNullTotalsPolicy.NoTotals);

        bars.Should().OnlyContain(bar => bar.Kind == WaterfallBarKind.Increase);
        bars[^1].CumulativeAfter.Should().Be(60);
    }

    [Fact]
    public void EmptyTotalIndices_MeansNoTotalsEvenForTheLastPoint()
    {
        var bars = WaterfallBarPlanner.Compute(
            [10, 20, 30],
            totalIndices: [],
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars.Should().NotContain(b => b.Kind == WaterfallBarKind.Total);
    }

    [Fact]
    public void MultipleTotals_EachShowsTheRunningCumulativeAtThatPoint()
    {
        // start +10 (=10), total (shows 10), +5 (=15), total (shows 15)
        var bars = WaterfallBarPlanner.Compute(
            [10, 0, 5, 0],
            totalIndices: [1, 3],
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars[1].Kind.Should().Be(WaterfallBarKind.Total);
        bars[1].Top.Should().Be(10);
        bars[3].Kind.Should().Be(WaterfallBarKind.Total);
        bars[3].Top.Should().Be(15);
    }

    [Fact]
    public void CumulativeAfter_TracksRunningTotalThroughIncreasesDecreasesAndTotals()
    {
        var bars = WaterfallBarPlanner.Compute(
            [10, -4, 0],
            totalIndices: [2],
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars[0].CumulativeAfter.Should().Be(10); // after +10
        bars[1].CumulativeAfter.Should().Be(6);  // after -4 (connector must sit at 6, not the bar top 10)
        bars[2].CumulativeAfter.Should().Be(6);  // total anchor leaves the running total unchanged
    }

    [Fact]
    public void TotalAtFirstPoint_WithNoPriorValuesIsADegenerateZeroHeightTotal()
    {
        var bars = WaterfallBarPlanner.Compute(
            [0, 10],
            totalIndices: [0],
            WaterfallNullTotalsPolicy.LastPointIsTotal);

        bars[0].Kind.Should().Be(WaterfallBarKind.Total);
        bars[0].Bottom.Should().Be(0);
        bars[0].Top.Should().Be(0);
        bars[1].Bottom.Should().Be(0);
        bars[1].Top.Should().Be(10);
    }
}
