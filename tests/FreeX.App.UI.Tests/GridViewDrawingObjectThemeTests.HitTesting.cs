using System;
using System.IO;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewDrawingObjectThemeTests
{
    [Fact]
    public void PictureHitTesting_MapsPictureBodyAndResizeHandleToObjectCommands()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var picture = new PictureModel
            {
                Id = Guid.NewGuid(),
                Anchor = new CellAddress(sheetId, 1, 1),
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var grid = new GridView
            {
                Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(),
                Pictures = [picture]
            };

            grid.TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect)
                .Should().BeTrue();

            var hit = GridViewTestHelpers.HitTestDrawingObject(grid, new Point(rect.Left + 10, rect.Top + 10));
            hit.Id.Should().Be(picture.Id);
            hit.Kind.Should().Be(ObjectKind.Picture);

            GridViewTestHelpers.HitTestObjectHandle(grid, new Point(rect.Right, rect.Bottom), rect)
                .Should()
                .Match<object>(value => value.ToString() == "ResizeSE");
            GridViewTestHelpers.HitTestObjectHandle(grid, new Point(rect.Left + 10, rect.Top + 10), rect)
                .Should()
                .Match<object>(value => value.ToString() == "Move");
        });
    }

    [Fact]
    public void DrawingObjectHitTesting_IncludesRenderedBodyBoundary()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var picture = new PictureModel
            {
                Id = Guid.NewGuid(),
                Anchor = new CellAddress(sheetId, 1, 1),
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var grid = new GridView
            {
                Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(),
                Pictures = [picture]
            };

            grid.TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect)
                .Should().BeTrue();

            var hit = GridViewTestHelpers.HitTestDrawingObject(grid, new Point(rect.Right, rect.Bottom));

            hit.Id.Should().Be(picture.Id);
            hit.Kind.Should().Be(ObjectKind.Picture);
        });
    }

    [Fact]
    public void DrawingObjectHitTesting_HonorsPictureRotation()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var picture = new PictureModel
            {
                Id = Guid.NewGuid(),
                Anchor = new CellAddress(sheetId, 1, 1),
                Width = 80,
                Height = 40,
                RotationDegrees = 90,
                IsVisible = true
            };
            var grid = new GridView
            {
                Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(),
                Pictures = [picture]
            };

            grid.TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect)
                .Should().BeTrue();

            var centerHit = GridViewTestHelpers.HitTestDrawingObject(
                grid,
                new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
            var cornerHit = GridViewTestHelpers.HitTestDrawingObject(grid, new Point(rect.Left + 5, rect.Top + 5));

            centerHit.Id.Should().Be(picture.Id);
            cornerHit.Id.Should().Be(Guid.Empty);
        });
    }

    [Fact]
    public void DrawingObjectHitTesting_ChoosesTopmostRenderedObject()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var anchor = new CellAddress(sheetId, 1, 1);
            var shape = new DrawingShapeModel
            {
                Id = Guid.NewGuid(),
                Anchor = anchor,
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var backPicture = new PictureModel
            {
                Id = Guid.NewGuid(),
                Anchor = anchor,
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var frontPicture = new PictureModel
            {
                Id = Guid.NewGuid(),
                Anchor = anchor,
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var grid = new GridView
            {
                Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(),
                DrawingShapes = [shape],
                Pictures = [backPicture, frontPicture]
            };

            grid.TryCreateAnchoredObjectRect(anchor, frontPicture.Width, frontPicture.Height, 24, 18, out var rect)
                .Should().BeTrue();

            var hit = GridViewTestHelpers.HitTestDrawingObject(grid, new Point(rect.Left + 10, rect.Top + 10));

            hit.Id.Should().Be(frontPicture.Id);
            hit.Kind.Should().Be(ObjectKind.Picture);
        });
    }

    [Fact]
    public void DrawingObjectHitTesting_UsesIndexedReverseLoops()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ObjectDrag.cs"));
        var hitTestBlock = source[
            source.IndexOf("private (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) HitTestDrawingObject", StringComparison.Ordinal)..
            source.IndexOf("private static bool ContainsInclusive", StringComparison.Ordinal)];

        hitTestBlock.Should().Contain("for (var i = TextBoxes.Count - 1; i >= 0; i--)");
        hitTestBlock.Should().Contain("for (var i = Pictures.Count - 1; i >= 0; i--)");
        hitTestBlock.Should().Contain("for (var i = DrawingShapes.Count - 1; i >= 0; i--)");
        hitTestBlock.Should().NotContain(".Reverse()");
    }

    [Fact]
    public void DrawingObjectHitTesting_UsesRotatedBodyChecks()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ObjectDrag.cs"));

        source.Should().Contain("ContainsRotatedInclusive(rect, pos, textBox.RotationDegrees)");
        source.Should().Contain("ContainsRotatedInclusive(rect, pos, picture.RotationDegrees)");
        source.Should().Contain("ContainsRotatedInclusive(rect, pos, shape.RotationDegrees)");
        source.Should().Contain("var radians = -rotationDegrees * Math.PI / 180.0;");
    }

    [Fact]
    public void SelectedDrawingObjectAnchor_UsesCurrentSelectedObject()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var first = new PictureModel
            {
                Id = Guid.NewGuid(),
                Anchor = new CellAddress(sheetId, 1, 1),
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var selected = new PictureModel
            {
                Id = Guid.NewGuid(),
                Anchor = new CellAddress(sheetId, 3, 4),
                Width = 80,
                Height = 40,
                IsVisible = true
            };
            var grid = new GridView
            {
                SelectedObjectId = selected.Id,
                SelectedObjectKind = ObjectKind.Picture,
                Pictures = [first, selected]
            };

            GridViewTestHelpers.GetSelectedObjectAnchor(grid).Should().Be(selected.Anchor);
        });
    }
}
