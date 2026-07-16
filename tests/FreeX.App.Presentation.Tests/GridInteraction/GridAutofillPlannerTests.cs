using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class GridAutofillPlannerTests
{
    [Fact]
    public void ConstrainTarget_PrefersVerticalAxisWhenDragExtendsFartherDown()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.ConstrainTarget(source, new CellAddress(sheet, 8, 6))
            .Should()
            .Be(new CellAddress(sheet, 8, 3));
    }

    [Fact]
    public void ConstrainTarget_PrefersHorizontalAxisWhenDragExtendsFartherRight()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.ConstrainTarget(source, new CellAddress(sheet, 5, 9))
            .Should()
            .Be(new CellAddress(sheet, 3, 9));
    }

    [Fact]
    public void ConstrainTarget_SupportsDraggingAboveOrLeftOfSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 4, 4),
            new CellAddress(sheet, 6, 6));

        GridAutofillPlanner.ConstrainTarget(source, new CellAddress(sheet, 1, 5))
            .Should()
            .Be(new CellAddress(sheet, 1, 6));

        GridAutofillPlanner.ConstrainTarget(source, new CellAddress(sheet, 5, 1))
            .Should()
            .Be(new CellAddress(sheet, 6, 1));
    }

    [Fact]
    public void ConstrainTarget_PrefersVerticalOnTie()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 2, 2));

        // Equal downward and rightward distance => vertical wins (>=).
        GridAutofillPlanner.ConstrainTarget(source, new CellAddress(sheet, 5, 5))
            .Should()
            .Be(new CellAddress(sheet, 5, 2));
    }

    [Fact]
    public void CalculateFillRange_ReturnsVerticalExtensionBelowSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 4));

        GridAutofillPlanner.CalculateFillRange(source, new CellAddress(sheet, 7, 4))
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 4, 2),
                new CellAddress(sheet, 7, 4)));
    }

    [Fact]
    public void CalculateFillRange_ReturnsHorizontalExtensionRightOfSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 4));

        GridAutofillPlanner.CalculateFillRange(source, new CellAddress(sheet, 3, 8))
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 2, 5),
                new CellAddress(sheet, 3, 8)));
    }

    [Fact]
    public void CalculateFillRange_ReturnsVerticalExtensionAboveSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 4, 2),
            new CellAddress(sheet, 6, 4));

        GridAutofillPlanner.CalculateFillRange(source, new CellAddress(sheet, 2, 4))
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 2, 2),
                new CellAddress(sheet, 3, 4)));
    }

    [Fact]
    public void CalculateFillRange_ReturnsHorizontalExtensionLeftOfSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 4),
            new CellAddress(sheet, 3, 6));

        GridAutofillPlanner.CalculateFillRange(source, new CellAddress(sheet, 3, 2))
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 2, 2),
                new CellAddress(sheet, 3, 3)));
    }

    [Fact]
    public void CalculateFillRange_ReturnsNullWhenTargetDoesNotExtendSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 4));

        GridAutofillPlanner.CalculateFillRange(source, new CellAddress(sheet, 3, 4))
            .Should()
            .BeNull();
        GridAutofillPlanner.CalculateFillRange(source, new CellAddress(sheet, 2, 3))
            .Should()
            .BeNull();
    }

    [Fact]
    public void CalculateCompletedSelectionRange_IncludesSourceAndVerticalFillBelow()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 4));
        var fillRange = new GridRange(
            new CellAddress(sheet, 4, 2),
            new CellAddress(sheet, 7, 4));

        GridAutofillPlanner.CalculateCompletedSelectionRange(source, fillRange)
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 2, 2),
                new CellAddress(sheet, 7, 4)));
    }

    [Fact]
    public void CalculateCompletedSelectionRange_IncludesSourceAndFillAboveOrLeft()
    {
        var sheet = SheetId.New();
        var verticalSource = new GridRange(
            new CellAddress(sheet, 4, 2),
            new CellAddress(sheet, 6, 4));
        var verticalFillRange = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 4));
        var horizontalSource = new GridRange(
            new CellAddress(sheet, 2, 4),
            new CellAddress(sheet, 3, 6));
        var horizontalFillRange = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.CalculateCompletedSelectionRange(verticalSource, verticalFillRange)
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 2, 2),
                new CellAddress(sheet, 6, 4)));
        GridAutofillPlanner.CalculateCompletedSelectionRange(horizontalSource, horizontalFillRange)
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 2, 2),
                new CellAddress(sheet, 3, 6)));
    }

    [Fact]
    public void CalculateEdgeScrollIntent_RequestsHorizontalScrollNearRightEdge()
    {
        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 795,
                pointerY: 120,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(1, 0));
    }

    [Fact]
    public void CalculateEdgeScrollIntent_IgnoresPointerAwayFromEdges()
    {
        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 400,
                pointerY: 300,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(0, 0));
    }

    [Fact]
    public void CalculateEdgeScrollIntent_IgnoresCollapsedContentArea()
    {
        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 48,
                pointerY: 24,
                width: 48,
                height: 24,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(0, 0));
    }

    [Fact]
    public void CalculateEdgeScrollIntent_ChoosesNearestEdgeWhenHotZonesOverlap()
    {
        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 54,
                pointerY: 30,
                width: 80,
                height: 48,
                rowHeaderWidth: 48,
                columnHeaderHeight: 18)
            .Should()
            .Be(new GridAutoScrollRequest(-1, -1));

        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 76,
                pointerY: 46,
                width: 80,
                height: 48,
                rowHeaderWidth: 48,
                columnHeaderHeight: 18)
            .Should()
            .Be(new GridAutoScrollRequest(1, 1));
    }

    [Fact]
    public void CalculateEdgeScrollIntent_ReturnsNoScrollForNonPositiveDimensions()
    {
        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 5,
                pointerY: 5,
                width: 0,
                height: 0,
                rowHeaderWidth: 0,
                columnHeaderHeight: 0)
            .Should()
            .Be(new GridAutoScrollRequest(0, 0));
    }

    [Fact]
    public void CalculateDragTarget_ReturnsFarthestVisibleCellWithinSourceAndPointerBounds()
    {
        var sheet = SheetId.New();
        var viewport = CreateViewport();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.CalculateDragTarget(
                viewport,
                source,
                new GridPoint(240, 130),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new CellAddress(default, 5, 5));
    }

    [Fact]
    public void CalculateDragTarget_IncludesCellWhoseMidpointIsExactlyOnPointerBoundary()
    {
        var sheet = SheetId.New();
        var viewport = CreateViewport();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.CalculateDragTarget(
                viewport,
                source,
                new GridPoint(170, 88),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new CellAddress(default, 4, 4));
    }

    [Fact]
    public void CalculateDragTarget_ReturnsFarthestVisibleCellWhenDraggingAboveOrLeft()
    {
        var sheet = SheetId.New();
        var viewport = CreateViewport();
        var source = new GridRange(
            new CellAddress(sheet, 3, 3),
            new CellAddress(sheet, 4, 4));

        GridAutofillPlanner.CalculateDragTarget(
                viewport,
                source,
                new GridPoint(120, 25),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new CellAddress(default, 1, 4));

        GridAutofillPlanner.CalculateDragTarget(
                viewport,
                source,
                new GridPoint(40, 90),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new CellAddress(default, 4, 1));
    }

    [Fact]
    public void CalculateDragTarget_ReturnsNullWhenSourceMetricsAreNotVisible()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 99, 2),
            new CellAddress(sheet, 100, 3));

        GridAutofillPlanner.CalculateDragTarget(
                CreateViewport(),
                source,
                new GridPoint(240, 130),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();
    }

    [Fact]
    public void CalculateDragTarget_UsesFirstMatchingSourceMetrics()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(4, 20, 60),
                new RowMetric(2, 20, 200)
            ],
            [
                new ColMetric(2, 40, 40),
                new ColMetric(3, 40, 80),
                new ColMetric(4, 40, 120),
                new ColMetric(2, 40, 300)
            ]);

        GridAutofillPlanner.CalculateDragTarget(
                viewport,
                source,
                new GridPoint(170, 100),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new CellAddress(default, 4, 4));
    }

    [Fact]
    public void IsOnHandle_ReturnsTrueForHandleCenterAndPaddedBoundary()
    {
        var sheet = SheetId.New();
        var selectedRange = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.IsOnHandle(
                CreateViewport(),
                selectedRange,
                new GridPoint(30 + 120 - 3 + 3, 18 + 60 - 3 + 3),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeTrue();
        GridAutofillPlanner.IsOnHandle(
                CreateViewport(),
                selectedRange,
                new GridPoint(30 + 120 - 6, 18 + 60 - 6),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeTrue("the hit test includes a 3px pad around the 6px handle");
    }

    [Fact]
    public void IsOnHandle_IncludesBottomRightPaddedBoundary()
    {
        var sheet = SheetId.New();
        var selectedRange = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.IsOnHandle(
                CreateViewport(),
                selectedRange,
                new GridPoint(30 + 120 + 3, 18 + 60 + 3),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeTrue("the rendered 6px handle includes the 3px padded bottom-right edge");
    }

    [Fact]
    public void IsOnHandle_ScalesMetricGeometryToMatchTheRenderedGrid()
    {
        var sheet = SheetId.New();
        var selectedRange = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.IsOnHandle(
                CreateViewport(),
                selectedRange,
                new GridPoint(210, 108),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                handleSize: 10,
                hitPadding: 6,
                metricScale: 1.5)
            .Should()
            .BeTrue("the hit target must follow the same zoomed bottom-right corner as the overlay handle");
    }

    [Fact]
    public void IsOnHandle_UsesRenderedHandleWhenEndMetricsAreDuplicated()
    {
        var sheet = SheetId.New();
        var selectedRange = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(3, 20, 200)
            ],
            [
                new ColMetric(2, 40, 40),
                new ColMetric(3, 40, 80),
                new ColMetric(3, 40, 300)
            ]);

        GridAutofillPlanner.IsOnHandle(
                viewport,
                selectedRange,
                new GridPoint(30 + 300 + 40, 18 + 200 + 20),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeTrue("the fill handle is rendered from the last visible end row and column metrics");

        GridAutofillPlanner.IsOnHandle(
                viewport,
                selectedRange,
                new GridPoint(30 + 80 + 40, 18 + 40 + 20),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse("the stale duplicate metric should not keep an invisible handle hot");
    }

    [Fact]
    public void IsOnHandle_ReturnsFalseAwayFromHandleOrWhenMetricsAreMissing()
    {
        var sheet = SheetId.New();
        var selectedRange = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.IsOnHandle(
                CreateViewport(),
                selectedRange,
                new GridPoint(30 + 120 + 10, 18 + 60 + 10),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();
        GridAutofillPlanner.IsOnHandle(
                null,
                selectedRange,
                new GridPoint(30 + 120, 18 + 60),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();
        GridAutofillPlanner.IsOnHandle(
                CreateViewport(),
                null,
                new GridPoint(30 + 120, 18 + 60),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();
        GridAutofillPlanner.IsOnHandle(
                CreateViewport(),
                new GridRange(new CellAddress(sheet, 99, 2), new CellAddress(sheet, 99, 3)),
                new GridPoint(30 + 120, 18 + 60),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();
        GridAutofillPlanner.IsOnHandle(
                CreateViewport(),
                new GridRange(new CellAddress(sheet, 2, 99), new CellAddress(sheet, 3, 99)),
                new GridPoint(30 + 120, 18 + 60),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();
    }

    private static ViewportModel CreateViewport() =>
        new(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(4, 20, 60),
                new RowMetric(5, 20, 80)
            ],
            [
                new ColMetric(1, 40, 0),
                new ColMetric(2, 40, 40),
                new ColMetric(3, 40, 80),
                new ColMetric(4, 40, 120),
                new ColMetric(5, 40, 160)
            ]);
}
