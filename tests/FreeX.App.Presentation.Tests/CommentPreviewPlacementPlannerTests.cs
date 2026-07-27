using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class CommentPreviewPlacementPlannerTests
{
    [Fact]
    public void Calculate_PlacesPreviewToRightAndUsesSharedLimits()
    {
        var placement = CommentPreviewPlacementPlanner.Calculate(
            new LayoutRect(50, 40, 64, 20),
            new CommentPreviewLayoutSize(800, 500),
            new CommentPreviewLayoutSize(300, 230));

        placement.Should().Be(new CommentPreviewPlacement(120, 40, 300, 220));
    }

    [Fact]
    public void Calculate_FlipsPreviewLeftNearRightViewportEdge()
    {
        var placement = CommentPreviewPlacementPlanner.Calculate(
            new LayoutRect(720, 40, 64, 20),
            new CommentPreviewLayoutSize(800, 500),
            new CommentPreviewLayoutSize(300, 230));

        placement.Should().Be(new CommentPreviewPlacement(414, 40, 300, 220));
    }

    [Fact]
    public void Calculate_ClampsPreviewInsideSmallViewport()
    {
        var placement = CommentPreviewPlacementPlanner.Calculate(
            new LayoutRect(90, 130, 50, 20),
            new CommentPreviewLayoutSize(150, 160),
            new CommentPreviewLayoutSize(300, 230));

        placement.Should().Be(new CommentPreviewPlacement(8, 8, 134, 144));
    }

    [Fact]
    public void CalculateConnector_UsesSideChosenByPlacement()
    {
        var rightCell = new LayoutRect(50, 40, 64, 20);
        var leftCell = new LayoutRect(720, 40, 64, 20);
        var rightPlacement = CommentPreviewPlacementPlanner.Calculate(
            rightCell,
            new CommentPreviewLayoutSize(800, 500),
            new CommentPreviewLayoutSize(120, 60));
        var leftPlacement = CommentPreviewPlacementPlanner.Calculate(
            leftCell,
            new CommentPreviewLayoutSize(800, 500),
            new CommentPreviewLayoutSize(120, 60));

        CommentPreviewPlacementPlanner.CalculateConnector(rightCell, rightPlacement)
            .Should().Be(new CommentPreviewConnectorLine(
                new LayoutPoint(114, 40),
                new LayoutPoint(120, 40)));
        CommentPreviewPlacementPlanner.CalculateConnector(leftCell, leftPlacement)
            .Should().Be(new CommentPreviewConnectorLine(
                new LayoutPoint(720, 40),
                new LayoutPoint(714, 40)));
    }

    [Fact]
    public void EstimatePreviewSize_MatchesAuthorityTextEstimate()
    {
        var size = CommentPreviewPlacementPlanner.EstimatePreviewSize(
            new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Short note"));

        size.Should().Be(new CommentPreviewLayoutSize(104, 70));
    }
}
