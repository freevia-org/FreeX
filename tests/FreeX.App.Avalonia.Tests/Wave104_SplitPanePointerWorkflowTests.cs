using FluentAssertions;

using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave104_SplitPanePointerWorkflowTests
{
    [Fact]
    public void DividerHitTestingAndDragUseTheRenderedPinnedPaneGeometry()
    {
        var viewport = BuildSplitViewport();

        var layout = SplitPanePointerPlanner.CalculateDividerLayout(viewport, 44, 20);

        layout.HorizontalY.Should().Be(100);
        layout.VerticalX.Should().Be(172);
        SplitPanePointerPlanner.HitTestDivider(viewport, new GridPoint(250, 100), 640, 420, 44, 20)
            .Should().Be(SplitPanePointerHandle.Horizontal);
        SplitPanePointerPlanner.HitTestDivider(viewport, new GridPoint(172, 100), 640, 420, 44, 20)
            .Should().Be(SplitPanePointerHandle.Intersection);

        SplitPanePointerPlanner.CalculateDividerDragTarget(
                viewport,
                SplitPanePointerHandle.Horizontal,
                new GridPoint(250, 132),
                44,
                20)
            .Should().Be(new SplitPanePointerDividerDragTarget(6, null));
        SplitPanePointerPlanner.CalculateDividerDragTarget(
                viewport,
                SplitPanePointerHandle.Vertical,
                new GridPoint(268, 200),
                44,
                20)
            .Should().Be(new SplitPanePointerDividerDragTarget(null, 4));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(20, 10)]
    public void DividerDragTargetsStayRelativeToSplitAnchorAcrossMainViewportOrigins(
        int mainRowOrigin,
        int mainColumnOrigin)
    {
        var viewport = BuildProductionSplitViewport((uint)mainRowOrigin, (uint)mainColumnOrigin);

        SplitPanePointerPlanner.CalculateDividerDragTarget(
                viewport,
                SplitPanePointerHandle.Intersection,
                new GridPoint(204, 110),
                44,
                20)
            .Should().Be(new SplitPanePointerDividerDragTarget(5, 3));
        SplitPanePointerPlanner.CalculateDividerDragTarget(
                viewport,
                SplitPanePointerHandle.Intersection,
                new GridPoint(268, 130),
                44,
                20)
            .Should().Be(new SplitPanePointerDividerDragTarget(6, 4));
    }

    [Fact]
    public void MiniScrollbarsExposeSharedScrollTargetsAndPageClicks()
    {
        var viewport = BuildSplitViewport();
        var chrome = SplitPanePointerPlanner.CalculateScrollbarChrome(viewport, 640, 420, 44, 20);

        chrome.HorizontalTopRight.Should().NotBeNull();
        chrome.VerticalBottomLeft.Should().NotBeNull();
        var horizontal = chrome.HorizontalTopRight!.Value;
        var vertical = chrome.VerticalBottomLeft!.Value;

        SplitPanePointerPlanner.HitTestScrollbar(
                chrome,
                new GridPoint(horizontal.Thumb.Left + 1, horizontal.Thumb.Top + 1))
            .Should().Be(new SplitPanePointerScrollbarHit(
                SplitPanePointerScrollbarPart.Thumb,
                SplitPanePointerScrollbarOrientation.Horizontal,
                SplitPanePointerRegion.TopRight));
        SplitPanePointerPlanner.CalculatePageTarget(
                horizontal,
                currentIndex: 3,
                new GridPoint(horizontal.Track.Right - 2, horizontal.Track.Top + 3))
            .Index.Should().Be(5);
        SplitPanePointerPlanner.CalculateThumbDragTarget(
                vertical,
                new GridPoint(vertical.Track.Left + 3, vertical.Track.Bottom - 3),
                pointerOffset: 2)
            .Index.Should().BeGreaterThan(1);
    }

    [Fact]
    public void WheelOwnershipAllowsOnlySharedScrollbarAxes()
    {
        var viewport = BuildSplitViewport();

        SplitPanePointerPlanner.ResolveWheelTarget(
                viewport,
                new GridPoint(300, 50),
                640,
                420,
                44,
                20,
                requestedHorizontal: false)
            .Should().Be(new SplitPanePointerWheelTarget(SplitPanePointerRegion.TopRight, false));
        SplitPanePointerPlanner.ResolveWheelTarget(
                viewport,
                new GridPoint(100, 200),
                640,
                420,
                44,
                20,
                requestedHorizontal: false)
            .Should().Be(new SplitPanePointerWheelTarget(SplitPanePointerRegion.BottomLeft, false));

        SplitPanePointerPlanner.CanScroll(SplitPanePointerRegion.TopRight, horizontal: true).Should().BeTrue();
        SplitPanePointerPlanner.CanScroll(SplitPanePointerRegion.TopRight, horizontal: false).Should().BeFalse();
        SplitPanePointerPlanner.CanScroll(SplitPanePointerRegion.BottomLeft, horizontal: false).Should().BeTrue();
        SplitPanePointerPlanner.CanScroll(SplitPanePointerRegion.BottomLeft, horizontal: true).Should().BeFalse();
    }

    [Fact]
    public void AvaloniaHostWiresPointerCaptureAndIndependentPaneRoutes()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "FreeX.App.Avalonia", "MainWindow.SplitPanePointer.cs"));
        var windowSource = File.ReadAllText(FindRepositoryFile(
            "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(FindRepositoryFile(
            "src", "FreeX.App.Services", "WorkbookSession.cs"));

        source.Should().Contain("InputElement.PointerPressedEvent");
        source.Should().Contain("args.Pointer.Capture(_sheetGridHost)");
        source.Should().Contain("CalculatePageTarget");
        source.Should().Contain("PanViewport(0, delta)");
        source.Should().Contain("PanViewport(delta, 0)");
        source.Should().NotContain("SetSplitPaneTopRightLeftCol");
        source.Should().NotContain("SetSplitPaneBottomLeftTopRow");
        source.Should().Contain("_session.SetSplitPanes(");
        source.Should().NotContain("ResetSplitPaneOffsets");
        sessionSource.Should().Contain("ResetSplitPaneOffsets();");
        windowSource.Should().Contain("SplitPanePointerPlanner.ResolveWheelTarget");
        windowSource.Should().Contain("CanScrollSplitPane(target.Region, target.Horizontal)");
        windowSource.Should().Contain("PanViewport(rowDelta * step, colDelta * step)");
        windowSource.Should().NotContain("ScrollSplitPaneTopRight");
        windowSource.Should().NotContain("ScrollSplitPaneBottomLeft");
        windowSource.Should().Contain("InputElement.PointerWheelChangedEvent");
        windowSource.Should().Contain("RoutingStrategies.Tunnel");
        windowSource.Should().Contain("handledEventsToo: true");
    }

    private static ViewportModel BuildSplitViewport() =>
        new(
            [],
            [new RowMetric(6, 20, 0), new RowMetric(7, 20, 20)],
            [new ColMetric(5, 64, 0), new ColMetric(6, 64, 64)],
            SplitPanes: new SplitPaneState(
                5,
                3,
                [
                    new RowMetric(1, 20, 0),
                    new RowMetric(2, 20, 20),
                    new RowMetric(3, 20, 40),
                    new RowMetric(4, 20, 60)
                ],
                [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)],
                TopRightColumns: [new ColMetric(3, 64, 0), new ColMetric(4, 64, 64)],
                BottomLeftRows: [new RowMetric(5, 20, 0), new RowMetric(6, 20, 20)]));

    private static ViewportModel BuildProductionSplitViewport(uint mainRowOrigin, uint mainColumnOrigin) =>
        new(
            [],
            [
                new RowMetric(mainRowOrigin, 20, 0),
                new RowMetric(mainRowOrigin + 1, 20, 20),
                new RowMetric(mainRowOrigin + 2, 20, 40)
            ],
            [
                new ColMetric(mainColumnOrigin, 64, 0),
                new ColMetric(mainColumnOrigin + 1, 64, 64),
                new ColMetric(mainColumnOrigin + 2, 64, 128)
            ],
            SplitPanes: new SplitPaneState(
                5,
                3,
                [
                    new RowMetric(1, 20, 0),
                    new RowMetric(2, 20, 20),
                    new RowMetric(3, 20, 40),
                    new RowMetric(4, 20, 60)
                ],
                [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)]));

    private static string FindRepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile("FreeX.slnx", parts);
}
