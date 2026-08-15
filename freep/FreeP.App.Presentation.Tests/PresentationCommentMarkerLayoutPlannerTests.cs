using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationCommentMarkerLayoutPlannerTests
{
    [Fact]
    public void Build_FitsAndCentersSlideAndMapsAnchorCoordinates()
    {
        var marker = PresentationCommentMarkerLayoutPlanner.Build(
            [Comment(xEmu: 250, yEmu: 100)],
            stageWidth: 1_100,
            stageHeight: 700,
            slideWidthEmu: 1_000,
            slideHeightEmu: 500,
            canvasMargin: 50).Should().ContainSingle().Subject;

        marker.Bounds.X.Should().Be(293);
        marker.Bounds.Y.Should().Be(193);
        marker.Bounds.Width.Should().Be(PresentationCommentMarkerLayoutPlanner.NormalDiameter);
        marker.Bounds.Height.Should().Be(PresentationCommentMarkerLayoutPlanner.NormalDiameter);
        marker.BorderThickness.Should().Be(PresentationCommentMarkerLayoutPlanner.NormalBorderThickness);
    }

    [Fact]
    public void Build_CentersFourByThreeAndSixteenByNineSlidesOnDifferentAxes()
    {
        var fourByThree = PresentationCommentMarkerLayoutPlanner.Build(
            [Comment(xEmu: 0, yEmu: 0)],
            1_000,
            600,
            400,
            300,
            canvasMargin: 0).Single();
        fourByThree.Bounds.Center.X.Should().Be(100);
        fourByThree.Bounds.Center.Y.Should().Be(0);

        var sixteenByNine = PresentationCommentMarkerLayoutPlanner.Build(
            [Comment(xEmu: 0, yEmu: 0)],
            1_000,
            600,
            1_600,
            900,
            canvasMargin: 0).Single();
        sixteenByNine.Bounds.Center.X.Should().Be(0);
        sixteenByNine.Bounds.Center.Y.Should().Be(18.75);
    }

    [Fact]
    public void Build_UsesSharedFallbackSlideSizeForMissingDimensions()
    {
        var fallback = PresentationCommentMarkerLayoutPlanner.Build(
            [Comment(xEmu: 1_000, yEmu: 2_000)],
            900,
            600,
            0,
            -1).Single();
        var explicitDefaults = PresentationCommentMarkerLayoutPlanner.Build(
            [Comment(xEmu: 1_000, yEmu: 2_000)],
            900,
            600,
            PresentationCommentMarkerLayoutPlanner.DefaultSlideWidthEmu,
            PresentationCommentMarkerLayoutPlanner.DefaultSlideHeightEmu).Single();

        fallback.Should().Be(explicitDefaults);
    }

    [Fact]
    public void Build_EmitsSelectedGeometryTooltipAndAutomationIdentity()
    {
        var marker = PresentationCommentMarkerLayoutPlanner.Build(
            [Comment(selected: true, modernCommentId: "modern-42")],
            1_000,
            600,
            1_600,
            900,
            canvasMargin: 0).Single();

        marker.IsSelected.Should().BeTrue();
        marker.Bounds.Width.Should().Be(PresentationCommentMarkerLayoutPlanner.SelectedDiameter);
        marker.BorderThickness.Should().Be(PresentationCommentMarkerLayoutPlanner.SelectedBorderThickness);
        marker.ToolTip.Should().Be("Alice: Review this anchor.");
        marker.AutomationId.Should().Be("modern-42");
    }

    [Theory]
    [InlineData(0, 600, 40)]
    [InlineData(1_000, 0, 40)]
    [InlineData(80, 600, 40)]
    [InlineData(1_000, 80, 40)]
    [InlineData(1_000, 600, -1)]
    [InlineData(double.NaN, 600, 40)]
    [InlineData(1_000, double.PositiveInfinity, 40)]
    public void Build_ReturnsNoMarkersForInvalidStageGeometry(
        double stageWidth,
        double stageHeight,
        double canvasMargin)
    {
        PresentationCommentMarkerLayoutPlanner.Build(
                [Comment()],
                stageWidth,
                stageHeight,
                1_600,
                900,
                canvasMargin)
            .Should().BeEmpty();
    }

    [Fact]
    public void BothRenderers_MaterializeTheSharedMarkerPlanWithoutOwningGeometryPolicy()
    {
        var wpf = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Host", "MainWindow.cs");
        var avalonia = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        wpf.Should().Contain("PresentationCommentMarkerLayoutPlanner.Build(");
        avalonia.Should().Contain("PresentationCommentMarkerLayoutPlanner.Build(");
        avalonia.Should().Contain("canvasStack.Children.Add(_commentOverlay);");

        wpf.Should().NotContain("const double CanvasMargin = 40.0;");
        wpf.Should().NotContain("double scaleX = slideW / presW;");
        avalonia.Should().NotContain("double scaleX = slideW / presW;");
    }

    private static PresentationCommentDescriptor Comment(
        long xEmu = 0,
        long yEmu = 0,
        bool selected = false,
        string modernCommentId = "") =>
        new(
            SlideIndex: 0,
            CommentIndex: 0,
            Idx: 1,
            Author: "Alice",
            Initials: "AL",
            TextPreview: "Review this anchor.",
            Timestamp: null,
            Xemu: xEmu,
            Yemu: yEmu,
            ModernAnchorKind: "",
            CanEdit: true,
            CanReply: true,
            CanDelete: true,
            CanResolve: true,
            CanReopen: false,
            ReplyCount: 0,
            MentionCount: 0,
            Replies: [],
            ThreadStatus: PresentationCommentThreadStatus.Open,
            IsSelected: selected)
        {
            ModernCommentId = modernCommentId,
        };
}
