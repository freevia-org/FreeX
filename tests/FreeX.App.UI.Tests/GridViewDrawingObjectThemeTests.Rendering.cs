using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewDrawingObjectThemeTests
{
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
        var renderNativeControls = drawingObjects[
            drawingObjects.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static bool TryCreateDrawingAnchorRect", StringComparison.Ordinal)];
        var renderPictures = pictures[
            pictures.IndexOf("private void RenderPictures", StringComparison.Ordinal)..
            pictures.IndexOf("private void DrawPictureSelectionAdorner", StringComparison.Ordinal)];
        var renderPlaceholders = drawingObjects[
            drawingObjects.IndexOf("private void RenderObjectPlaceholders", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static string CreateObjectPlaceholderLabel", StringComparison.Ordinal)];

        drawingObjects.Should().Contain("private bool IntersectsDrawingViewport(Rect rect, double rotationDegrees)");
        drawingObjects.Should().Contain("private static Rect CalculateRotatedBounds");

        renderCharts.Should().Contain("if (!IntersectsDrawingViewport(rect, 0, visibleRight, visibleBottom))");
        renderCharts.Should().Contain("GetCachedChartImage(chart, Viewport, WorkbookTheme, renderScale)");
        renderCharts.IndexOf("if (!IntersectsDrawingViewport(rect, 0, visibleRight, visibleBottom))", StringComparison.Ordinal)
            .Should().BeLessThan(renderCharts.IndexOf("GetCachedChartImage(chart, Viewport, WorkbookTheme, renderScale)", StringComparison.Ordinal));

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

        renderNativeControls.Should().Contain("GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom)");
        renderNativeControls.Should().Contain("CanAnchoredObjectReachDrawingViewport(anchor, lastRenderableRow, lastRenderableColumn)");
        renderNativeControls.IndexOf("CanAnchoredObjectReachDrawingViewport(anchor, lastRenderableRow, lastRenderableColumn)", StringComparison.Ordinal)
            .Should().BeLessThan(renderNativeControls.IndexOf("TryCreateDrawingAnchorRect(metricLookups, anchor", StringComparison.Ordinal));

        renderPictures.Should().Contain("GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom)");
        renderPictures.Should().Contain("CanAnchoredObjectReachDrawingViewport(picture.Anchor");
        renderPictures.Should().Contain("NeedsDrawingViewportCull(rect, picture.RotationDegrees, visibleRight, visibleBottom)");
        renderPictures.Should().Contain("IntersectsDrawingViewport(rect, picture.RotationDegrees, visibleRight, visibleBottom)");
        renderPictures.IndexOf("NeedsDrawingViewportCull(rect, picture.RotationDegrees, visibleRight, visibleBottom)", StringComparison.Ordinal)
            .Should().BeLessThan(renderPictures.IndexOf("TryLoadPictureImage(picture, out var image)", StringComparison.Ordinal));

        renderPlaceholders.Should().Contain("CanAnchoredObjectReachDrawingViewport(shape.Anchor");
        renderPlaceholders.Should().Contain("CanAnchoredObjectReachDrawingViewport(anchor, lastRenderableRow, lastRenderableColumn)");
        renderPlaceholders.IndexOf("CanAnchoredObjectReachDrawingViewport(anchor, lastRenderableRow, lastRenderableColumn)", StringComparison.Ordinal)
            .Should().BeLessThan(renderPlaceholders.IndexOf("TryCreateDrawingAnchorRect(metricLookups, anchor", StringComparison.Ordinal));
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
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.InnerShadow");
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.Reflection");
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.Glow");
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.SoftEdges");
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.Bevel");
        authoredEffect.Should().Contain("DrawingShapeEffectPreset.ThreeDRotation");
        authoredEffect.Should().Contain("DrawShapeShadowEffect");
        authoredEffect.Should().Contain("DrawShapeAuthoredInnerShadow");
        authoredEffect.Should().Contain("DrawShapeReflectionEffect");
        authoredEffect.Should().Contain("DrawShapeAuthoredBevelEffect");
        authoredEffect.Should().Contain("DrawShapeThreeDRotationEffect");
        authoredEffect.Should().Contain("DrawShapeOutlineEffect");
        authoredEffect.Should().Contain("GetInnerShadowRect(rect, thickness, offsetX: 1.5, offsetY: 1.5)");
        authoredEffect.Should().Contain("GetReflectionRect(rect)");
    }

    [Fact]
    public void DrawingObjectRendering_DrawsAuthoredBevelAsRaisedEdge()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();
            var visual = new DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                var drawBevel = typeof(GridView).GetMethod(
                    "DrawShapeAuthoredBevelEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                drawBevel.Should().NotBeNull();
                drawBevel!.Invoke(
                    grid,
                    [
                        drawingContext,
                        DrawingShapeKind.Rectangle,
                        new Rect(20, 12, 48, 24),
                        new DrawingShapeModel { EffectPreset = DrawingShapeEffectPreset.Bevel }
                    ]);
            }

            var bitmap = new RenderTargetBitmap(
                100,
                70,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var pixels = new byte[100 * 70 * 4];
            bitmap.CopyPixels(pixels, stride: 100 * 4, offset: 0);

            var highlightPixels = 0;
            for (var y = 12; y <= 16; y++)
            {
                for (var x = 20; x <= 68; x++)
                {
                    var offset = (y * 100 + x) * 4;
                    if (pixels[offset + 3] > 0 && pixels[offset] > 90 && pixels[offset + 1] > 90 && pixels[offset + 2] > 90)
                        highlightPixels++;
                }
            }

            var shadowPixels = 0;
            for (var y = 33; y <= 37; y++)
            {
                for (var x = 20; x <= 68; x++)
                {
                    var offset = (y * 100 + x) * 4;
                    if (pixels[offset + 3] > 0 && pixels[offset] < 12 && pixels[offset + 1] < 12 && pixels[offset + 2] < 12)
                        shadowPixels++;
                }
            }

            highlightPixels.Should().BeGreaterThan(20, "the authored bevel should draw a light top edge");
            shadowPixels.Should().BeGreaterThan(20, "the authored bevel should draw a dark bottom edge");
        });
    }

    [Fact]
    public void DrawingObjectRendering_DrawsAuthoredThreeDRotationAsPerspectiveCue()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();
            var visual = new DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                var drawThreeDRotation = typeof(GridView).GetMethod(
                    "DrawShapeThreeDRotationEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                drawThreeDRotation.Should().NotBeNull();
                drawThreeDRotation!.Invoke(
                    grid,
                    [
                        drawingContext,
                        DrawingShapeKind.Rectangle,
                        new Rect(24, 24, 44, 24),
                        new DrawingObjectColors(new CellColor(31, 119, 180), new CellColor(20, 60, 100))
                    ]);
            }

            var bitmap = new RenderTargetBitmap(
                100,
                70,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var pixels = new byte[100 * 70 * 4];
            bitmap.CopyPixels(pixels, stride: 100 * 4, offset: 0);

            var perspectivePixels = 0;
            for (var y = 17; y <= 45; y++)
            {
                for (var x = 68; x <= 80; x++)
                {
                    var offset = (y * 100 + x) * 4;
                    if (pixels[offset + 3] > 0)
                        perspectivePixels++;
                }
            }

            perspectivePixels.Should().BeGreaterThan(20, "the authored 3-D rotation cue should draw offset perspective edges");
        });
    }

    [Fact]
    public void DrawingObjectRendering_DrawsAuthoredReflectionBelowShape()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();
            var visual = new DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                var drawReflection = typeof(GridView).GetMethod(
                    "DrawShapeReflectionEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                drawReflection.Should().NotBeNull();
                drawReflection!.Invoke(
                    grid,
                    [
                        drawingContext,
                        DrawingShapeKind.Rectangle,
                        new Rect(20, 12, 48, 24),
                        new DrawingObjectColors(new CellColor(31, 119, 180), new CellColor(20, 60, 100))
                    ]);
            }

            var bitmap = new RenderTargetBitmap(
                100,
                70,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var pixels = new byte[100 * 70 * 4];
            bitmap.CopyPixels(pixels, stride: 100 * 4, offset: 0);

            var reflectedPixels = 0;
            for (var y = 40; y <= 52; y++)
            {
                for (var x = 24; x <= 64; x++)
                {
                    var offset = (y * 100 + x) * 4;
                    var alpha = pixels[offset + 3];
                    if (alpha > 0)
                        reflectedPixels++;
                }
            }

            reflectedPixels.Should().BeGreaterThan(20, "the authored reflection should render below the source shape");
        });
    }

    [Fact]
    public void DrawingObjectRendering_UsesThemeGlowForTextBoxesAndShapesOnly()
    {
        var drawingObjects = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var pictures = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var textBoxThemeEffect = drawingObjects[
            drawingObjects.IndexOf("private void DrawTextBoxThemeEffect", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void DrawShapeThemeEffect", StringComparison.Ordinal)];
        var shapeThemeEffect = drawingObjects[
            drawingObjects.IndexOf("private void DrawShapeThemeEffect", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static DrawingObjectColors ResolveDrawingShapeColors", StringComparison.Ordinal)];

        textBoxThemeEffect.Should().Contain("effect.HasGlow");
        textBoxThemeEffect.Should().Contain("effect.GlowRadius");
        textBoxThemeEffect.Should().Contain("effect.GlowColor ?? new CellColor(91, 155, 213)");
        shapeThemeEffect.Should().Contain("effect.HasGlow");
        shapeThemeEffect.Should().Contain("DrawShapeOutlineEffect(");
        shapeThemeEffect.Should().Contain("effect.GlowRadius");
        pictures.Should().NotContain("WorkbookThemeEffectStyle");
        pictures.Should().NotContain("HasGlow");
    }

    [Fact]
    public void DrawingObjectRendering_UsesThemeSoftEdgesForTextBoxesAndShapesOnly()
    {
        var drawingObjects = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var pictures = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var textBoxThemeEffect = drawingObjects[
            drawingObjects.IndexOf("private void DrawTextBoxThemeEffect", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void DrawShapeThemeEffect", StringComparison.Ordinal)];
        var shapeThemeEffect = drawingObjects[
            drawingObjects.IndexOf("private void DrawShapeThemeEffect", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static DrawingObjectColors ResolveDrawingShapeColors", StringComparison.Ordinal)];

        textBoxThemeEffect.Should().Contain("effect.HasSoftEdge");
        textBoxThemeEffect.Should().Contain("effect.SoftEdgeRadius");
        textBoxThemeEffect.Should().Contain("GetSoftEdgeThickness(effect.SoftEdgeRadius)");
        shapeThemeEffect.Should().Contain("effect.HasSoftEdge");
        shapeThemeEffect.Should().Contain("DrawShapeOutlineEffect(");
        shapeThemeEffect.Should().Contain("GetSoftEdgeInflate(effect.SoftEdgeRadius)");
        pictures.Should().NotContain("WorkbookThemeEffectStyle");
        pictures.Should().NotContain("HasSoftEdge");
        pictures.Should().NotContain("SoftEdgeRadius");
    }

    [Fact]
    public void DrawingObjectRendering_UsesThemeInnerShadowsForTextBoxesAndShapesOnly()
    {
        var drawingObjects = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var pictures = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var renderTextBox = drawingObjects[
            drawingObjects.IndexOf("private void RenderTextBox", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)];
        var renderDrawingShape = drawingObjects[
            drawingObjects.IndexOf("private void RenderDrawingShape", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private bool HasExplicitDrawingObjectZOrder", StringComparison.Ordinal)];
        var textBoxThemeInnerShadow = drawingObjects[
            drawingObjects.IndexOf("private void DrawTextBoxThemeInnerShadow", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void DrawShapeThemeEffect", StringComparison.Ordinal)];
        var shapeThemeInnerShadow = drawingObjects[
            drawingObjects.IndexOf("private void DrawShapeThemeInnerShadow", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private static double GetSoftEdgeThickness", StringComparison.Ordinal)];

        renderTextBox.Should().Contain("DrawTextBoxThemeInnerShadow(dc, rect, themeEffect);");
        renderDrawingShape.Should().Contain("DrawShapeThemeInnerShadow(dc, shape.Kind, rect, themeEffect);");
        textBoxThemeInnerShadow.Should().Contain("effect.HasInnerShadow");
        textBoxThemeInnerShadow.Should().Contain("GetInnerShadowThickness(effect.InnerShadowBlurRadius)");
        textBoxThemeInnerShadow.Should().Contain("GetInnerShadowRect(rect, thickness, effect.InnerShadowOffsetX, effect.InnerShadowOffsetY)");
        shapeThemeInnerShadow.Should().Contain("effect.HasInnerShadow");
        shapeThemeInnerShadow.Should().Contain("dc.DrawRectangle(null, pen, shadowRect);");
        shapeThemeInnerShadow.Should().Contain("dc.DrawEllipse(null, pen");
        pictures.Should().NotContain("WorkbookThemeEffectStyle");
        pictures.Should().NotContain("HasInnerShadow");
        pictures.Should().NotContain("InnerShadow");
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
            File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs")) +
            File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjectLayerCache.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));

        source.Should().Contain("public enum GridObjectDisplayMode");
        propertiesSource.Should().Contain("ObjectDisplayModeProperty");
        source.Should().Contain("RenderObjectPlaceholders(dc)");
        source.Should().Contain("RenderCharts(dc)");
    }
}
