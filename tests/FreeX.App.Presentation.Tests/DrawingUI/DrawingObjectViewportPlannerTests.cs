using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectViewportPlannerTests
{
    [Fact]
    public void TryCreateAnchorRect_MapsTwoCellAnchorAndEmuOffsetsToViewportPixels()
    {
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(3, 20, 0),
                new RowMetric(4, 20, 20),
                new RowMetric(5, 20, 40)
            ],
            [
                new ColMetric(2, 80, 0),
                new ColMetric(3, 80, 80),
                new ColMetric(4, 80, 160)
            ]);
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 95250, 2, 190500),
            new DrawingAnchorPoint(3, 47625, 4, 95250));

        var created = DrawingObjectViewportPlanner.TryCreateAnchorRect(
            viewport,
            anchor,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            out var rect);

        created.Should().BeTrue();
        rect.Should().Be(new LayoutRect(40, 38, 155, 30));
    }

    [Fact]
    public void TryCreateAnchoredObjectRect_AppliesOffsetsAndMinimumExtent()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(4, 20, 40)],
            [new ColMetric(3, 80, 160)]);
        var anchor = new CellAddress(SheetId.New(), 4, 3);

        var created = DrawingObjectViewportPlanner.TryCreateAnchoredObjectRect(
            viewport,
            anchor,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            width: 12,
            height: 6,
            minimumWidth: 24,
            minimumHeight: 18,
            out var rect,
            anchorOffsetX: 7,
            anchorOffsetY: 3);

        created.Should().BeTrue();
        rect.Should().Be(new LayoutRect(197, 61, 24, 18));
    }

    [Fact]
    public void TryCreateDisplayedObjectRect_UsesProjectedOffsetsWithZoomedHeaders()
    {
        var drawingObject = new DrawingObjectBounds(
            SelectionPaneObjectKind.Picture,
            Guid.NewGuid(),
            "Picture 1",
            AnchorRow: 4,
            AnchorCol: 3,
            Left: 167,
            Top: 43,
            Width: 12,
            Height: 6);

        var created = DrawingObjectViewportPlanner.TryCreateDisplayedObjectRect(
            drawingObject,
            rowHeaderWidth: 60,
            columnHeaderHeight: 36,
            zoomFactor: 2,
            out var rect);

        created.Should().BeTrue();
        rect.Should().Be(new LayoutRect(394, 122, 24, 12));
    }

    [Fact]
    public void IntersectsViewport_UsesRotatedBoundsWhenObjectStartsOutsideViewport()
    {
        var rect = new LayoutRect(105, 10, 20, 100);

        DrawingObjectViewportPlanner.IntersectsViewport(
                rect,
                rotationDegrees: 0,
                visibleRight: 100,
                visibleBottom: 100)
            .Should()
            .BeFalse();

        DrawingObjectViewportPlanner.IntersectsViewport(
                rect,
                rotationDegrees: 45,
                visibleRight: 100,
                visibleBottom: 100)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void GetRenderableAnchorBounds_StopsAtVisibleViewportEdges()
    {
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40)
            ],
            [
                new ColMetric(1, 50, 0),
                new ColMetric(2, 50, 50),
                new ColMetric(3, 50, 100)
            ]);

        var bounds = DrawingObjectViewportPlanner.GetRenderableAnchorBounds(
            viewport,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            visibleRight: 120,
            visibleBottom: 55);

        bounds.Should().Be(new DrawingViewportAnchorBounds(2, 2));
        DrawingObjectViewportPlanner.CanAnchoredObjectReachViewport(
                new CellAddress(SheetId.New(), 2, 2),
                bounds)
            .Should()
            .BeTrue();
        DrawingObjectViewportPlanner.CanAnchoredObjectReachViewport(
                new CellAddress(SheetId.New(), 3, 3),
                bounds)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void PaintMetadata_ResolvesShapeAndTextBoxDefaults()
    {
        var theme = WorkbookTheme.Office;

        var shapePaint = DrawingObjectViewportPlanner.ResolveDrawingShapePaint(
            new DrawingShapeModel { HasFill = false, OutlineHasNoFill = true },
            theme);
        var textBoxPaint = DrawingObjectViewportPlanner.ResolveTextBoxPaint(
            new TextBoxModel { HasFill = false },
            theme);

        shapePaint.Fill.Should().Be(DrawingShapeModel.ResolveDefaultFillColor(theme));
        shapePaint.Outline.Should().Be(DrawingShapeModel.ResolveDefaultOutlineColor(theme));
        shapePaint.HasFill.Should().BeFalse();
        shapePaint.HasOutline.Should().BeFalse();
        textBoxPaint.Fill.Should().Be(CellColor.White);
        textBoxPaint.Outline.Should().Be(new CellColor(89, 89, 89));
        textBoxPaint.HasFill.Should().BeFalse();
        textBoxPaint.HasOutline.Should().BeTrue();
    }
}
