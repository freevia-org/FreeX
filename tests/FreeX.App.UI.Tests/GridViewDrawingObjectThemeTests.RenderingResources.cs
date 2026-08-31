using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewDrawingObjectThemeTests
{
    [Fact]
    public void PictureRenderer_LeavesSelectionHandlesToObjectOverlay()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");

        source.Should().NotContain("DrawPictureSelectionAdorner");
        source.Should().NotContain("PictureSelectionBrush");
        source.Should().NotContain("PictureSelectionPen");
        source.Should().NotContain("DrawPictureSelectionHandle");
        source.Should().NotContain("SelectedRange?.Start != picture.Anchor");
    }

    [Fact]
    public void PictureRenderer_ReusesFrozenStaticResources()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");
        var renderStart = source.IndexOf("private void RenderPictures", StringComparison.Ordinal);
        var renderEnd = source.IndexOf("private void DrawPictureCellStyle", StringComparison.Ordinal);
        renderStart.Should().BeGreaterThanOrEqualTo(0);
        renderEnd.Should().BeGreaterThan(renderStart);
        var renderPictures = source[
            renderStart..
            renderEnd];
        var getCroppedBrush = source[
            source.IndexOf("private ImageBrush GetCroppedPictureBrush", StringComparison.Ordinal)..
            source.IndexOf("private void RenderWorksheetBackground", StringComparison.Ordinal)];

        GetStaticResource<Pen>("PictureBorderPen").IsFrozen.Should().BeTrue();
        GetStaticResource<Pen>("PictureGridPen").IsFrozen.Should().BeTrue();
        source.Should().Contain("private const int CroppedPictureBrushCacheLimit = 256;");
        source.Should().Contain("private readonly Dictionary<CroppedPictureBrushCacheKey, ImageBrush> _croppedPictureBrushCache = new();");
        source.Should().Contain("private readonly record struct CroppedPictureBrushCacheKey(");
        source.Should().Contain("private static readonly Pen PictureBorderPen = CreateFrozenPen");
        source.Should().Contain("private static readonly Pen PictureGridPen = CreateFrozenPen");
        renderPictures.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderPictures.Should().Contain("var brush = GetCroppedPictureBrush(crop, image);");
        renderPictures.Should().Contain("pixelsPerDip,");
        renderPictures.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip)");
        renderPictures.Should().NotContain("new ImageBrush");
        renderPictures.Should().NotContain("new RectangleGeometry(textRect)");
        renderPictures.Should().NotContain("new Pen(new SolidColorBrush");
        renderPictures.Should().NotContain("new SolidColorBrush");
        getCroppedBrush.Should().Contain("_croppedPictureBrushCache.TryGetValue(key, out var cached)");
        getCroppedBrush.Should().Contain("_croppedPictureBrushCache.Count >= CroppedPictureBrushCacheLimit");
        getCroppedBrush.Should().Contain("_croppedPictureBrushCache.Clear();");
        getCroppedBrush.Should().Contain("new ImageBrush(image)");
        getCroppedBrush.Should().Contain("if (brush.CanFreeze)");
        getCroppedBrush.Should().Contain("brush.Freeze();");
        getCroppedBrush.Should().Contain("_croppedPictureBrushCache.Add(key, brush);");
    }

    [Fact]
    public void WorksheetBackgroundRenderer_ReusesFrozenTiledBrush()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");
        var renderWorksheetBackground = source[
            source.IndexOf("private void RenderWorksheetBackground", StringComparison.Ordinal)..
            source.IndexOf("internal ImageBrush GetWorksheetBackgroundBrush", StringComparison.Ordinal)];
        var getBrush = source[
            source.IndexOf("internal ImageBrush GetWorksheetBackgroundBrush", StringComparison.Ordinal)..
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
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();
            var visual = new System.Windows.Media.DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                grid.DrawCommentIndicator(drawingContext, new Rect(30, 18, 60, 24), CellCommentDisplayKind.Note);
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
}
