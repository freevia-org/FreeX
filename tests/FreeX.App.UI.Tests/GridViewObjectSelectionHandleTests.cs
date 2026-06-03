using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
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
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var id = Guid.NewGuid();
            var anchor = new CellAddress(sheetId, 1, 1);
            var grid = CreateGridWithSelectedObject(kind, id, anchor, width: 3, height: 2, isVisible: true);

            var rect = GridViewTestHelpers.GetSelectedObjectRect(grid);

            rect.Left.Should().Be(GridView.RowHeaderWidth);
            rect.Top.Should().Be(GridView.ColHeaderHeight);
            rect.Width.Should().Be(expectedWidth);
            rect.Height.Should().Be(expectedHeight);
        });
    }

    [Fact]
    public void HiddenSelectedObject_DoesNotExposeSelectionHandleGeometry()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var id = Guid.NewGuid();
            var anchor = new CellAddress(sheetId, 1, 1);
            var grid = CreateGridWithSelectedObject(ObjectKind.Shape, id, anchor, width: 80, height: 40, isVisible: false);

            GridViewTestHelpers.GetSelectedObjectRect(grid).Should().Be(Rect.Empty);
            GridViewTestHelpers.GetSelectedObjectAnchor(grid).Should().BeNull();
        });
    }

    [Fact]
    public void ObjectDisplayModeNothing_DisablesObjectHitTestingAndSelectionHandles()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var id = Guid.NewGuid();
            var anchor = new CellAddress(sheetId, 1, 1);
            var grid = CreateGridWithSelectedObject(ObjectKind.Picture, id, anchor, width: 80, height: 40, isVisible: true);
            grid.ObjectDisplayMode = GridObjectDisplayMode.Nothing;

            var hit = GridViewTestHelpers.HitTestDrawingObject(
                grid,
                new Point(GridView.RowHeaderWidth + 4, GridView.ColHeaderHeight + 4));

            hit.Id.Should().Be(Guid.Empty);
            hit.Kind.Should().Be(ObjectKind.None);
            GridViewTestHelpers.GetSelectedObjectRect(grid).Should().Be(Rect.Empty);
            GridViewTestHelpers.GetSelectedObjectAnchor(grid).Should().BeNull();
        });
    }

    [Fact]
    public void HitTestDrawingObject_UsesExplicitMixedDrawingObjectZOrder()
    {
        WpfTestThread.Run(() =>
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
                Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(),
                Pictures = [picture],
                DrawingShapes = [shape],
                DrawingObjectZOrder =
                [
                    new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id),
                    new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id)
                ]
            };

            var hit = GridViewTestHelpers.HitTestDrawingObject(
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
            Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(anchor.Row, anchor.Col),
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
}
