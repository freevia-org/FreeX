using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class ViewportGeometryPlannerTests
{
    [Fact]
    public void ProjectMetrics_PrependsSplitBandsAndDropsOverlappingMainMetrics()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(2, 20, 0), new RowMetric(3, 20, 20), new RowMetric(4, 20, 40)],
            [new ColMetric(2, 40, 0), new ColMetric(3, 40, 40), new ColMetric(4, 40, 80)],
            SplitPanes: new SplitPaneState(
                3,
                3,
                [new RowMetric(1, 18, 0), new RowMetric(2, 18, 18)],
                [new ColMetric(1, 32, 0), new ColMetric(2, 32, 32)]));

        ViewportGeometryPlanner.ProjectRows(viewport).Select(metric => metric.Row)
            .Should().Equal(1, 2, 3, 4);
        ViewportGeometryPlanner.ProjectColumns(viewport).Select(metric => metric.Col)
            .Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void TryGetCellBounds_UsesIndependentSplitPaneOriginsWithMetricOffsets()
    {
        var viewport = SplitViewport();
        var settings = new ViewportGeometrySettings(40, 20);

        ViewportGeometryPlanner.TryGetCellBounds(viewport, 1, 10, settings, out var topRight)
            .Should().BeTrue();
        ViewportGeometryPlanner.TryGetCellBounds(viewport, 20, 1, settings, out var bottomLeft)
            .Should().BeTrue();

        topRight.Should().Be(new LayoutRect(184, 20, 50, 18));
        bottomLeft.Should().Be(new LayoutRect(40, 60, 32, 22));
    }

    [Fact]
    public void TryGetCellBounds_SeparatesHiddenHeadingOriginsFromLegacySplitDividerOrigins()
    {
        var settings = new ViewportGeometrySettings(
            0,
            0,
            SplitColumnHeaderHeight: 20,
            SplitRowHeaderWidth: 40);

        ViewportGeometryPlanner.TryGetCellBounds(SplitViewport(), 1, 10, settings, out var topRight)
            .Should().BeTrue();
        ViewportGeometryPlanner.TryGetCellBounds(SplitViewport(), 20, 1, settings, out var bottomLeft)
            .Should().BeTrue();

        topRight.Should().Be(new LayoutRect(184, 0, 50, 18));
        bottomLeft.Should().Be(new LayoutRect(0, 60, 32, 22));
    }

    [Fact]
    public void TryGetCellBounds_SequentialProfileAppliesNativeMinimumsAndScale()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 0, 0), new RowMetric(2, 5, 0)],
            [new ColMetric(1, 0, 0), new ColMetric(2, 10, 0)]);
        var settings = new ViewportGeometrySettings(
            30,
            15,
            Scale: 2,
            MinimumColumnWidth: 4,
            MinimumRowHeight: 3,
            MetricPlacement: ViewportMetricPlacement.Sequential,
            HitTestEdges: ViewportHitTestEdgeBehavior.InclusiveEnd);

        ViewportGeometryPlanner.TryGetCellBounds(viewport, 2, 2, settings, out var bounds)
            .Should().BeTrue();

        bounds.Should().Be(new LayoutRect(38, 21, 20, 10));
    }

    [Fact]
    public void TryGetCellBounds_ExplicitMetricsPreserveMainPaneOnlyConsumers()
    {
        var viewport = SplitViewport();
        var settings = new ViewportGeometrySettings(
            40,
            20,
            MetricPlacement: ViewportMetricPlacement.Sequential);

        ViewportGeometryPlanner.TryGetCellBounds(
                viewport.RowMetrics,
                viewport.ColMetrics,
                10,
                10,
                settings,
                out var bounds)
            .Should().BeTrue();

        bounds.Should().Be(new LayoutRect(40, 20, 50, 20));
    }

    [Fact]
    public void TryGetVisibleRangeBounds_ClipsToVisibleProjectedMetrics()
    {
        var sheet = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(4, 10, 0), new RowMetric(6, 20, 10)],
            [new ColMetric(3, 30, 0), new ColMetric(5, 40, 30)]);
        var range = new GridRange(new CellAddress(sheet, 2, 2), new CellAddress(sheet, 5, 4));
        var settings = new ViewportGeometrySettings(25, 12, MetricPlacement: ViewportMetricPlacement.Sequential);

        ViewportGeometryPlanner.TryGetVisibleRangeBounds(viewport, range, settings, out var bounds)
            .Should().BeTrue();

        bounds.Should().Be(new LayoutRect(25, 12, 30, 10));
    }

    [Fact]
    public void TryGetVisibleRangeBounds_OffsetProfileUsesNativeMetricOffsets()
    {
        var sheet = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(4, 10, 25), new RowMetric(5, 20, 35)],
            [new ColMetric(3, 30, 70), new ColMetric(4, 40, 100)]);
        var range = new GridRange(new CellAddress(sheet, 4, 3), new CellAddress(sheet, 5, 4));

        ViewportGeometryPlanner.TryGetVisibleRangeBounds(
                viewport,
                range,
                new ViewportGeometrySettings(25, 12),
                out var bounds)
            .Should().BeTrue();

        bounds.Should().Be(new LayoutRect(95, 37, 70, 30));
    }

    [Fact]
    public void MergePlanning_UsesVisibleSubstituteAnchorAndStopsAtMetricGap()
    {
        var sheet = SheetId.New();
        var merge = new GridRange(new CellAddress(sheet, 5, 2), new CellAddress(sheet, 9, 4));
        var rows = new[]
        {
            new RowMetric(6, 10, 0),
            new RowMetric(7, 20, 10),
            new RowMetric(9, 30, 30),
        };
        var columns = new[]
        {
            new ColMetric(3, 40, 0),
            new ColMetric(4, 50, 40),
        };

        ViewportGeometryPlanner.ResolveVisibleMergeAnchor(merge, rows, columns)
            .Should().Be(new CellAddress(sheet, 6, 3));
        ViewportGeometryPlanner.CalculateVisibleMergeSpan(
                merge,
                0,
                0,
                rows,
                columns,
                new ViewportGeometrySettings(0, 0))
            .Should().Be(new ViewportMergeSpan(2, 2, 30, 90));
    }

    [Fact]
    public void SplitPaneLayouts_EmitCrossPaneMergeOncePerQuadrantAndStripSecondaryContent()
    {
        var sheet = SheetId.New();
        var anchor = Cell(1, 1, "merged");
        var viewport = new ViewportModel(
            [],
            [new RowMetric(10, 20, 0)],
            [new ColMetric(10, 50, 0)],
            SplitPanes: new SplitPaneState(
                2,
                2,
                [new RowMetric(1, 18, 0)],
                [new ColMetric(1, 32, 0)],
                [anchor],
                [new ColMetric(2, 50, 0)]));
        var merge = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 2));

        var layouts = ViewportGeometryPlanner.CalculateSplitPaneLayouts(
            viewport,
            new ViewportGeometrySettings(40, 20),
            [merge]);

        layouts.Should().HaveCount(2);
        layouts[0].Region.Should().Be(SplitPanePointerRegion.TopLeft);
        layouts[0].Cell.DisplayText.Should().Be("merged");
        layouts[1].Region.Should().Be(SplitPanePointerRegion.TopRight);
        layouts[1].Cell.DisplayText.Should().BeEmpty();
    }

    [Fact]
    public void OverflowAvailability_LogicalTraversalCrossesHiddenColumnsButStopsAtOccupiedCell()
    {
        var columns = new[]
        {
            new ColMetric(1, 40, 0),
            new ColMetric(3, 50, 40),
            new ColMetric(4, 60, 90),
        };

        var availability = ViewportGeometryPlanner.CalculateOverflowAvailability(
            2,
            1,
            0,
            columns,
            0,
            new ViewportGeometrySettings(0, 0),
            ViewportOverflowTraversal.LogicalColumns,
            (_, column) => column == 4);

        availability.Should().Be(new ViewportOverflowAvailability(0, 50));
    }

    [Fact]
    public void OverflowAvailability_VisibleTraversalStopsAtFrozenScrollableBoundary()
    {
        var columns = new[]
        {
            new ColMetric(1, 40, 0),
            new ColMetric(5, 50, 40),
        };

        ViewportGeometryPlanner.CalculateOverflowAvailability(
                2,
                1,
                0,
                columns,
                1,
                new ViewportGeometrySettings(0, 0),
                ViewportOverflowTraversal.VisibleMetrics,
                (_, _) => false)
            .Should().Be(default(ViewportOverflowAvailability));
    }

    [Theory]
    [InlineData(CellHAlign.Left, 10, 20, 30, 50)]
    [InlineData(CellHAlign.Center, 10, 20, 20, 60)]
    [InlineData(CellHAlign.Right, 10, 20, 20, 40)]
    public void CalculateOverflowClip_ExpandsOnlyAlongAlignmentDirections(
        CellHAlign alignment,
        double leftWidth,
        double rightWidth,
        double expectedX,
        double expectedWidth)
    {
        ViewportGeometryPlanner.CalculateOverflowClip(
                new LayoutRect(30, 12, 30, 18),
                alignment,
                new ViewportOverflowAvailability(leftWidth, rightWidth))
            .Should().Be(new LayoutRect(expectedX, 12, expectedWidth, 18));
    }

    [Fact]
    public void HitTesting_ParameterizesEstablishedHostBoundaryDifference()
    {
        var sheet = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 40, 0), new ColMetric(2, 40, 40)]);
        var boundary = new LayoutPoint(50, 20);

        ViewportGeometryPlanner.HitTestCell(
                viewport,
                sheet,
                boundary,
                new ViewportGeometrySettings(
                    10,
                    10,
                    MetricPlacement: ViewportMetricPlacement.Sequential,
                    HitTestEdges: ViewportHitTestEdgeBehavior.ExclusiveEnd))
            .Should().Be(new CellAddress(sheet, 1, 2));
        ViewportGeometryPlanner.HitTestCell(
                viewport,
                sheet,
                boundary,
                new ViewportGeometrySettings(
                    10,
                    10,
                    MetricPlacement: ViewportMetricPlacement.Sequential,
                    HitTestEdges: ViewportHitTestEdgeBehavior.InclusiveEnd))
            .Should().Be(new CellAddress(sheet, 1, 1));
    }

    [Theory]
    [InlineData(1, 1, ViewportFrozenQuadrant.FrozenRowsAndColumns)]
    [InlineData(1, 3, ViewportFrozenQuadrant.FrozenRows)]
    [InlineData(3, 1, ViewportFrozenQuadrant.FrozenColumns)]
    [InlineData(3, 3, ViewportFrozenQuadrant.Scrollable)]
    public void ResolveFrozenQuadrant_ClassifiesBothAxes(uint row, uint column, ViewportFrozenQuadrant expected)
    {
        var viewport = new ViewportModel([], [], [], new FrozenPaneState(2, 2));

        ViewportGeometryPlanner.ResolveFrozenQuadrant(viewport, row, column).Should().Be(expected);
    }

    [Fact]
    public void CellEdgeVisibility_SuppressesMergedInteriorEdges()
    {
        var sheet = SheetId.New();
        var merge = new GridRange(new CellAddress(sheet, 2, 3), new CellAddress(sheet, 4, 5));

        ViewportGeometryPlanner.GetCellEdgeVisibility(merge, 3, 4)
            .Should().Be(new ViewportCellEdgeVisibility(false, false, false, false));
        ViewportGeometryPlanner.GetCellEdgeVisibility(merge, 2, 5)
            .Should().Be(new ViewportCellEdgeVisibility(true, false, false, true));
    }

    private static ViewportModel SplitViewport() =>
        new(
            [],
            [new RowMetric(10, 20, 0)],
            [new ColMetric(10, 50, 0)],
            SplitPanes: new SplitPaneState(
                3,
                3,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18)],
                [new ColMetric(1, 32, 0), new ColMetric(2, 112, 32)],
                [],
                [new ColMetric(10, 50, 0)],
                [new RowMetric(20, 22, 0)]));

    private static DisplayCell Cell(uint row, uint column, string text) =>
        new(row, column, new TextValue(text), text, null, StyleId.Default, null);
}

