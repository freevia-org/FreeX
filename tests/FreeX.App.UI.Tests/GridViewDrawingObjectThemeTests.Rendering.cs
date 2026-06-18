using System;
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
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
        renderDrawingShapes.Should().Contain("var shapeThemeEffect = ResolveDrawingShapeThemeEffect(shape, themeEffect);");
        renderDrawingShapes.Should().Contain("DrawShapeThemeEffect(dc, shape.Kind, rect, shapeThemeEffect, colors);");
        renderDrawingShapes.Should().NotContain("DrawShapeThemeEffect(dc, shape.Kind, rect, WorkbookTheme);");
        drawTextBoxEffect.Should().Contain("WorkbookThemeEffectStyle effect");
        drawTextBoxEffect.Should().NotContain("WorkbookThemeEffectStyle.FromTheme");
        drawShapeEffect.Should().Contain("WorkbookThemeEffectStyle effect");
        drawShapeEffect.Should().NotContain("WorkbookThemeEffectStyle.FromTheme");
    }

    [Fact]
    public void DrawingObjectRendering_CullsOffscreenObjectsBeforeExpensiveWork()
    {
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var pictures = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");

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
            pictures.IndexOf("private void DrawPictureCellStyle", StringComparison.Ordinal)];
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
        renderTextBoxes.Should().Contain("var flipHorizontal = textBox.FlipHorizontal;");
        renderTextBoxes.Should().Contain("var flipVertical = textBox.FlipVertical;");
        renderTextBoxes.Should().Contain("TryResolveLiveObjectTransform(");
        renderTextBoxes.Should().Contain("PushDrawingObjectTransform(dc, rotationDegrees, flipHorizontal, flipVertical, rect)");
        renderTextBoxes.Should().Contain("NeedsDrawingViewportCull(rect, rotationDegrees, visibleRight, visibleBottom)");
        renderTextBoxes.Should().Contain("IntersectsDrawingViewport(rect, rotationDegrees, visibleRight, visibleBottom)");
        renderTextBoxes.IndexOf("NeedsDrawingViewportCull(rect, rotationDegrees, visibleRight, visibleBottom)", StringComparison.Ordinal)
            .Should().BeLessThan(renderTextBoxes.IndexOf("ResolveTextBoxColors(textBox, WorkbookTheme)", StringComparison.Ordinal));

        renderDrawingShapes.Should().Contain("GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom)");
        renderDrawingShapes.Should().Contain("CanAnchoredObjectReachDrawingViewport(shape.Anchor");
        renderDrawingShapes.Should().Contain("var flipHorizontal = shape.FlipHorizontal;");
        renderDrawingShapes.Should().Contain("var flipVertical = shape.FlipVertical;");
        renderDrawingShapes.Should().Contain("TryResolveLiveObjectTransform(");
        renderDrawingShapes.Should().Contain("PushDrawingObjectTransform(dc, rotationDegrees, flipHorizontal, flipVertical, rect)");
        renderDrawingShapes.Should().Contain("NeedsDrawingViewportCull(rect, rotationDegrees, visibleRight, visibleBottom)");
        renderDrawingShapes.Should().Contain("IntersectsDrawingViewport(rect, rotationDegrees, visibleRight, visibleBottom)");
        renderDrawingShapes.IndexOf("NeedsDrawingViewportCull(rect, rotationDegrees, visibleRight, visibleBottom)", StringComparison.Ordinal)
            .Should().BeLessThan(renderDrawingShapes.IndexOf("ResolveDrawingShapeColors(shape, WorkbookTheme)", StringComparison.Ordinal));

        renderNativeControls.Should().Contain("GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom)");
        renderNativeControls.Should().Contain("CanAnchoredObjectReachDrawingViewport(anchor, lastRenderableRow, lastRenderableColumn)");
        renderNativeControls.IndexOf("CanAnchoredObjectReachDrawingViewport(anchor, lastRenderableRow, lastRenderableColumn)", StringComparison.Ordinal)
            .Should().BeLessThan(renderNativeControls.IndexOf("TryCreateDrawingAnchorRect(metricLookups, anchor", StringComparison.Ordinal));

        renderPictures.Should().Contain("GetRenderableDrawingAnchorBounds(visibleRight, visibleBottom)");
        renderPictures.Should().Contain("CanAnchoredObjectReachDrawingViewport(picture.Anchor");
        renderPictures.Should().Contain("var flipHorizontal = picture.FlipHorizontal;");
        renderPictures.Should().Contain("var flipVertical = picture.FlipVertical;");
        renderPictures.Should().Contain("TryResolveLiveObjectTransform(");
        renderPictures.Should().Contain("PushDrawingObjectTransform(dc, rotationDegrees, flipHorizontal, flipVertical, rect)");
        renderPictures.Should().Contain("NeedsDrawingViewportCull(rect, rotationDegrees, visibleRight, visibleBottom)");
        renderPictures.Should().Contain("IntersectsDrawingViewport(rect, rotationDegrees, visibleRight, visibleBottom)");
        renderPictures.IndexOf("NeedsDrawingViewportCull(rect, rotationDegrees, visibleRight, visibleBottom)", StringComparison.Ordinal)
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
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
    public void PictureRenderer_DrawsCellRangeSnapshotFormatting()
    {
        WpfTestThread.Run(() =>
        {
            var style = new CellStyle
            {
                FillColor = new CellColor(220, 240, 255),
                FontColor = new CellColor(180, 0, 0),
                HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Right,
                BorderBottom = new CellBorder(BorderStyle.Thick, new CellColor(30, 60, 90))
            };
            var picture = new PictureModel
            {
                Anchor = new CellAddress(SheetId.New(), 1, 1),
                SourceRowCount = 1,
                SourceColumnCount = 1,
                Width = 80,
                Height = 30,
                Cells =
                {
                    new PictureCellSnapshot(0, 0, "123", style, IsNumericOrDate: true)
                }
            };
            var grid = new GridView
            {
                Width = 100,
                Height = 45,
                ShowHeaders = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 30, 0)],
                    [new ColMetric(1, 80, 0)]),
                Pictures = [picture]
            };

            grid.Measure(new Size(100, 45));
            grid.Arrange(new Rect(0, 0, 100, 45));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                100,
                45,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(grid);

            var pixels = new byte[100 * 45 * 4];
            bitmap.CopyPixels(pixels, stride: 100 * 4, offset: 0);

            var fillOffset = (10 * 100 + 10) * 4;
            pixels[fillOffset + 0].Should().BeGreaterThan(240);
            pixels[fillOffset + 1].Should().BeGreaterThan(220);
            pixels[fillOffset + 2].Should().BeGreaterThan(200);

            var borderPixels = 0;
            for (var x = 4; x < 76; x++)
            {
                var offset = (29 * 100 + x) * 4;
                if (pixels[offset + 0] is >= 70 and <= 110 &&
                    pixels[offset + 1] is >= 45 and <= 75 &&
                    pixels[offset + 2] is >= 15 and <= 45)
                {
                    borderPixels++;
                }
            }

            borderPixels.Should().BeGreaterThan(30, "the snapshot bottom border should use the captured cell style");
        });
    }

    [Fact]
    public void ChartRendererLayer_DrawsInsertedSelectedChartContent()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2)),
                Left = 120,
                Top = 24,
                Width = 400,
                Height = 300
            };
            var grid = new GridView
            {
                Width = 640,
                Height = 380,
                ShowHeaders = false,
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 1, new TextValue("Quarter"), "Quarter", null, StyleId.Default, null),
                        new DisplayCell(1, 2, new TextValue("Revenue"), "Revenue", null, StyleId.Default, null),
                        new DisplayCell(2, 1, new TextValue("Q1"), "Q1", null, StyleId.Default, null),
                        new DisplayCell(2, 2, new NumberValue(10), "10", null, StyleId.Default, null),
                        new DisplayCell(3, 1, new TextValue("Q2"), "Q2", null, StyleId.Default, null),
                        new DisplayCell(3, 2, new NumberValue(18), "18", null, StyleId.Default, null),
                        new DisplayCell(4, 1, new TextValue("Q3"), "Q3", null, StyleId.Default, null),
                        new DisplayCell(4, 2, new NumberValue(14), "14", null, StyleId.Default, null),
                        new DisplayCell(5, 1, new TextValue("Q4"), "Q4", null, StyleId.Default, null),
                        new DisplayCell(5, 2, new NumberValue(26), "26", null, StyleId.Default, null)
                    ],
                    [new RowMetric(1, 24, 0), new RowMetric(2, 24, 24), new RowMetric(3, 24, 48), new RowMetric(4, 24, 72), new RowMetric(5, 24, 96)],
                    [new ColMetric(1, 80, 0), new ColMetric(2, 80, 80), new ColMetric(3, 80, 160)]),
                Charts = [chart],
                SelectedObjectId = chart.Id,
                SelectedObjectKind = ObjectKind.Chart
            };

            grid.Measure(new Size(640, 380));
            grid.Arrange(new Rect(0, 0, 640, 380));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                640,
                380,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(grid);

            CountNonWhitePixels(bitmap, new Int32Rect(150, 54, 330, 230))
                .Should().BeGreaterThan(500, "inserted charts should render axes or data marks inside the selected chart frame");
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
    public void DrawingObjectRendering_SkipsWorkbookThemeEffectsForInsertedShapes()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var shape = new DrawingShapeModel
            {
                Anchor = new CellAddress(sheetId, 1, 1),
                Kind = DrawingShapeKind.Rectangle,
                Width = 60,
                Height = 30,
                FillColor = DrawingShapeModel.DefaultFillColor,
                OutlineColor = DrawingShapeModel.DefaultOutlineColor
            };
            var grid = new GridView
            {
                Width = 90,
                Height = 60,
                ShowHeaders = false,
                ShowGridLines = false,
                WorkbookTheme = WorkbookTheme.Office.WithNativeFormatSchemeXml(ThemeWithTenPixelDropShadow),
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 60, 0)],
                    [new ColMetric(1, 90, 0)]),
                DrawingShapes = [shape]
            };

            grid.Measure(new Size(90, 60));
            grid.Arrange(new Rect(0, 0, 90, 60));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                90,
                60,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(grid);

            CountNonWhitePixels(bitmap, new Int32Rect(8, 8, 44, 14))
                .Should()
                .BeGreaterThan(50, "the inserted shape fill and outline should still render");
            CountNonWhitePixels(bitmap, new Int32Rect(8, 34, 44, 10))
                .Should()
                .Be(0, "plain inserted shapes should not inherit workbook theme shadow artifacts");
        });
    }

    [Fact]
    public void DrawingObjectRendering_UsesThemeEffectsOnlyForThemeStyledShapes()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var shape = new DrawingShapeModel
            {
                Anchor = new CellAddress(sheetId, 1, 1),
                Kind = DrawingShapeKind.Rectangle,
                Width = 60,
                Height = 30,
                FillColor = DrawingShapeModel.DefaultFillColor,
                OutlineColor = DrawingShapeModel.DefaultOutlineColor,
                UsesThemeEffects = true
            };
            var grid = new GridView
            {
                Width = 90,
                Height = 60,
                ShowHeaders = false,
                ShowGridLines = false,
                WorkbookTheme = WorkbookTheme.Office.WithNativeFormatSchemeXml(ThemeWithTenPixelDropShadow),
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 60, 0)],
                    [new ColMetric(1, 90, 0)]),
                DrawingShapes = [shape]
            };

            grid.Measure(new Size(90, 60));
            grid.Arrange(new Rect(0, 0, 90, 60));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                90,
                60,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(grid);

            CountNonWhitePixels(bitmap, new Int32Rect(8, 34, 44, 10))
                .Should()
                .BeGreaterThan(20, "style-backed imported shapes can still opt into workbook theme effects");
        });
    }

    [Fact]
    public void DrawingObjectRendering_UsesThemeGlowForTextBoxesAndShapesOnly()
    {
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var pictures = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");
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
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var pictures = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");
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
    public void DrawingObjectRendering_UsesThemeBevelAndThreeDRotationForShapesOnly()
    {
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var pictures = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");
        var renderCharts = drawingObjects[
            drawingObjects.IndexOf("private void RenderCharts", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderTextBoxes", StringComparison.Ordinal)];
        var renderTextBox = drawingObjects[
            drawingObjects.IndexOf("private void RenderTextBox", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)];
        var renderDrawingShape = drawingObjects[
            drawingObjects.IndexOf("private void RenderDrawingShape", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private bool HasExplicitDrawingObjectZOrder", StringComparison.Ordinal)];
        var shapeThemeBevel = drawingObjects[
            drawingObjects.IndexOf("private void DrawShapeThemeBevelEffect", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void DrawShapeAuthoredInnerShadow", StringComparison.Ordinal)];
        var shapeThemeEffect = drawingObjects[
            drawingObjects.IndexOf("private void DrawShapeThemeEffect", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void DrawShapeThemeInnerShadow", StringComparison.Ordinal)];

        renderDrawingShape.Should().Contain("DrawShapeThemeEffect(dc, shape.Kind, rect, shapeThemeEffect, colors);");
        renderDrawingShape.Should().Contain("DrawShapeThemeBevelEffect(dc, shape.Kind, rect, shapeThemeEffect);");
        renderDrawingShape.IndexOf("DrawShapeThemeEffect(dc, shape.Kind, rect, shapeThemeEffect, colors);", StringComparison.Ordinal)
            .Should().BeLessThan(renderDrawingShape.IndexOf("DrawShapeGeometry(dc, shape.Kind, rect", StringComparison.Ordinal));
        renderDrawingShape.IndexOf("DrawShapeGeometry(dc, shape.Kind, rect", StringComparison.Ordinal)
            .Should().BeLessThan(renderDrawingShape.IndexOf("DrawShapeThemeBevelEffect(dc, shape.Kind, rect, shapeThemeEffect);", StringComparison.Ordinal));
        shapeThemeEffect.Should().Contain("effect.HasThreeDRotation");
        shapeThemeEffect.Should().Contain("DrawShapeThreeDRotationEffect(dc, kind, rect, colors);");
        shapeThemeBevel.Should().Contain("effect.HasBevel");
        shapeThemeBevel.Should().Contain("DrawShapeBevelEffect(dc, kind, rect);");
        renderTextBox.Should().NotContain("HasBevel");
        renderTextBox.Should().NotContain("HasThreeDRotation");
        renderCharts.Should().NotContain("WorkbookThemeEffectStyle");
        pictures.Should().NotContain("WorkbookThemeEffectStyle");
        pictures.Should().NotContain("HasBevel");
        pictures.Should().NotContain("HasThreeDRotation");
    }

    [Fact]
    public void DrawingObjectRendering_UsesThemeInnerShadowsForTextBoxesAndShapesOnly()
    {
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var pictures = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");
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
        renderDrawingShape.Should().Contain("DrawShapeThemeInnerShadow(dc, shape.Kind, rect, shapeThemeEffect);");
        textBoxThemeInnerShadow.Should().Contain("effect.HasInnerShadow");
        textBoxThemeInnerShadow.Should().Contain("GetInnerShadowThickness(effect.InnerShadowBlurRadius)");
        textBoxThemeInnerShadow.Should().Contain("GetInnerShadowRect(rect, thickness, effect.InnerShadowOffsetX, effect.InnerShadowOffsetY)");
        shapeThemeInnerShadow.Should().Contain("effect.HasInnerShadow");
        shapeThemeInnerShadow.Should().Contain("DrawShapeGeometry(dc, kind, shadowRect, null, pen);");
        pictures.Should().NotContain("WorkbookThemeEffectStyle");
        pictures.Should().NotContain("HasInnerShadow");
        pictures.Should().NotContain("InnerShadow");
    }

    [Fact]
    public void DrawingObjectRendering_UsesAuthoredGradientDirectionMetadata()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");

        source.Should().Contain("shape.GetEffectiveGradientFillDirection()");
        source.Should().Contain("DrawingShapeGradientDirection.Horizontal");
        source.Should().Contain("DrawingShapeGradientDirection.Vertical");
        source.Should().Contain("DrawingShapeGradientDirection.DiagonalUp");
        source.Should().Contain("DrawingObjectGradientBrushKey(");
        source.Should().Contain("DrawingShapeGradientDirection Direction");
        source.Should().Contain("Color.FromRgb(startColor.R, startColor.G, startColor.B)");
        source.Should().Contain("Color.FromRgb(endColor.R, endColor.G, endColor.B)");
        source.Should().NotContain("Color.FromArgb(72, startColor.R");
        source.Should().NotContain("Color.FromArgb(72, endColor.R");
    }

    [Fact]
    public void DrawingObjectRendering_UsesOpaqueChosenShapeFill()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();
            var createFill = typeof(GridView).GetMethod(
                "CreateDrawingShapeFill",
                BindingFlags.Instance | BindingFlags.NonPublic);
            createFill.Should().NotBeNull();

            var brush = (Brush)createFill!.Invoke(
                grid,
                [
                    new DrawingShapeModel { Kind = DrawingShapeKind.Rectangle },
                    new CellColor(10, 120, 230)
                ])!;
            var visual = new DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                drawingContext.DrawRectangle(brush, null, new Rect(0, 0, 20, 20));
            }

            var bitmap = new RenderTargetBitmap(
                20,
                20,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var pixels = new byte[20 * 20 * 4];
            bitmap.CopyPixels(pixels, stride: 20 * 4, offset: 0);
            var center = (10 * 20 + 10) * 4;

            pixels[center + 3].Should().Be(255);
            pixels[center + 2].Should().Be(10);
            pixels[center + 1].Should().Be(120);
            pixels[center].Should().Be(230);
        });
    }

    [Fact]
    public void DrawingObjectRendering_SkipsFillBrushWhenObjectHasNoFill()
    {
        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));

        source.Should().Contain("var fillBrush = textBox.HasFill ? GetDrawingObjectBrush(242, colors.Fill) : null;");
        source.Should().Contain("var fill = shape.HasFill ? CreateDrawingShapeFill(shape, colors.Fill) : null;");
        source.Should().Contain("dc.DrawRectangle(fillBrush, borderPen, rect);");
        source.Should().Contain("DrawShapeGeometry(dc, shape.Kind, rect, DrawingShapeKindSupport.IsLineLike(shape.Kind) ? null : fill, pen);");
    }

    [Fact]
    public void NativeSlicerRendering_DrawsSelectedTilesWithoutMaterializingArray()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var drawSlicer = source[
            source.IndexOf("private void DrawNativeSlicerControl", StringComparison.Ordinal)..
            source.IndexOf("private void DrawNativeTimelineControl", StringComparison.Ordinal)];

        // The renderer draws the slicer's resolved available items (table-column distinct values / pivot
        // cache shared items) with per-tile selected/unselected styling, honoring the slicer's columnCount.
        drawSlicer.Should().Contain("ResolveSlicerTileItems(slicer, out var fallbackAllTile)");
        drawSlicer.Should().Contain("var columnCount = Math.Max(1, slicer.ColumnCount);");
        // Tile fills/text now come from the resolved slicer-style colors (SlicerStyleColors.Resolve), not
        // hardcoded brushes, so slicers theme like Excel's built-in styles.
        drawSlicer.Should().Contain("SlicerStyleColors.Resolve(slicer.StyleName, WorkbookTheme)");
        drawSlicer.Should().Contain("isSelected ? selectedTileBrush : tileBrush");
        drawSlicer.Should().NotContain(".Take(4)");
        drawSlicer.Should().NotContain(".ToArray()");
        drawSlicer.Should().NotContain("new[]");
    }

    [Fact]
    public void GridView_ExposesObjectDisplayModeForExcelPlaceholderRendering()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSourcesWithSeparator(
            "",
            "GridView.cs",
            "GridView.RenderDispatch.cs",
            "GridView.DrawingObjectLayerCache.cs");
        var propertiesSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Properties.cs");

        source.Should().Contain("public enum GridObjectDisplayMode");
        propertiesSource.Should().Contain("ObjectDisplayModeProperty");
        source.Should().Contain("RenderObjectPlaceholders(dc)");
        source.Should().Contain("RenderCharts(dc)");
    }

    private static int CountNonWhitePixels(BitmapSource bitmap, Int32Rect rect)
    {
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        bitmap.CopyPixels(rect, pixels, stride, 0);

        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 10 && (red < 245 || green < 245 || blue < 245))
                count++;
        }

        return count;
    }

    private const string ThemeWithTenPixelDropShadow = """
        <a:fmtScheme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Artifact Test Effects">
          <a:effectStyleLst>
            <a:effectStyle>
              <a:effectLst>
                <a:outerShdw blurRad="40000" dist="95250" dir="5400000" rotWithShape="0">
                  <a:srgbClr val="000000"><a:alpha val="50000"/></a:srgbClr>
                </a:outerShdw>
              </a:effectLst>
            </a:effectStyle>
          </a:effectStyleLst>
        </a:fmtScheme>
        """;
}
