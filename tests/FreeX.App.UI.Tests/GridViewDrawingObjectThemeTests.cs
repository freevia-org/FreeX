using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

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
            "src", "FreeX.App.UI", "GridDrawingObjectPlanner.cs"));
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
        anchorHelpers.Should().Contain("if (metric.Col > toColumn)");
        anchorHelpers.Should().Contain("if (metric.Row > toRow)");
        anchorHelpers.Should().Contain("break;");
        anchorHelpers.Should().NotContain("FirstOrDefault");
        anchorHelpers.Should().NotContain(".Where(");
        anchorHelpers.Should().NotContain(".ToList()");
    }

    [Fact]
    public void AnchoredObjectRendering_UsesSharedSinglePassMetricPlanner()
    {
        var planner = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridDrawingObjectPlanner.cs"));
        var drawingObjects = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var pictures = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var plannerMethod = planner[
            planner.IndexOf("public static bool TryCreateAnchoredObjectRect", StringComparison.Ordinal)..
            planner.IndexOf("public static string GetNativeControlCaption", StringComparison.Ordinal)];
        var anchorHelpers = planner[
            planner.IndexOf("private static bool TryFindAnchorRow", StringComparison.Ordinal)..
            planner.IndexOf("private static double EmusToPixels", StringComparison.Ordinal)];
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
        anchorHelpers.Should().Contain("if (metric.Row > row)");
        anchorHelpers.Should().Contain("if (metric.Col > column)");
        anchorHelpers.Should().Contain("break;");
        renderTextBoxes.Should().Contain("TryCreateAnchoredObjectRect(textBox.Anchor");
        renderTextBoxes.Should().NotContain("FirstOrDefault");
        renderDrawingShapes.Should().Contain("TryCreateAnchoredObjectRect(shape.Anchor");
        renderDrawingShapes.Should().NotContain("FirstOrDefault");
        renderPictures.Should().Contain("TryCreateAnchoredObjectRect(picture.Anchor");
        renderPictures.Should().NotContain("FirstOrDefault");
    }

    [Fact]
    public void DrawingObjectRendering_ReusesThemeEffectWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var renderTextBoxes = source[
            source.IndexOf("private void RenderTextBoxes", StringComparison.Ordinal)..
            source.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)];
        var renderDrawingShapes = source[
            source.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)..
            source.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)];
        var drawTextBoxEffect = source[
            source.IndexOf("private void DrawTextBoxThemeEffect", StringComparison.Ordinal)..
            source.IndexOf("private void DrawShapeThemeEffect", StringComparison.Ordinal)];
        var drawShapeEffect = source[
            source.IndexOf("private void DrawShapeThemeEffect", StringComparison.Ordinal)..
            source.IndexOf("public static DrawingObjectColors ResolveDrawingShapeColors", StringComparison.Ordinal)];

        renderTextBoxes.Should().Contain("var themeEffect = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme);");
        renderTextBoxes.Should().Contain("DrawTextBoxThemeEffect(dc, rect, themeEffect);");
        renderTextBoxes.Should().NotContain("DrawTextBoxThemeEffect(dc, rect, WorkbookTheme);");
        renderDrawingShapes.Should().Contain("var themeEffect = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme);");
        renderDrawingShapes.Should().Contain("DrawShapeThemeEffect(dc, shape.Kind, rect, themeEffect);");
        renderDrawingShapes.Should().NotContain("DrawShapeThemeEffect(dc, shape.Kind, rect, WorkbookTheme);");
        drawTextBoxEffect.Should().Contain("WorkbookThemeEffectStyle effect");
        drawTextBoxEffect.Should().NotContain("WorkbookThemeEffectStyle.FromTheme");
        drawShapeEffect.Should().Contain("WorkbookThemeEffectStyle effect");
        drawShapeEffect.Should().NotContain("WorkbookThemeEffectStyle.FromTheme");
    }

    [Fact]
    public void DrawingObjectRendering_CullsOffscreenObjectsBeforeExpensiveWork()
    {
        var drawingObjects = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var pictures = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));

        var renderCharts = drawingObjects[
            drawingObjects.IndexOf("private void RenderCharts", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderTextBoxes", StringComparison.Ordinal)];
        var renderTextBoxes = drawingObjects[
            drawingObjects.IndexOf("private void RenderTextBoxes", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)];
        var renderDrawingShapes = drawingObjects[
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)];
        var renderPictures = pictures[
            pictures.IndexOf("private void RenderPictures", StringComparison.Ordinal)..
            pictures.IndexOf("private void DrawPictureSelectionAdorner", StringComparison.Ordinal)];
        var renderPlaceholders = drawingObjects[
            drawingObjects.IndexOf("private void RenderObjectPlaceholders", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static string CreateObjectPlaceholderLabel", StringComparison.Ordinal)];

        drawingObjects.Should().Contain("private bool IntersectsDrawingViewport(Rect rect, double rotationDegrees)");
        drawingObjects.Should().Contain("private static Rect CalculateRotatedBounds");

        renderCharts.Should().Contain("if (!IntersectsDrawingViewport(rect, 0, visibleRight, visibleBottom))");
        renderCharts.Should().Contain("GetCachedChartImage(chart, Viewport, WorkbookTheme)");
        renderCharts.IndexOf("if (!IntersectsDrawingViewport(rect, 0, visibleRight, visibleBottom))", StringComparison.Ordinal)
            .Should().BeLessThan(renderCharts.IndexOf("GetCachedChartImage(chart, Viewport, WorkbookTheme)", StringComparison.Ordinal));

        renderTextBoxes.Should().Contain("GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom)");
        renderTextBoxes.Should().Contain("CanAnchoredObjectReachDrawingViewport(textBox.Anchor");
        renderTextBoxes.Should().Contain("NeedsDrawingViewportCull(rect, textBox.RotationDegrees, visibleRight, visibleBottom)");
        renderTextBoxes.Should().Contain("IntersectsDrawingViewport(rect, textBox.RotationDegrees, visibleRight, visibleBottom)");
        renderTextBoxes.IndexOf("NeedsDrawingViewportCull(rect, textBox.RotationDegrees, visibleRight, visibleBottom)", StringComparison.Ordinal)
            .Should().BeLessThan(renderTextBoxes.IndexOf("ResolveTextBoxColors(textBox, WorkbookTheme)", StringComparison.Ordinal));

        renderDrawingShapes.Should().Contain("GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom)");
        renderDrawingShapes.Should().Contain("CanAnchoredObjectReachDrawingViewport(shape.Anchor");
        renderDrawingShapes.Should().Contain("NeedsDrawingViewportCull(rect, shape.RotationDegrees, visibleRight, visibleBottom)");
        renderDrawingShapes.Should().Contain("IntersectsDrawingViewport(rect, shape.RotationDegrees, visibleRight, visibleBottom)");
        renderDrawingShapes.IndexOf("NeedsDrawingViewportCull(rect, shape.RotationDegrees, visibleRight, visibleBottom)", StringComparison.Ordinal)
            .Should().BeLessThan(renderDrawingShapes.IndexOf("ResolveDrawingShapeColors(shape, WorkbookTheme)", StringComparison.Ordinal));

        renderPictures.Should().Contain("GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom)");
        renderPictures.Should().Contain("CanAnchoredObjectReachDrawingViewport(picture.Anchor");
        renderPictures.Should().Contain("NeedsDrawingViewportCull(rect, picture.RotationDegrees, visibleRight, visibleBottom)");
        renderPictures.Should().Contain("IntersectsDrawingViewport(rect, picture.RotationDegrees, visibleRight, visibleBottom)");
        renderPictures.IndexOf("NeedsDrawingViewportCull(rect, picture.RotationDegrees, visibleRight, visibleBottom)", StringComparison.Ordinal)
            .Should().BeLessThan(renderPictures.IndexOf("TryLoadPictureImage(picture, out var image)", StringComparison.Ordinal));

        renderPlaceholders.Should().Contain("CanAnchoredObjectReachDrawingViewport(shape.Anchor");
        renderPlaceholders.Should().Contain("NeedsDrawingViewportCull(rect, picture.RotationDegrees, visibleRight, visibleBottom)");
        renderPlaceholders.Should().Contain("IntersectsDrawingViewport(rect, picture.RotationDegrees, visibleRight, visibleBottom)");
        renderPlaceholders.Should().Contain("IntersectsDrawingViewport(controlRect, 0, visibleRight, visibleBottom)");
    }

    [Fact]
    public void DrawingObjectRendering_UsesAuthoredEffectPresetMetadata()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var authoredEffect = source[
            source.IndexOf("private void DrawShapeAuthoredEffect", StringComparison.Ordinal)..
            source.IndexOf("private void DrawTextBoxThemeEffect", StringComparison.Ordinal)];

        authoredEffect.Should().Contain("shape.GetEffectiveEffectPreset()");
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.Shadow");
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.Glow");
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.SoftEdges");
        authoredEffect.Should().Contain("DrawShapeShadowEffect");
        authoredEffect.Should().Contain("DrawShapeOutlineEffect");
    }

    [Fact]
    public void DrawingObjectRendering_UsesAuthoredGradientDirectionMetadata()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));

        source.Should().Contain("shape.GetEffectiveGradientFillDirection()");
        source.Should().Contain("DrawingShapeGradientDirection.Horizontal");
        source.Should().Contain("DrawingShapeGradientDirection.Vertical");
        source.Should().Contain("DrawingShapeGradientDirection.DiagonalUp");
        source.Should().Contain("DrawingObjectGradientBrushKey(");
        source.Should().Contain("DrawingShapeGradientDirection Direction");
    }

    [Fact]
    public void NativeSlicerRendering_DrawsSelectedTilesWithoutMaterializingArray()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var drawSlicer = source[
            source.IndexOf("private void DrawNativeSlicerControl", StringComparison.Ordinal)..
            source.IndexOf("private void DrawNativeTimelineControl", StringComparison.Ordinal)];

        drawSlicer.Should().Contain("var tileCount = selectedItemCount == 0 ? 1 : Math.Min(4, selectedItemCount);");
        drawSlicer.Should().Contain("slicer.SelectedItems[index]");
        drawSlicer.Should().NotContain(".Take(4)");
        drawSlicer.Should().NotContain(".ToArray()");
        drawSlicer.Should().NotContain("new[]");
    }

    [Fact]
    public void GridView_ExposesObjectDisplayModeForExcelPlaceholderRendering()
    {
        var source =
            File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs")) +
            File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));

        source.Should().Contain("public enum GridObjectDisplayMode");
        propertiesSource.Should().Contain("ObjectDisplayModeProperty");
        source.Should().Contain("RenderObjectPlaceholders(dc)");
        source.Should().Contain("RenderCharts(dc)");
    }

    [Fact]
    public void PictureRenderer_DrawsSelectionAdornerForPictureAtActiveCell()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var adorner = source[
            source.IndexOf("private void DrawPictureSelectionAdorner", StringComparison.Ordinal)..
            source.IndexOf("private static bool HasPictureCrop", StringComparison.Ordinal)];

        source.Should().Contain("DrawPictureSelectionAdorner");
        source.Should().Contain("SelectedRange?.Start != picture.Anchor");
        adorner.Should().Contain("dc.DrawRectangle(null, PictureSelectionPen, rect);");
        adorner.Should().Contain("DrawPictureSelectionHandle(dc, rect.TopLeft, handle);");
        adorner.Should().Contain("DrawPictureSelectionHandle(dc, rect.TopRight, handle);");
        adorner.Should().Contain("DrawPictureSelectionHandle(dc, rect.BottomLeft, handle);");
        adorner.Should().Contain("DrawPictureSelectionHandle(dc, rect.BottomRight, handle);");
        adorner.Should().Contain("private static void DrawPictureSelectionHandle");
        adorner.Should().NotContain("new[]");
        adorner.Should().NotContain("foreach (var point");
    }

    [Fact]
    public void ObjectSelectionHandles_DrawWithoutMaterializingRectArray()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ObjectDrag.cs"));
        var drawHandles = source[
            source.IndexOf("internal void DrawObjectSelectionHandles", StringComparison.Ordinal)..
            source.IndexOf("private ObjectDragKind HitTestObjectHandle", StringComparison.Ordinal)];

        drawHandles.Should().Contain("DrawObjectSelectionHandle(dc,");
        drawHandles.Should().Contain("private static void DrawObjectSelectionHandle");
        drawHandles.Should().NotContain("GetHandleRects");
        drawHandles.Should().NotContain("Rect[]");
        drawHandles.Should().NotContain("new[]");
        drawHandles.Should().NotContain("foreach");
    }

    [Fact]
    public void PictureRenderer_ReusesFrozenStaticResources()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
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
        renderPictures.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderPictures.Should().Contain("if (brush.CanFreeze)");
        renderPictures.Should().Contain("brush.Freeze();");
        renderPictures.Should().Contain("GetDrawingObjectText(");
        renderPictures.Should().Contain("pixelsPerDip,");
        renderPictures.Should().Contain("TextTrimming.CharacterEllipsis");
        renderPictures.Should().Contain("dc.PushClip(GetDrawingObjectClipGeometry(textRect));");
        renderPictures.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip)");
        renderPictures.Should().NotContain("new FormattedText(");
        renderPictures.Should().NotContain("new RectangleGeometry(textRect)");
        renderPictures.Should().NotContain("new Pen(new SolidColorBrush");
        renderPictures.Should().NotContain("new SolidColorBrush");
    }

    [Fact]
    public void WorksheetBackgroundRenderer_ReusesFrozenTiledBrush()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var renderWorksheetBackground = source[
            source.IndexOf("private void RenderWorksheetBackground", StringComparison.Ordinal)..
            source.IndexOf("private ImageBrush GetWorksheetBackgroundBrush", StringComparison.Ordinal)];
        var getBrush = source[
            source.IndexOf("private ImageBrush GetWorksheetBackgroundBrush", StringComparison.Ordinal)..
            source.IndexOf("private static bool TryLoadWorksheetBackgroundImage", StringComparison.Ordinal)];

        source.Should().Contain("private ImageBrush? _worksheetBackgroundBrushCache;");
        source.Should().Contain("private WorksheetBackgroundBrushCacheKey _worksheetBackgroundBrushCacheKey;");
        source.Should().Contain("private readonly record struct WorksheetBackgroundBrushCacheKey(");
        renderWorksheetBackground.Should().Contain("var brush = GetWorksheetBackgroundBrush(WorksheetBackground, image);");
        renderWorksheetBackground.Should().NotContain("new ImageBrush");
        getBrush.Should().Contain("_worksheetBackgroundBrushCache is { } cached && _worksheetBackgroundBrushCacheKey == key");
        getBrush.Should().Contain("new ImageBrush(image)");
        getBrush.Should().Contain("if (brush.CanFreeze)");
        getBrush.Should().Contain("brush.Freeze();");
        getBrush.Should().Contain("_worksheetBackgroundBrushCache = brush;");
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

        source.Should().Contain("ContainsRotatedInclusive(r, pos, t.RotationDegrees)");
        source.Should().Contain("ContainsRotatedInclusive(r, pos, p.RotationDegrees)");
        source.Should().Contain("ContainsRotatedInclusive(r, pos, s.RotationDegrees)");
        source.Should().Contain("var radians = -rotationDegrees * Math.PI / 180.0;");
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
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + 30, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeS);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + 30, start.Top + 10), start)
            .Should().Be(ObjectDragKind.Move);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left - 20, start.Top - 20), start)
            .Should().Be(ObjectDragKind.None);
    }

    [Fact]
    public void GridObjectDragPlanner_ExposesSharedMinimumResizeSizeForMouseCommit()
    {
        var start = new Rect(10, 20, 80, 40);

        GridObjectDragPlanner.MinimumObjectSize.Should().Be(8);
        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.ResizeSE,
                start,
                new Point(start.Right, start.Bottom),
                new Point(start.Left - 100, start.Top - 100))
            .Should()
            .Be(new Rect(
                start.Left,
                start.Top,
                GridObjectDragPlanner.MinimumObjectSize,
                GridObjectDragPlanner.MinimumObjectSize));

        var inputSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Input.cs"));
        var mouseUpStart = inputSource.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        mouseUpStart.Should().BeGreaterThanOrEqualTo(0);
        var mouseUpObjectCommit = inputSource[
            inputSource.IndexOf("if (_objectDragKind != ObjectDragKind.None)", mouseUpStart, StringComparison.Ordinal)..
            inputSource.IndexOf("if (_marginDragEdge.HasValue)", mouseUpStart, StringComparison.Ordinal)];

        mouseUpObjectCommit.Should().Contain("GridObjectDragPlanner.MinimumObjectSize");
        mouseUpObjectCommit.Should().NotContain("Math.Max(8");
    }

    [Fact]
    public void GridObjectDragPlanner_HitTestsAllEightResizeHandlesAndRotation()
    {
        var start = new Rect(10, 20, 80, 40);

        GridObjectDragPlanner.HitTestHandle(new Point(start.Left, start.Top), start)
            .Should().Be(ObjectDragKind.ResizeNW);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + start.Width / 2, start.Top), start)
            .Should().Be(ObjectDragKind.ResizeN);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Top), start)
            .Should().Be(ObjectDragKind.ResizeNE);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left, start.Top + start.Height / 2), start)
            .Should().Be(ObjectDragKind.ResizeW);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Top + start.Height / 2), start)
            .Should().Be(ObjectDragKind.ResizeE);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeSW);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + start.Width / 2, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeS);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeSE);

        // Rotation grip sits above the top-center handle.
        GridObjectDragPlanner.HitTestHandle(
                new Point(start.Left + start.Width / 2, start.Top - GridObjectDragPlanner.RotationGripOffset), start)
            .Should().Be(ObjectDragKind.Rotate);

        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + 16, start.Top + 16), start)
            .Should().Be(ObjectDragKind.Move);
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
    public void GridObjectDragPlanner_HitTestsAnchorCellFromSplitPaneQuadrants()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [],
                [new ColMetric(12, 64, 0), new ColMetric(13, 64, 64)],
                [new RowMetric(30, 18, 0), new RowMetric(31, 18, 18)]));

        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight)
            .Should()
            .Be(new CellAddress(default, 1, 1));
        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight)
            .Should()
            .Be(new CellAddress(default, 1, 12));
        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 58 + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight)
            .Should()
            .Be(new CellAddress(default, 30, 1));
        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 58 + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight)
            .Should()
            .Be(new CellAddress(default, 20, 10));
    }

    [Fact]
    public void GridObjectDragPlanner_StopsAnchorHitScansOnceSortedMetricsPassPointer()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridObjectDragPlanner.cs"));
        var anchorHitTest = source[
            source.IndexOf("public static CellAddress? HitTestAnchorCell", StringComparison.Ordinal)..];

        anchorHitTest.Should().Contain("foreach (var row in rows)");
        anchorHitTest.Should().Contain("foreach (var column in columns)");
        anchorHitTest.Should().Contain("if (position.Y < top)");
        anchorHitTest.Should().Contain("break;");
        anchorHitTest.Should().Contain("if (position.X < left)");
        anchorHitTest.Should().Contain("SumRowHeights(pinnedRows)");
        anchorHitTest.Should().Contain("SumColumnWidths(pinnedColumns)");
        anchorHitTest.Should().Contain("if (metric.Row > row)");
        anchorHitTest.Should().Contain("if (metric.Col > column)");
        anchorHitTest.Should().Contain("return new CellAddress(default, row.Row, column.Col);");
        anchorHitTest.Should().NotContain(".Sum(");
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
        var inputSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Input.cs"));
        var dragSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ObjectDrag.cs"));

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