public sealed class ViewportGeometryPlannerOwnershipTests
{
    [Fact]
    public void ViewportGeometry_HasOnePresentationOwnerAndThinNativeAdapters()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var planner = Read(repoRoot, "src", "FreeX.App.Presentation", "Rendering", "ViewportGeometryPlanner.cs");
        var wpfAdapter = Read(repoRoot, "src", "FreeX.App.UI", "SplitPaneCellLayoutPlanner.cs");
        var wpfSplit = Read(repoRoot, "src", "FreeX.App.UI", "GridView.SplitPanes.cs");
        var wpfGrid = Read(repoRoot, "src", "FreeX.App.UI", "GridView.cs");
        var wpfRendering = Read(repoRoot, "src", "FreeX.App.UI", "GridView.Rendering.cs");
        var wpfCommentPreview = Read(repoRoot, "src", "FreeX.App.UI", "GridView.CommentPreview.cs");
        var wpfSelection = Read(repoRoot, "src", "FreeX.App.UI", "GridView.Rendering.Selection.cs");
        var autofill = Read(repoRoot, "src", "FreeX.App.Presentation", "GridInteraction", "GridAutofillPlanner.cs");
        var avalonia = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var avaloniaSlicerTimeline = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.SlicerTimeline.cs");

        planner.Should().Contain("public static class ViewportGeometryPlanner");
        planner.Should().Contain("CalculateSplitPaneLayouts(");
        planner.Should().Contain("TryGetVisibleRangeBounds(");
        planner.Should().Contain("CalculateOverflowAvailability(");
        planner.Should().Contain("HitTestCell(");
        planner.Should().Contain("GetCellEdgeVisibility(");

