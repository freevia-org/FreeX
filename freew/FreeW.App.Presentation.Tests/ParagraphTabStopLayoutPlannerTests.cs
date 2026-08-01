using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class ParagraphTabStopLayoutPlannerTests
{
    private const double DipPerPoint = 96.0 / 72.0;

    [Fact]
    public void ResolveNextStop_UsesExplicitWordMarginRelativeStop()
    {
        var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: 48,
            followingSegmentWidthDip: 30,
            tabStops: [new TabStop(144, TabStopAlignment.Left, TabLeader.Dots)],
            defaultTabStopPt: 36,
            dipPerPoint: DipPerPoint);

        plan.IsExplicit.Should().BeTrue();
        plan.StopPositionDip.Should().BeApproximately(192, 0.01);
        plan.SegmentStartDip.Should().BeApproximately(192, 0.01);
        plan.AdvanceDip.Should().BeApproximately(144, 0.01);
        plan.Leader.Should().Be(TabLeader.Dots);
        plan.HasLeader.Should().BeTrue();
    }

    [Fact]
    public void BuildPlacementPlan_RightAndCenterStopsUseFollowingSegmentWidth()
    {
        var right = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: 40,
            followingSegmentWidthDip: 60,
            tabStops: [new TabStop(144, TabStopAlignment.Right, TabLeader.Dashes)],
            defaultTabStopPt: 36,
            dipPerPoint: DipPerPoint);
        var center = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: 40,
            followingSegmentWidthDip: 60,
            tabStops: [new TabStop(144, TabStopAlignment.Center)],
            defaultTabStopPt: 36,
            dipPerPoint: DipPerPoint);

        right.SegmentStartDip.Should().BeApproximately(132, 0.01);
        right.AdvanceDip.Should().BeApproximately(92, 0.01);
        right.Leader.Should().Be(TabLeader.Dashes);

        center.SegmentStartDip.Should().BeApproximately(162, 0.01);
        center.AdvanceDip.Should().BeApproximately(122, 0.01);
    }

    [Fact]
    public void BuildPlacementPlan_DecimalStopAlignsDecimalOffsetInsteadOfRightEdge()
    {
        var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: 40,
            followingSegmentWidthDip: 72,
            tabStops: [new TabStop(144, TabStopAlignment.Decimal, TabLeader.Dots)],
            defaultTabStopPt: 36,
            dipPerPoint: DipPerPoint,
            decimalAlignmentOffsetDip: 28);

        plan.Alignment.Should().Be(TabStopAlignment.Decimal);
        plan.StopPositionDip.Should().BeApproximately(192, 0.01);
        plan.SegmentStartDip.Should().BeApproximately(164, 0.01);
        plan.AdvanceDip.Should().BeApproximately(124, 0.01);
        plan.Leader.Should().Be(TabLeader.Dots);
    }

    [Fact]
    public void BuildPlacementPlan_DecimalStopWithoutSeparatorFallsBackToRightAlignedSegment()
    {
        var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: 40,
            followingSegmentWidthDip: 72,
            tabStops: [new TabStop(144, TabStopAlignment.Decimal)],
            defaultTabStopPt: 36,
            dipPerPoint: DipPerPoint);

        plan.SegmentStartDip.Should().BeApproximately(120, 0.01);
        plan.AdvanceDip.Should().BeApproximately(80, 0.01);
    }

    [Fact]
    public void ResolveNextStop_FallsBackToNextDefaultInterval()
    {
        var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: 51,
            followingSegmentWidthDip: 0,
            tabStops: [],
            defaultTabStopPt: 36,
            dipPerPoint: DipPerPoint);

        plan.IsExplicit.Should().BeFalse();
        plan.StopPositionDip.Should().BeApproximately(96, 0.01);
        plan.AdvanceDip.Should().BeApproximately(45, 0.01);
        plan.Leader.Should().Be(TabLeader.None);
    }

    [Fact]
    public void ResolveNextStop_ClearOperationRemovesSamePositionStop()
    {
        var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: 0,
            followingSegmentWidthDip: 0,
            tabStops:
            [
                new TabStop(72, TabStopAlignment.Right, TabLeader.Dots),
                new TabStop(72, IsClear: true),
                new TabStop(144, TabStopAlignment.Center),
            ],
            defaultTabStopPt: 36,
            dipPerPoint: DipPerPoint);

        plan.IsExplicit.Should().BeTrue();
        plan.StopPositionDip.Should().BeApproximately(192, 0.01);
        plan.Alignment.Should().Be(TabStopAlignment.Center);
        plan.Leader.Should().Be(TabLeader.None);
    }

    [Fact]
    public void BuildPlacementPlan_ClampsBackwardAlignedSegmentsToForwardAdvance()
    {
        var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: 180,
            followingSegmentWidthDip: 120,
            tabStops: [new TabStop(144, TabStopAlignment.Right, TabLeader.Underline)],
            defaultTabStopPt: 36,
            dipPerPoint: DipPerPoint);

        plan.SegmentStartDip.Should().BeApproximately(181, 0.01);
        plan.AdvanceDip.Should().BeApproximately(1, 0.01);
        plan.Leader.Should().Be(TabLeader.Underline);
    }
}
