using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewDrawingObjectThemeTests
{
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
        var getCroppedBrush = source[
            source.IndexOf("private ImageBrush GetCroppedPictureBrush", StringComparison.Ordinal)..
            source.IndexOf("private void RenderWorksheetBackground", StringComparison.Ordinal)];

        GetStaticResource<Pen>("PictureBorderPen").IsFrozen.Should().BeTrue();
        GetStaticResource<Pen>("PictureGridPen").IsFrozen.Should().BeTrue();
        GetStaticResource<Brush>("PictureSelectionBrush").IsFrozen.Should().BeTrue();
        GetStaticResource<Pen>("PictureSelectionPen").IsFrozen.Should().BeTrue();
        source.Should().Contain("private const int CroppedPictureBrushCacheLimit = 256;");
        source.Should().Contain("private readonly Dictionary<CroppedPictureBrushCacheKey, ImageBrush> _croppedPictureBrushCache = new();");
        source.Should().Contain("private readonly record struct CroppedPictureBrushCacheKey(");
        source.Should().Contain("private static readonly Pen PictureBorderPen = CreateFrozenPen");
        source.Should().Contain("private static readonly Pen PictureGridPen = CreateFrozenPen");
        source.Should().Contain("private static readonly Brush PictureSelectionBrush = MakeBrush");
        source.Should().Contain("private static readonly Pen PictureSelectionPen = CreateFrozenPen");
        renderPictures.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderPictures.Should().Contain("var brush = GetCroppedPictureBrush(picture, image);");
        renderPictures.Should().Contain("GetDrawingObjectText(");
        renderPictures.Should().Contain("pixelsPerDip,");
        renderPictures.Should().Contain("TextTrimming.CharacterEllipsis");
        renderPictures.Should().Contain("dc.PushClip(GetDrawingObjectClipGeometry(textRect));");
        renderPictures.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip)");
        renderPictures.Should().NotContain("new ImageBrush");
        renderPictures.Should().NotContain("new FormattedText(");
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
            var grid = new GridView();
            var visual = new System.Windows.Media.DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                var drawCommentIndicator = typeof(GridView).GetMethod(
                    "DrawCommentIndicator",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                drawCommentIndicator!.Invoke(grid, [drawingContext, new Rect(30, 18, 60, 24)]);
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