        wpfAdapter.Should().Contain("ViewportGeometryPlanner.CalculateSplitPaneLayouts(");
        wpfAdapter.Should().Contain("ViewportGeometryPlanner.VisitSplitPaneLayouts(");
        wpfAdapter.Should().NotContain("SplitPaneOccupiedCellMap");
        wpfAdapter.Should().NotContain("MergeRangeIndex");
        wpfSplit.Should().Contain("ViewportGeometryPlanner.HitTestCell(");
        wpfGrid.Should().Contain("ViewportGeometryPlanner.TryGetCellBounds(");
        wpfRendering.Should().Contain("ViewportGeometryPlanner.CalculateOverflowAvailability(");
        wpfRendering.Should().Contain("ViewportGeometryPlanner.GetCellEdgeVisibility(");
        wpfCommentPreview.Should().Contain("ViewportGeometryPlanner.TryGetVisibleRangeBounds(");
        wpfSelection.Should().Contain("ViewportGeometryPlanner.TryGetCellBounds(");
        wpfSelection.Should().NotContain("TryResolveSplitPaneRowMetric(");
        autofill.Should().Contain("ViewportGeometryPlanner.TryGetCellBounds(");
        autofill.Should().NotContain("TryResolveSplitPaneRowMetric(");
        autofill.Should().NotContain("CalculateSplitDividerHorizontalY(");

