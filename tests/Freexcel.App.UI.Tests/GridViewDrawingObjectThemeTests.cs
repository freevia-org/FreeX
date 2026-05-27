using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewDrawingObjectThemeTests
{
    [Fact]
    public void ResolveDrawingShapeColors_UsesThemeReferences()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(10, 20, 30));
        var shape = new DrawingShapeModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.5),
            FillColor = new CellColor(1, 1, 1),
            OutlineColor = new CellColor(2, 2, 2)
        };

        var colors = GridView.ResolveDrawingShapeColors(shape, theme);

        colors.Fill.Should().Be(new CellColor(178, 202, 228));
        colors.Outline.Should().Be(new CellColor(5, 10, 15));
    }

    [Fact]
    public void ResolveTextBoxColors_UsesThemeReferences()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(10, 20, 30));
        var textBox = new TextBoxModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5),
            FillColor = new CellColor(1, 1, 1),
            OutlineColor = new CellColor(2, 2, 2)
        };

        var colors = GridView.ResolveTextBoxColors(textBox, theme);

        colors.Fill.Should().Be(new CellColor(178, 202, 228));
        colors.Outline.Should().Be(new CellColor(5, 10, 15));
    }

    [Fact]
    public void CreateObjectPlaceholderLabel_UsesObjectNameOrExcelLikeFallback()
    {
        GridView.CreateObjectPlaceholderLabel("Picture", "  Logo  ", 3).Should().Be("Logo");
        GridView.CreateObjectPlaceholderLabel("Picture", "", 1).Should().Be("Picture");
        GridView.CreateObjectPlaceholderLabel("Picture", null, 3).Should().Be("Picture 3");
    }

    [Fact]
    public void TryCreateDrawingAnchorRect_MapsTwoCellAnchorToViewportPixels()
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

        var created = GridView.TryCreateDrawingAnchorRect(
            viewport,
            anchor,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            out var rect);

        created.Should().BeTrue();
        rect.Should().Be(new Rect(40, 38, 155, 30));
    }

    [Fact]
    public void TryCreateDrawingAnchorRect_UsesFirstMatchingAnchorMetrics()
    {
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(3, 20, 0),
                new RowMetric(5, 20, 40),
                new RowMetric(3, 20, 200)
            ],
            [
                new ColMetric(2, 80, 0),
                new ColMetric(4, 80, 160),
                new ColMetric(2, 80, 300)
            ]);
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 0, 2, 0),
            new DrawingAnchorPoint(3, 0, 4, 0));

        GridView.TryCreateDrawingAnchorRect(
                viewport,
                anchor,
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                out var rect)
            .Should()
            .BeTrue();
        rect.Should().Be(new Rect(30, 18, 160, 40));
    }

    [Fact]
    public void TryCreateDrawingAnchorRect_ReturnsFalseForMaxValueAnchorPoint()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 80, 0)]);
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(uint.MaxValue, 0, 0, 0),
            new DrawingAnchorPoint(0, 0, 0, 0));

        GridView.TryCreateDrawingAnchorRect(
                viewport,
                anchor,
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryCreateDrawingAnchorRect_UsesSinglePassAnchorMetricLookups()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridDrawingObjectPlanner.cs"));
        var anchorRange = source[
            source.IndexOf("public static bool TryCreateDrawingAnchorRect", StringComparison.Ordinal)..
            source.IndexOf("public static bool TryCreateAnchoredObjectRect", StringComparison.Ordinal)];
        var anchorHelpers = source[
            source.IndexOf("private static bool TryGetAnchorPoints", StringComparison.Ordinal)..
            source.IndexOf("private static double EmusToPixels", StringComparison.Ordinal)];

        anchorRange.Should().Contain("TryGetAnchorPoints(viewport, anchor");
        anchorRange.Should().NotContain("TryGetAnchorPoint(viewport, anchor.From");
        anchorRange.Should().NotContain("TryGetAnchorPoint(viewport, anchor.To");
        anchorHelpers.Should().Contain("TryFindAnchorColumns(viewport.ColMetrics");
        anchorHelpers.Should().Contain("TryFindAnchorRows(viewport.RowMetrics");
        anchorHelpers.Should().Contain("foreach (var metric in metrics)");
        anchorHelpers.Should().NotContain("FirstOrDefault");
        anchorHelpers.Should().NotContain(".Where(");
        anchorHelpers.Should().NotContain(".ToList()");
    }

    [Fact]
    public void AnchoredObjectRendering_UsesSharedSinglePassMetricPlanner()
    {
        var planner = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridDrawingObjectPlanner.cs"));
        var drawingObjects = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.DrawingObjects.cs"));
        var pictures = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var plannerMethod = planner[
            planner.IndexOf("public static bool TryCreateAnchoredObjectRect", StringComparison.Ordinal)..
            planner.IndexOf("public static string GetNativeControlCaption", StringComparison.Ordinal)];
        var renderTextBoxes = drawingObjects[
            drawingObjects.IndexOf("private void RenderTextBoxes", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)];
        var renderDrawingShapes = drawingObjects[
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)];
        var renderPictures = pictures[
            pictures.IndexOf("private void RenderPictures", StringComparison.Ordinal)..
            pictures.IndexOf("private void DrawPictureSelectionAdorner", StringComparison.Ordinal)];

        plannerMethod.Should().Contain("TryFindAnchorRow(viewport.RowMetrics, anchor.Row");
        plannerMethod.Should().Contain("TryFindAnchorColumn(viewport.ColMetrics, anchor.Col");
        plannerMethod.Should().NotContain("FirstOrDefault");
        renderTextBoxes.Should().Contain("TryCreateAnchoredObjectRect(textBox.Anchor");
        renderTextBoxes.Should().NotContain("FirstOrDefault");
        renderDrawingShapes.Should().Contain("TryCreateAnchoredObjectRect(shape.Anchor");
        renderDrawingShapes.Should().NotContain("FirstOrDefault");
        renderPictures.Should().Contain("TryCreateAnchoredObjectRect(picture.Anchor");
        renderPictures.Should().NotContain("FirstOrDefault");
    }

    [Fact]
    public void GridView_ExposesObjectDisplayModeForExcelPlaceholderRendering()
    {
        var source =
            File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.cs")) +
            File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.RenderDispatch.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Properties.cs"));

        source.Should().Contain("public enum GridObjectDisplayMode");
        propertiesSource.Should().Contain("ObjectDisplayModeProperty");
        source.Should().Contain("RenderObjectPlaceholders(dc)");
        source.Should().Contain("RenderCharts(dc)");
    }

    [Fact]
    public void PictureRenderer_DrawsSelectionAdornerForPictureAtActiveCell()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.DrawingObjects.Pictures.cs"));

        source.Should().Contain("DrawPictureSelectionAdorner");
        source.Should().Contain("SelectedRange?.Start != picture.Anchor");
        source.Should().Contain("dc.DrawRectangle(null, PictureSelectionPen, rect);");
    }

    [Fact]
    public void PictureRenderer_ReusesFrozenStaticResources()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var renderStart = source.IndexOf("private void RenderPictures", StringComparison.Ordinal);
        var renderEnd = source.IndexOf("private static bool HasPictureCrop", StringComparison.Ordinal);
        renderStart.Should().BeGreaterThanOrEqualTo(0);
        renderEnd.Should().BeGreaterThan(renderStart);
        var renderPictures = source[
            renderStart..
            renderEnd];

        GetStaticResource<Pen>("PictureBorderPen").IsFrozen.Should().BeTrue();
        GetStaticResource<Pen>("PictureGridPen").IsFrozen.Should().BeTrue();
        GetStaticResource<Brush>("PictureSelectionBrush").IsFrozen.Should().BeTrue();
        GetStaticResource<Pen>("PictureSelectionPen").IsFrozen.Should().BeTrue();
        source.Should().Contain("private static readonly Pen PictureBorderPen = CreateFrozenPen");
        source.Should().Contain("private static readonly Pen PictureGridPen = CreateFrozenPen");
        source.Should().Contain("private static readonly Brush PictureSelectionBrush = MakeBrush");
        source.Should().Contain("private static readonly Pen PictureSelectionPen = CreateFrozenPen");
        renderPictures.Should().NotContain("new Pen(new SolidColorBrush");
        renderPictures.Should().NotContain("new SolidColorBrush");
    }

    [Fact]
    public void CommentMarkerRenderer_PaintsRedTriangleAtCellTopRight()
    {
        RunOnStaThread(() =>
        {
            var visual = new System.Windows.Media.DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                var drawCommentIndicator = typeof(GridView).GetMethod(
                    "DrawCommentIndicator",
                    BindingFlags.Static | BindingFlags.NonPublic);
                drawCommentIndicator!.Invoke(null, [drawingContext, new Rect(30, 18, 60, 24)]);
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                120,
                80,
                96,
                96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var pixels = new byte[120 * 80 * 4];
            bitmap.CopyPixels(pixels, stride: 120 * 4, offset: 0);

            var redPixels = 0;
            for (var y = 18; y <= 26; y++)
            {
                for (var x = 82; x <= 90; x++)
                {
                    var offset = (y * 120 + x) * 4;
                    var blue = pixels[offset];
                    var green = pixels[offset + 1];
                    var red = pixels[offset + 2];
                    var alpha = pixels[offset + 3];
                    if (red > 180 && green < 110 && blue < 110 && alpha > 128)
                        redPixels++;
                }
            }

            redPixels.Should().BeGreaterThan(4, "commented cells must show a visible red top-right marker");
        });
    }

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
    public void GridObjectDragPlanner_CalculatesMoveResizeAndHandleTargets()
    {
        var start = new Rect(10, 20, 80, 40);

        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.Move,
                start,
                new Point(15, 25),
                new Point(35, 45))
            .Should()
            .Be(new Rect(30, 40, 80, 40));
        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.ResizeSE,
                start,
                new Point(90, 60),
                new Point(100, 75))
            .Should()
            .Be(new Rect(10, 20, 90, 55));
        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.ResizeE,
                start,
                new Point(90, 60),
                new Point(0, 60))
            .Width.Should().Be(8);
        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.ResizeS,
                start,
                new Point(90, 60),
                new Point(90, 10))
            .Height.Should().Be(8);

        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeSE);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Top + 10), start)
            .Should().Be(ObjectDragKind.ResizeE);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + 10, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeS);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + 10, start.Top + 10), start)
            .Should().Be(ObjectDragKind.Move);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left - 20, start.Top - 20), start)
            .Should().Be(ObjectDragKind.None);
    }

    [Fact]
    public void GridObjectDragPlanner_IncludesResizeHandleHitZoneBoundary()
    {
        var start = new Rect(10, 20, 80, 40);
        const double handleSize = 8;
        const double hitPadding = 4;
        const double pad = handleSize / 2 + hitPadding;

        GridObjectDragPlanner.HitTestHandle(
                new Point(start.Right + pad, start.Bottom),
                start,
                handleSize,
                hitPadding)
            .Should().Be(ObjectDragKind.ResizeSE);
        GridObjectDragPlanner.HitTestHandle(
                new Point(start.Right, start.Bottom + pad),
                start,
                handleSize,
                hitPadding)
            .Should().Be(ObjectDragKind.ResizeSE);
    }

    [Fact]
    public void GridObjectDragPlanner_HitTestsAnchorCellFromViewportMetrics()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(2, 20, 0), new RowMetric(3, 20, 20)],
            [new ColMetric(4, 80, 0), new ColMetric(5, 80, 80)]);

        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(30 + 80 + 10, 18 + 20 + 10),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new CellAddress(default, 3, 5));
        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(4, 4),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();
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

    [Fact]
    public void GridViewObjectDrag_DelegatesGeometryToPlanner()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Input.cs"));
        var dragSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.ObjectDrag.cs"));

        inputSource.Should().Contain("GridObjectDragPlanner.CalculateDragRect(");
        inputSource.Should().Contain("_objectDragStartAnchor = GetSelectedObjectAnchor() ?? HitTestAnchorCell(pos) ?? default;");
        dragSource.Should().Contain("GridObjectDragPlanner.HitTestHandle(pos, objRect, HandleSize, HandleHitPad)");
        dragSource.Should().Contain("GridObjectDragPlanner.HitTestAnchorCell(");
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

    private static T GetStaticResource<T>(string fieldName)
    {
        var field = typeof(GridView).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        return field!.GetValue(null).Should().BeAssignableTo<T>().Subject;
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
