using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class GridShapePlacementPlannerTests
{
    [Fact]
    public void CreateRequest_ClickUsesExcelStyleDefaultShapeSize()
    {
        var anchor = new CellAddress(new SheetId(Guid.NewGuid()), 4, 3);

        var request = GridShapePlacementPlanner.CreateRequest(
            DrawingShapeKind.Rectangle,
            anchor,
            new Point(10, 20),
            new Point(11, 22));

        request.Kind.Should().Be(DrawingShapeKind.Rectangle);
        request.Anchor.Should().Be(anchor);
        request.Width.Should().Be(GridShapePlacementPlanner.DefaultShapeWidth);
        request.Height.Should().Be(GridShapePlacementPlanner.DefaultShapeHeight);
    }

    [Fact]
    public void CreateRequest_DragUsesDrawnSizeWithMinimumClamp()
    {
        var anchor = new CellAddress(new SheetId(Guid.NewGuid()), 4, 3);

        var request = GridShapePlacementPlanner.CreateRequest(
            DrawingShapeKind.Ellipse,
            anchor,
            new Point(100, 80),
            new Point(103, 140));

        request.Kind.Should().Be(DrawingShapeKind.Ellipse);
        request.Width.Should().Be(GridShapePlacementPlanner.MinimumShapeSize);
        request.Height.Should().Be(60);
    }

    [Fact]
    public void CalculatePreviewRect_NormalizesReverseDragToTopLeft()
    {
        var rect = GridShapePlacementPlanner.CalculatePreviewRect(
            new Point(120, 90),
            new Point(40, 50));

        rect.Should().Be(new Rect(40, 50, 80, 40));
    }

    [Fact]
    public void TextBoxCreateRequest_ClickUsesDefaultTextBoxSize()
    {
        var anchor = new CellAddress(new SheetId(Guid.NewGuid()), 5, 6);

        var request = GridTextBoxPlacementPlanner.CreateRequest(
            anchor,
            new Point(20, 30),
            new Point(22, 33));

        request.Anchor.Should().Be(anchor);
        request.Width.Should().Be(GridTextBoxPlacementPlanner.DefaultTextBoxWidth);
        request.Height.Should().Be(GridTextBoxPlacementPlanner.DefaultTextBoxHeight);
    }

    [Fact]
    public void TextBoxCreateRequest_DragUsesDrawnSizeWithMinimumClamp()
    {
        var anchor = new CellAddress(new SheetId(Guid.NewGuid()), 5, 6);

        var request = GridTextBoxPlacementPlanner.CreateRequest(
            anchor,
            new Point(50, 60),
            new Point(52, 120));

        request.Width.Should().Be(GridTextBoxPlacementPlanner.MinimumTextBoxSize);
        request.Height.Should().Be(60);
    }

    [Fact]
    public void TextBoxCalculateAnchorPoint_ReverseDragUsesTopLeft()
    {
        var anchorPoint = GridTextBoxPlacementPlanner.CalculateAnchorPoint(
            new Point(120, 90),
            new Point(40, 50));

        anchorPoint.Should().Be(new Point(40, 50));
    }

    [Fact]
    public void GridView_BeginShapePlacementClearsSelectedObjectAndCanCancel()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                SelectedObjectId = Guid.NewGuid(),
                SelectedObjectKind = ObjectKind.Shape
            };

            grid.BeginShapePlacement(DrawingShapeKind.Line);

            grid.IsShapePlacementPending.Should().BeTrue();
            grid.SelectedObjectId.Should().Be(Guid.Empty);
            grid.SelectedObjectKind.Should().Be(ObjectKind.None);
            grid.Cursor.Should().Be(System.Windows.Input.Cursors.Cross);

            grid.CancelShapePlacement();

            grid.IsShapePlacementPending.Should().BeFalse();
            grid.Cursor.Should().BeNull();
        });
    }

    [Fact]
    public void GridView_BeginTextBoxPlacementClearsSelectedObjectAndCanCancel()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                SelectedObjectId = Guid.NewGuid(),
                SelectedObjectKind = ObjectKind.TextBox
            };

            grid.BeginTextBoxPlacement();

            grid.IsTextBoxPlacementPending.Should().BeTrue();
            grid.SelectedObjectId.Should().Be(Guid.Empty);
            grid.SelectedObjectKind.Should().Be(ObjectKind.None);
            grid.Cursor.Should().Be(System.Windows.Input.Cursors.Cross);

            grid.CancelTextBoxPlacement();

            grid.IsTextBoxPlacementPending.Should().BeFalse();
            grid.Cursor.Should().BeNull();
        });
    }

    [Fact]
    public void GridView_CommitShapePlacementFiresPlacementRequestAndClearsPendingMode()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(2, 20, 0), new RowMetric(3, 20, 20)],
                    [new ColMetric(4, 80, 0), new ColMetric(5, 80, 80)])
            };
            ShapePlacementRequest? placement = null;
            grid.ShapePlacementRequested += request => placement = request;

            grid.BeginShapePlacement(DrawingShapeKind.Rectangle);
            InvokePrivate<bool>(
                    grid,
                    "TryBeginShapePlacement",
                    new Point(GridView.RowHeaderWidth + 10, GridView.ColHeaderHeight + 10))
                .Should()
                .BeTrue();
            InvokePrivate<object?>(
                grid,
                "CommitShapePlacement",
                new Point(GridView.RowHeaderWidth + 130, GridView.ColHeaderHeight + 60));

            placement.Should().NotBeNull();
            placement!.Value.Kind.Should().Be(DrawingShapeKind.Rectangle);
            placement.Value.Anchor.Should().Be(new CellAddress(default, 2, 4));
            placement.Value.Width.Should().Be(120);
            placement.Value.Height.Should().Be(50);
            grid.IsShapePlacementPending.Should().BeFalse();
        });
    }

    [Fact]
    public void GridView_CommitTextBoxPlacementFiresPlacementRequestAndClearsPendingMode()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(2, 20, 0), new RowMetric(3, 20, 20)],
                    [new ColMetric(4, 80, 0), new ColMetric(5, 80, 80)])
            };
            TextBoxPlacementRequest? placement = null;
            grid.TextBoxPlacementRequested += request => placement = request;

            grid.BeginTextBoxPlacement();
            InvokePrivate<bool>(
                    grid,
                    "TryBeginTextBoxPlacement",
                    new Point(GridView.RowHeaderWidth + 10, GridView.ColHeaderHeight + 10))
                .Should()
                .BeTrue();
            InvokePrivate<object?>(
                grid,
                "CommitTextBoxPlacement",
                new Point(GridView.RowHeaderWidth + 150, GridView.ColHeaderHeight + 70));

            placement.Should().NotBeNull();
            placement!.Value.Anchor.Should().Be(new CellAddress(default, 2, 4));
            placement.Value.Width.Should().Be(140);
            placement.Value.Height.Should().Be(60);
            grid.IsTextBoxPlacementPending.Should().BeFalse();
        });
    }

    private static T InvokePrivate<T>(GridView grid, string methodName, params object[] arguments)
    {
        var method = typeof(GridView).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (T)method!.Invoke(grid, arguments)!;
    }
}