        avalonia.Should().Contain("ViewportGeometryPlanner.ProjectRows(viewport)");
        avalonia.Should().Contain("ViewportGeometryPlanner.ProjectColumns(viewport)");
        avalonia.Should().Contain("ViewportGeometryPlanner.ResolveVisibleMergeAnchor(");
        avalonia.Should().Contain("ViewportGeometryPlanner.CalculateVisibleMergeSpan(");
        avalonia.Should().Contain("ViewportGeometryPlanner.TryGetCellBounds(");
        avalonia.Should().Contain("ViewportGeometryPlanner.TryGetVisibleRangeBounds(");
        avalonia.Should().Contain("ViewportGeometryPlanner.CalculateOverflowAvailability(");
        avalonia.Should().Contain("ViewportGeometryPlanner.HitTestCell(");
        avalonia.Should().NotContain("private static CellAddress? ResolveVisibleMergeAnchor(");
        avalonia.Should().NotContain("private static (int RowSpan, int ColSpan, double Height, double Width) ResolveVisibleMergeSpan(");
        avalonia.Should().NotContain("private static bool TryGetDisplayedColumnLeft(");
        avalonia.Should().NotContain("private static bool TryGetDisplayedRowTop(");
        avaloniaSlicerTimeline.Should().Contain("ViewportGeometryPlanner.TryGetCellBounds(");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(parts.Aggregate(root, static (path, part) => Path.Combine(path, part)));
}
