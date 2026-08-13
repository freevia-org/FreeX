using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentRulerInteractionPlannerTests
{
    [Fact]
    public void HorizontalMetrics_MapBetweenRendererCoordinatesAndModelPoints()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            MarginLeftPt = 72,
            MarginRightPt = 72
        };

        var metrics = DocumentRulerInteractionPlanner.TryBuildCenteredHorizontalMetrics(1000, page, 1)!;

        metrics.ContentStart.Should().BeApproximately(188, 0.001);
        metrics.ContentEnd.Should().BeApproximately(812, 0.001);
        metrics.XToContentPoint(metrics.ContentPointToX(108)).Should().BeApproximately(108, 0.001);
    }

    [Fact]
    public void HorizontalHitTest_DistinguishesIndentMarkersTabsAndNewStops()
    {
        var metrics = new DocumentRulerHorizontalMetrics(100, 700, 1);
        var formatting = ParagraphFormatting.Default with
        {
            IndentLeftPt = 36,
            IndentRightPt = 54,
            FirstLineIndentPt = 18,
            TabStops = [new TabStop(144, TabStopAlignment.Center)]
        };

        Hit(metrics.ContentPointToX(54), 2).Should().Be(DocumentRulerDragKind.FirstLineIndent);
        Hit(metrics.ContentPointToX(36), 14).Should().Be(DocumentRulerDragKind.LeftIndent);
        Hit(metrics.ContentEnd - PageLayout.PointsToDip(54), 14).Should().Be(DocumentRulerDragKind.RightIndent);
        Hit(metrics.ContentPointToX(144), 8).Should().Be(DocumentRulerDragKind.TabStop);
        Hit(metrics.ContentPointToX(200), 8).Should().Be(DocumentRulerDragKind.NewTabStop);
        Hit(50, 8).Should().Be(DocumentRulerDragKind.None);

        DocumentRulerDragKind Hit(double x, double y) =>
            DocumentRulerInteractionPlanner.HitTestHorizontal(
                new DocumentRulerPoint(x, y), 16, metrics, formatting, out _);
    }

    [Fact]
    public void TabStopMutation_SnapsSortsPreservesExistingAlignmentAndRemoves()
    {
        IReadOnlyList<TabStop> start =
        [
            new(144, TabStopAlignment.Right, TabLeader.Dots),
            new(36, TabStopAlignment.Left)
        ];

        var moved = DocumentRulerInteractionPlanner.MoveOrAddTabStop(
            start, 0, 101, TabStopAlignment.Decimal);
        moved.Should().Equal(
            new TabStop(36, TabStopAlignment.Left),
            new TabStop(102, TabStopAlignment.Right, TabLeader.Dots));

        var added = DocumentRulerInteractionPlanner.MoveOrAddTabStop(
            moved, -1, 75, TabStopAlignment.Center);
        added.Should().Equal(
            new TabStop(36, TabStopAlignment.Left),
            new TabStop(78, TabStopAlignment.Center),
            new TabStop(102, TabStopAlignment.Right, TabLeader.Dots));

        DocumentRulerInteractionPlanner.RemoveTabStop(added, 1).Should().Equal(
            new TabStop(36, TabStopAlignment.Left),
            new TabStop(102, TabStopAlignment.Right, TabLeader.Dots));
    }

    [Fact]
    public void IndentPlanning_SnapsOnlyTheDraggedMarker()
    {
        var start = ParagraphFormatting.Default with
        {
            IndentLeftPt = 18,
            IndentRightPt = 24,
            FirstLineIndentPt = 12
        };

        DocumentRulerInteractionPlanner.BuildIndentFormatting(start, DocumentRulerDragKind.LeftIndent, 73)
            .Should().Be(start with { IndentLeftPt = 72 });
        DocumentRulerInteractionPlanner.BuildIndentFormatting(start, DocumentRulerDragKind.FirstLineIndent, 58)
            .Should().Be(start with { FirstLineIndentPt = 42 });
        DocumentRulerInteractionPlanner.BuildIndentFormatting(start, DocumentRulerDragKind.FirstLineIndent, 6)
            .Should().Be(start with { FirstLineIndentPt = -12 }, "the first-line marker must support hanging indents");
        DocumentRulerInteractionPlanner.BuildIndentFormatting(start, DocumentRulerDragKind.RightIndent, 31)
            .Should().Be(start with { IndentRightPt = 30 });
    }

    [Fact]
    public void VerticalPlanning_UsesPageAnchorZoomAndOppositeBottomDirection()
    {
        var page = new PageSettings
        {
            HeightPt = 792,
            MarginTopPt = 72,
            MarginBottomPt = 90
        };
        var metrics = DocumentRulerInteractionPlanner.TryBuildVerticalMetrics(page, 1.5, pageTopDip: 40)!;

        metrics.TopBoundaryY.Should().BeApproximately(184, 0.001);
        DocumentRulerInteractionPlanner.HitTestVertical(184, metrics)
            .Should().Be(DocumentRulerDragKind.TopMargin);
        DocumentRulerInteractionPlanner.ResolveVerticalMargin(
                DocumentRulerDragKind.TopMargin, 72, 24, 90, metrics)
            .Should().BeApproximately(84, 0.001);
        DocumentRulerInteractionPlanner.ResolveVerticalMargin(
                DocumentRulerDragKind.BottomMargin, 90, 24, 72, metrics)
            .Should().BeApproximately(78, 0.001);
    }

    [Theory]
    [InlineData(-8, 16, true)]
    [InlineData(-7, 16, false)]
    [InlineData(23, 16, false)]
    [InlineData(24, 16, true)]
    public void TabDropRemoval_UsesSharedHitSlop(double y, double height, bool expected) =>
        DocumentRulerInteractionPlanner.IsTabStopRemovalDrop(y, height).Should().Be(expected);
}
