using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-60 fixes for src/FreeX.App.UI drawing-object rendering:
///   R60-render-drawing-shapes-6-1: charts must draw in their recorded z-order slot instead of
///     always being forced behind every shape/picture/textbox.
///   R60-render-drawing-shapes-6-3: an inserted picture must not get an unauthored flat gray border.
///   R60-render-drawing-shapes-6-4: shape text with "Wrap text in shape" off must stay on one line.
/// </summary>
public sealed class GridViewRound60DrawingObjectTests
{
    // A minimal 1x1 PNG (from WpfBitmapImageLoaderTests' fixture) -- decodable by
    // WpfBitmapImageLoader.TryLoad so PictureModel.Kind == Image takes the "real image" branch.
    private static readonly byte[] OnePixelPng =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    [Fact]
    public void DrawingObjectRendering_RendersChartsInExplicitZOrderInsteadOfAlwaysBehind()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var shape = new DrawingShapeModel
            {
                Anchor = new CellAddress(sheetId, 1, 1),
                Kind = DrawingShapeKind.Rectangle,
                Width = 150,
                Height = 150,
                FillColor = new CellColor(0, 0, 255) // opaque blue
            };
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                Left = 30,
                Top = 30,
                Width = 80,
                Height = 80,
                ChartAreaFillColor = new CellColor(255, 0, 0) // opaque red
            };
            var grid = new GridView
            {
                Width = 200,
                Height = 200,
                ShowHeaders = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 200, 0)],
                    [new ColMetric(1, 200, 0)]),
                DrawingShapes = [shape],
                Charts = [chart],
                // The chart was inserted AFTER the shape, so Excel's "later object = higher
                // z-order" rule means the chart must render ON TOP of the shape here.
                DrawingObjectZOrder =
                [
                    new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id),
                    new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id)
                ]
            };

            grid.Measure(new Size(200, 200));
            grid.Arrange(new Rect(0, 0, 200, 200));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            var pixels = new byte[200 * 200 * 4];
            bitmap.CopyPixels(pixels, stride: 200 * 4, offset: 0);

            // (70,70) is inside both the shape's (0,0)-(150,150) rect and the chart's
            // (30,30)-(110,110) rect. Pbgra32 byte order is B,G,R,A.
            var offset = (70 * 200 + 70) * 4;
            pixels[offset + 2].Should().BeGreaterThan(200,
                "the chart's opaque red chart-area fill was inserted after the shape and must win the overlap");
            pixels[offset + 0].Should().BeLessThan(60,
                "the shape's blue fill must not show through the chart that was inserted on top of it");
        });
    }

    [Fact]
    public void DrawingObjectRendering_StillRendersChartsBehindShapesWithNoExplicitZOrder()
    {
        // Sibling/no-regression: sheets with no recorded z-order at all (the vast majority of
        // existing documents) must keep the pre-existing "charts render first" fallback -- only
        // the explicit-z-order path changed.
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var shape = new DrawingShapeModel
            {
                Anchor = new CellAddress(sheetId, 1, 1),
                Kind = DrawingShapeKind.Rectangle,
                Width = 150,
                Height = 150,
                FillColor = new CellColor(0, 0, 255)
            };
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                Left = 30,
                Top = 30,
                Width = 80,
                Height = 80,
                ChartAreaFillColor = new CellColor(255, 0, 0)
            };
            var grid = new GridView
            {
                Width = 200,
                Height = 200,
                ShowHeaders = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 200, 0)],
                    [new ColMetric(1, 200, 0)]),
                DrawingShapes = [shape],
                Charts = [chart]
                // No DrawingObjectZOrder set -- legacy/no-explicit-order path.
            };

            grid.Measure(new Size(200, 200));
            grid.Arrange(new Rect(0, 0, 200, 200));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            var pixels = new byte[200 * 200 * 4];
            bitmap.CopyPixels(pixels, stride: 200 * 4, offset: 0);

            var offset = (70 * 200 + 70) * 4;
            pixels[offset + 0].Should().BeGreaterThan(200,
                "with no recorded z-order, charts still render first/bottom-most and the shape drawn after must win the overlap");
            pixels[offset + 2].Should().BeLessThan(60);
        });
    }

    [Fact]
    public void PictureRendering_DoesNotDrawUnauthoredBorderAroundInsertedImage()
    {
        WpfTestThread.Run(() =>
        {
            var picture = new PictureModel
            {
                Anchor = new CellAddress(SheetId.New(), 1, 1),
                Kind = PictureKind.Image,
                ImageBytes = OnePixelPng,
                Width = 100,
                Height = 60
            };
            var grid = new GridView
            {
                Width = 140,
                Height = 100,
                ShowHeaders = false,
                ShowGridLines = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 100, 0)],
                    [new ColMetric(1, 140, 0)]),
                Pictures = [picture]
            };

            grid.Measure(new Size(140, 100));
            grid.Arrange(new Rect(0, 0, 140, 100));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(140, 100, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            CountBorderLikePixels(bitmap, new Rect(0, 0, 100, 60))
                .Should().Be(0,
                    "Excel draws no border on an inserted picture unless one is explicitly authored");
        });
    }

    [Fact]
    public void PictureRendering_StillDrawsBorderAroundCellRangeSnapshotPictures()
    {
        // Sibling/no-regression: the OTHER picture kind (a "paste as picture" cell-range
        // snapshot, PictureModel's default Kind) intentionally keeps its frame -- only the
        // real-image path's unauthored border was removed.
        WpfTestThread.Run(() =>
        {
            var picture = new PictureModel
            {
                Anchor = new CellAddress(SheetId.New(), 1, 1),
                Kind = PictureKind.CellRangeSnapshot,
                SourceRowCount = 1,
                SourceColumnCount = 1,
                Width = 100,
                Height = 60
            };
            var grid = new GridView
            {
                Width = 140,
                Height = 100,
                ShowHeaders = false,
                ShowGridLines = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 100, 0)],
                    [new ColMetric(1, 140, 0)]),
                Pictures = [picture]
            };

            grid.Measure(new Size(140, 100));
            grid.Arrange(new Rect(0, 0, 140, 100));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(140, 100, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            CountBorderLikePixels(bitmap, new Rect(0, 0, 100, 60))
                .Should().BeGreaterThan(0,
                    "the cell-range-snapshot picture path is untouched and must keep its frame");
        });
    }

    [Fact]
    public void ShapeTextRendering_StaysOnOneLineWhenWrapIsDisabled()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();
            var shape = new DrawingShapeModel
            {
                Kind = DrawingShapeKind.Rectangle,
                ShapeText = "Excel shape text overflow test line",
                ShapeTextWrap = false,
                ShapeTextFontSizePoints = 11
            };

            var textSpanHeight = MeasureShapeTextVerticalSpan(grid, shape, new Rect(4, 4, 60, 200));

            textSpanHeight.Should().BeLessThan(24,
                "with 'Wrap text in shape' off, Excel keeps the text on a single unconstrained line " +
                "instead of word-wrapping it across the narrow shape width");
        });
    }

    [Fact]
    public void ShapeTextRendering_StillWrapsAcrossMultipleLinesWhenWrapIsEnabled()
    {
        // Sibling/no-regression: the wrap-ON path (the common case) must keep wrapping.
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();
            var shape = new DrawingShapeModel
            {
                Kind = DrawingShapeKind.Rectangle,
                ShapeText = "Excel shape text overflow test line",
                ShapeTextWrap = true,
                ShapeTextFontSizePoints = 11
            };

            var textSpanHeight = MeasureShapeTextVerticalSpan(grid, shape, new Rect(4, 4, 60, 200));

            textSpanHeight.Should().BeGreaterThan(24,
                "with 'Wrap text in shape' on, the long line must still wrap across multiple lines " +
                "inside the narrow shape width");
        });
    }

    private static int MeasureShapeTextVerticalSpan(GridView grid, DrawingShapeModel shape, Rect rect)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            grid.DrawShapeText(dc, shape, rect, 1.0);
        }

        const int width = 100;
        const int height = 220;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, stride: width * 4, offset: 0);

        int? firstRow = null;
        int? lastRow = null;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                if (pixels[offset + 3] > 10)
                {
                    firstRow ??= y;
                    lastRow = y;
                    break;
                }
            }
        }

        firstRow.Should().NotBeNull("the shape text should render at least one visible pixel");
        return lastRow!.Value - firstRow!.Value + 1;
    }

    // Counts pixels in a band around the rect's perimeter that are neither transparent nor
    // (near-)white. The white-filled interior (picture background / decoded 1x1-white test image)
    // means any darker pixel found right at the edge can only be a drawn border stroke -- this
    // avoids assuming exactly which gray the pen resolves to after anti-aliasing/sub-pixel offsets.
    private static int CountBorderLikePixels(RenderTargetBitmap bitmap, Rect rect, int band = 2)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var top = (int)Math.Round(rect.Top);
        var left = (int)Math.Round(rect.Left);
        var right = (int)Math.Round(rect.Right);
        var bottom = (int)Math.Round(rect.Bottom);

        bool IsBorderLike(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return false;

            var offset = (y * width + x) * 4;
            if (pixels[offset + 3] < 10)
                return false;

            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            return r < 235 && g < 235 && b < 235;
        }

        var count = 0;
        for (var x = left - band; x <= right + band; x++)
        {
            for (var dy = -band; dy <= band; dy++)
            {
                if (IsBorderLike(x, top + dy))
                    count++;
                if (IsBorderLike(x, bottom + dy))
                    count++;
            }
        }

        for (var y = top - band; y <= bottom + band; y++)
        {
            for (var dx = -band; dx <= band; dx++)
            {
                if (IsBorderLike(left + dx, y))
                    count++;
                if (IsBorderLike(right + dx, y))
                    count++;
            }
        }

        return count;
    }
}
