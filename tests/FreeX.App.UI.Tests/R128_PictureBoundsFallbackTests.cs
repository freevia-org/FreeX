using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R128-render-drawing-pictures: a picture inserted via Excel's Insert &gt; Pictures &gt;
/// "Link to File" (no "Insert and Link") has an &lt;a:blip r:link=...&gt; with no r:embed --
/// FreeX round-trips this deliberately (PictureModel.LinkedImageTarget, R65-io-image-drawing-6-1)
/// with ImageBytes left empty. Before the fix, RenderPicture's Kind == PictureKind.Image gate only
/// special-cased the picture when TryLoadPictureImage succeeded; on failure (empty/undecodable
/// bytes) it fell through into the CellRangeSnapshot-only rows/cols/Cells loop, whose only visible
/// effect for an ordinary image picture (SourceRowCount/SourceColumnCount both default to 0, so
/// rows=cols=1 and Cells is empty) was an unconditional, fully opaque Brushes.White rectangle --
/// silently hiding whatever grid content sat under the picture's anchor with no indication a
/// picture was even there. The fix draws a translucent, bordered, labeled placeholder instead,
/// mirroring the Avalonia shell's DrawingObjectRenderPlanner BoundsFallback treatment.
/// </summary>
public sealed class R128_PictureBoundsFallbackTests
{
    // A minimal 1x1 PNG (same fixture GridViewRound60DrawingObjectTests uses) -- decodable by
    // WpfBitmapImageLoader.TryLoad so PictureModel.Kind == Image takes the "real image" branch.
    private static readonly byte[] OnePixelPng =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    private static byte[] RenderLinkedPictureOverDarkShape(out Rect pictureRect)
    {
        var pixels = new byte[200 * 150 * 4];
        var capturedRect = new Rect(20, 20, 120, 80);

        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            // A dark shape drawn BEHIND the picture (shapes render before pictures when there is no
            // explicit z-order -- GridView.DrawingObjectLayerCache.RenderDrawingObjectLayers) so we
            // can tell whether the picture placeholder fully occludes it (pre-fix, opaque white) or
            // leaves it partially visible (post-fix, translucent).
            var shape = new DrawingShapeModel
            {
                Anchor = new CellAddress(sheetId, 1, 1),
                Kind = DrawingShapeKind.Rectangle,
                Width = 120,
                Height = 80,
                FillColor = new CellColor(10, 10, 10) // near-black, opaque
            };
            var linkedPicture = new PictureModel
            {
                Anchor = new CellAddress(sheetId, 1, 1),
                Kind = PictureKind.Image,
                Name = "LinkedPhoto.jpg",
                ImageBytes = null, // "Link to File": nothing embedded to decode
                LinkedImageTarget = "file:///C:/Images/LinkedPhoto.jpg",
                Width = 120,
                Height = 80
            };
            var grid = new GridView
            {
                Width = 200,
                Height = 150,
                ShowHeaders = false,
                ShowGridLines = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 150, 0)],
                    [new ColMetric(1, 200, 0)]),
                DrawingShapes = [shape],
                Pictures = [linkedPicture]
            };

            grid.Measure(new Size(200, 150));
            grid.Arrange(new Rect(0, 0, 200, 150));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(200, 150, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);
            bitmap.CopyPixels(pixels, stride: 200 * 4, offset: 0);
        });

        pictureRect = capturedRect;
        return pixels;
    }

    private static (byte B, byte G, byte R, byte A) SamplePixel(byte[] pixels, int width, int x, int y)
    {
        var offset = (y * width + x) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    [Fact]
    public void LinkedPicture_WithNoImageBytes_LeavesUnderlyingContentPartiallyVisible()
    {
        // Sample deep in the interior of the shape/picture overlap rect (20,20)-(140,100) to avoid
        // both the picture's border pen and the shape's own edge.
        var pixels = RenderLinkedPictureOverDarkShape(out _);
        var (b, g, r, _) = SamplePixel(pixels, 200, 70, 55);

        // Pre-fix: the CellRangeSnapshot fallback's unconditional Brushes.White rectangle painted
        // fully opaque white (255,255,255) over the near-black shape, completely hiding it.
        // Post-fix: the translucent teal placeholder (alpha 42) blends with the near-black shape
        // underneath, so the pixel must be far darker than pure white on every channel.
        ((int)r + g + b).Should().BeLessThan(600,
            "the placeholder for a picture with no loadable bytes must be translucent so grid " +
            "content underneath (here, a near-black shape) stays at least partly visible, instead " +
            "of an opaque white rectangle that hides it completely");
    }

    [Fact]
    public void LinkedPicture_WithNoImageBytes_IsNotPureOpaqueWhite()
    {
        var pixels = RenderLinkedPictureOverDarkShape(out _);
        var (b, g, r, a) = SamplePixel(pixels, 200, 70, 55);

        (r == 255 && g == 255 && b == 255 && a == 255).Should().BeFalse(
            "an image-kind picture whose bytes fail to decode must not render as a plain opaque " +
            "white rectangle with no indication a picture is even present");
    }

    [Fact]
    public void DecodablePicture_StillRendersItsImage_NoRegression()
    {
        // Sibling/no-regression: an Image-kind picture whose bytes DO decode must keep taking the
        // normal DrawImage path -- only the "bytes fail to load" branch changed.
        var pixels = new byte[140 * 100 * 4];

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
            bitmap.CopyPixels(pixels, stride: 140 * 4, offset: 0);
        });

        // The decoded 1x1 PNG is opaque white, stretched to fill the whole rect -- interior pixels
        // must be pure opaque white, same as before the fix (this path is untouched).
        var (b, g, r, a) = SamplePixel(pixels, 140, 50, 30);
        (r == 255 && g == 255 && b == 255 && a == 255).Should().BeTrue(
            "a picture whose bytes DO decode successfully must keep rendering its actual image, " +
            "unaffected by the bounds-fallback placeholder added for pictures with no loadable bytes");
    }

    [Fact]
    public void CellRangeSnapshotPicture_StillUsesOpaqueWhiteFill_NoRegression()
    {
        // Sibling/no-regression: PictureKind.CellRangeSnapshot (Copy > Paste as Picture / camera
        // pictures) is the ONLY kind that should ever reach the rows/cols/Cells loop and its
        // opaque Brushes.White background -- that loop's behavior is intentionally untouched.
        var pixels = new byte[140 * 100 * 4];

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
            bitmap.CopyPixels(pixels, stride: 140 * 4, offset: 0);
        });

        var (b, g, r, a) = SamplePixel(pixels, 140, 50, 30);
        (r == 255 && g == 255 && b == 255 && a == 255).Should().BeTrue(
            "a genuine CellRangeSnapshot picture must keep its opaque white background -- the fix " +
            "only gates the Image-kind bytes-failed-to-load path, not this kind's own rendering");
    }
}
