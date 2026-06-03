using System;
using System.IO;
using System.Reflection;
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
        RunOnStaThread(() =>
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
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 24, 0), new RowMetric(2, 24, 24)],
                    [new ColMetric(1, 80, 0), new ColMetric(2, 80, 80)]),
                Pictures = [picture]
            };

            grid.TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect)
                .Should().BeTrue();

            var hitTestDrawingObject = typeof(GridView).GetMethod(
                "HitTestDrawingObject",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var hit = hitTestDrawingObject!.Invoke(grid, [new Point(rect.Left + 10, rect.Top + 10)]);
            hit!.GetType().GetField("Item1")!.GetValue(hit).Should().Be(picture.Id);
            hit.GetType().GetField("Item2")!.GetValue(hit).Should().Be(ObjectKind.Picture);

            var hitTestObjectHandle = typeof(GridView).GetMethod(
                "HitTestObjectHandle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            hitTestObjectHandle!.Invoke(grid, [new Point(rect.Right, rect.Bottom), rect])
                .Should()
                .Match<object>(value => value.ToString() == "ResizeSE");
            hitTestObjectHandle.Invoke(grid, [new Point(rect.Left + 10, rect.Top + 10), rect])
                .Should()
                .Match<object>(value => value.ToString() == "Move");
        });
    }

    [Fact]
    public void DrawingObjectHitTesting_IncludesRenderedBodyBoundary()
    {
        RunOnStaThread(() =>
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
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 24, 0), new RowMetric(2, 24, 24)],
                    [new ColMetric(1, 80, 0), new ColMetric(2, 80, 80)]),
                Pictures = [picture]
            };

            grid.TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect)
                .Should().BeTrue();

            var hitTestDrawingObject = typeof(GridView).GetMethod(
                "HitTestDrawingObject",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var hit = hitTestDrawingObject!.Invoke(grid, [new Point(rect.Right, rect.Bottom)]);

            hit!.GetType().GetField("Item1")!.GetValue(hit).Should().Be(picture.Id);
            hit.GetType().GetField("Item2")!.GetValue(hit).Should().Be(ObjectKind.Picture);
        });
    }

    [Fact]
    public void DrawingObjectHitTesting_HonorsPictureRotation()
    {
        RunOnStaThread(() =>
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
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 24, 0), new RowMetric(2, 24, 24)],
                    [new ColMetric(1, 80, 0), new ColMetric(2, 80, 80)]),
                Pictures = [picture]
            };

            grid.TryCreateAnchoredObjectRect(picture.Anchor, picture.Width, picture.Height, 24, 18, out var rect)
                .Should().BeTrue();

            var hitTestDrawingObject = typeof(GridView).GetMethod(
                "HitTestDrawingObject",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var centerHit = hitTestDrawingObject!.Invoke(grid, [new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2)]);
            var cornerHit = hitTestDrawingObject.Invoke(grid, [new Point(rect.Left + 5, rect.Top + 5)]);

            centerHit!.GetType().GetField("Item1")!.GetValue(centerHit).Should().Be(picture.Id);
            cornerHit!.GetType().GetField("Item1")!.GetValue(cornerHit).Should().Be(Guid.Empty);
        });
    }

    [Fact]
    public void DrawingObjectHitTesting_ChoosesTopmostRenderedObject()
    {
        RunOnStaThread(() =>
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
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 24, 0), new RowMetric(2, 24, 24)],
                    [new ColMetric(1, 80, 0), new ColMetric(2, 80, 80)]),
                DrawingShapes = [shape],
                Pictures = [backPicture, frontPicture]
            };

            grid.TryCreateAnchoredObjectRect(anchor, frontPicture.Width, frontPicture.Height, 24, 18, out var rect)
                .Should().BeTrue();

            var hitTestDrawingObject = typeof(GridView).GetMethod(
                "HitTestDrawingObject",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var hit = hitTestDrawingObject!.Invoke(grid, [new Point(rect.Left + 10, rect.Top + 10)]);

            hit!.GetType().GetField("Item1")!.GetValue(hit).Should().Be(frontPicture.Id);
            hit.GetType().GetField("Item2")!.GetValue(hit).Should().Be(ObjectKind.Picture);
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
        RunOnStaThread(() =>
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

            var getSelectedObjectAnchor = typeof(GridView).GetMethod(
                "GetSelectedObjectAnchor",
                BindingFlags.Instance | BindingFlags.NonPublic);

            getSelectedObjectAnchor!.Invoke(grid, [])
                .Should()
                .Be(selected.Anchor);
        });
    }
}
