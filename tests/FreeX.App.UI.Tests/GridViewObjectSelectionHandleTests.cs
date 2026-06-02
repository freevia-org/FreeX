using System.Reflection;
using System.Threading;
using System.Windows;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class GridViewObjectSelectionHandleTests
{
    [Theory]
    [InlineData(ObjectKind.Picture, 24, 18)]
    [InlineData(ObjectKind.TextBox, 24, 18)]
    [InlineData(ObjectKind.Shape, 8, 8)]
    public void SelectedObjectRect_UsesRenderedMinimumBounds(ObjectKind kind, double expectedWidth, double expectedHeight)
    {
        RunOnStaThread(() =>
        {
            var sheetId = SheetId.New();
            var id = Guid.NewGuid();
            var anchor = new CellAddress(sheetId, 1, 1);
            var grid = CreateGridWithSelectedObject(kind, id, anchor, width: 3, height: 2, isVisible: true);

            var rect = InvokeSelectedObjectRect(grid);

            rect.Left.Should().Be(GridView.RowHeaderWidth);
            rect.Top.Should().Be(GridView.ColHeaderHeight);
            rect.Width.Should().Be(expectedWidth);
            rect.Height.Should().Be(expectedHeight);
        });
    }

    [Fact]
    public void HiddenSelectedObject_DoesNotExposeSelectionHandleGeometry()
    {
        RunOnStaThread(() =>
        {
            var sheetId = SheetId.New();
            var id = Guid.NewGuid();
            var anchor = new CellAddress(sheetId, 1, 1);
            var grid = CreateGridWithSelectedObject(ObjectKind.Shape, id, anchor, width: 80, height: 40, isVisible: false);

            InvokeSelectedObjectRect(grid).Should().Be(Rect.Empty);
            InvokeSelectedObjectAnchor(grid).Should().BeNull();
        });
    }

    [Fact]
    public void ObjectDisplayModeNothing_DisablesObjectHitTestingAndSelectionHandles()
    {
        RunOnStaThread(() =>
        {
            var sheetId = SheetId.New();
            var id = Guid.NewGuid();
            var anchor = new CellAddress(sheetId, 1, 1);
            var grid = CreateGridWithSelectedObject(ObjectKind.Picture, id, anchor, width: 80, height: 40, isVisible: true);
            grid.ObjectDisplayMode = GridObjectDisplayMode.Nothing;

            var hit = InvokeHitTestDrawingObject(grid, new Point(GridView.RowHeaderWidth + 4, GridView.ColHeaderHeight + 4));

            hit.Id.Should().Be(Guid.Empty);
            hit.Kind.Should().Be(ObjectKind.None);
            InvokeSelectedObjectRect(grid).Should().Be(Rect.Empty);
            InvokeSelectedObjectAnchor(grid).Should().BeNull();
        });
    }

    [Fact]
    public void HitTestDrawingObject_UsesExplicitMixedDrawingObjectZOrder()
    {
        RunOnStaThread(() =>
        {
            var sheetId = SheetId.New();
            var anchor = new CellAddress(sheetId, 1, 1);
            var picture = new PictureModel
            {
                Anchor = anchor,
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var shape = new DrawingShapeModel
            {
                Anchor = anchor,
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 24, 0), new RowMetric(2, 24, 24)],
                    [new ColMetric(1, 80, 0), new ColMetric(2, 80, 80)]),
                Pictures = [picture],
                DrawingShapes = [shape],
                DrawingObjectZOrder =
                [
                    new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id),
                    new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id)
                ]
            };

            var hit = InvokeHitTestDrawingObject(
                grid,
                new Point(GridView.RowHeaderWidth + 10, GridView.ColHeaderHeight + 10));

            hit.Id.Should().Be(shape.Id);
            hit.Kind.Should().Be(ObjectKind.Shape);
        });
    }

    private static GridView CreateGridWithSelectedObject(
        ObjectKind kind,
        Guid id,
        CellAddress anchor,
        double width,
        double height,
        bool isVisible)
    {
        var grid = new GridView
        {
            Viewport = new ViewportModel(
                [],
                [new RowMetric(anchor.Row, 24, 0), new RowMetric(anchor.Row + 1, 24, 24)],
                [new ColMetric(anchor.Col, 80, 0), new ColMetric(anchor.Col + 1, 80, 80)]),
            SelectedObjectId = id,
            SelectedObjectKind = kind
        };

        switch (kind)
        {
            case ObjectKind.Picture:
                grid.Pictures =
                [
                    new PictureModel
                    {
                        Id = id,
                        Anchor = anchor,
                        Width = width,
                        Height = height,
                        IsVisible = isVisible
                    }
                ];
                break;
            case ObjectKind.Shape:
                grid.DrawingShapes =
                [
                    new DrawingShapeModel
                    {
                        Id = id,
                        Anchor = anchor,
                        Width = width,
                        Height = height,
                        IsVisible = isVisible
                    }
                ];
                break;
            case ObjectKind.TextBox:
                grid.TextBoxes =
                [
                    new TextBoxModel
                    {
                        Id = id,
                        Anchor = anchor,
                        Width = width,
                        Height = height,
                        IsVisible = isVisible
                    }
                ];
                break;
        }

        return grid;
    }

    private static Rect InvokeSelectedObjectRect(GridView grid)
    {
        var method = typeof(GridView).GetMethod("GetSelectedObjectRect", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(grid, [])!.Should().BeOfType<Rect>().Subject;
    }

    private static CellAddress? InvokeSelectedObjectAnchor(GridView grid)
    {
        var method = typeof(GridView).GetMethod("GetSelectedObjectAnchor", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(grid, []) as CellAddress?;
    }

    private static (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) InvokeHitTestDrawingObject(GridView grid, Point point)
    {
        var method = typeof(GridView).GetMethod("HitTestDrawingObject", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return ((Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor))method!.Invoke(grid, [point])!;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null)
            throw exception;
    }
}
