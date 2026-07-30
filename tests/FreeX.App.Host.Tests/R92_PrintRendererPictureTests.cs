using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R92-consumer-wiring-sweep-1: sheet pictures (Insert &gt; Pictures, or a raster non-linked Paste
/// Special &gt; Picture) were never drawn by <see cref="PrintRenderer"/> at all -- the shared
/// <see cref="FreeX.App.Presentation.PageLayout.PageContentLayout"/> model it draws from had no
/// picture field, so an inserted picture rendered fine on screen (<c>GridView.RenderPicture</c>) but
/// was completely absent from physical print, XPS export, and Print Preview (which all share this
/// one <see cref="PrintRenderer.RenderWorksheet"/> entry point). These tests exercise that real
/// product entry point and assert actual pixel ink is painted, not just that a model field exists.
/// </summary>
public sealed class R92_PrintRendererPictureTests
{
    [Fact]
    public void RenderWorksheet_PaintsVisibleImagePicture()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Picture print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Anchor"));
            sheet.Pictures.Add(new PictureModel
            {
                Kind = PictureKind.Image,
                Anchor = new CellAddress(sheet.Id, 2, 2),
                Width = 96,
                Height = 42,
                ImageBytes = CreateSolidColorPngBytes(200, 30, 30),
                ContentType = "image/png"
            });

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            CountApproximateRgbPixels(page, 200, 30, 30).Should().BeGreaterThan(100);
        });
    }

    [Fact]
    public void RenderWorksheet_SkipsHiddenOffPageAndCellRangeSnapshotPictures()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Picture print no-regression");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Anchor"));
            sheet.Pictures.Add(new PictureModel
            {
                Kind = PictureKind.Image,
                Anchor = new CellAddress(sheet.Id, 2, 2),
                Width = 96,
                Height = 42,
                ImageBytes = CreateSolidColorPngBytes(30, 200, 30),
                ContentType = "image/png",
                IsVisible = false
            });
            sheet.Pictures.Add(new PictureModel
            {
                Kind = PictureKind.Image,
                Anchor = new CellAddress(sheet.Id, 40, 40),
                Width = 96,
                Height = 42,
                ImageBytes = CreateSolidColorPngBytes(30, 200, 30),
                ContentType = "image/png"
            });
            sheet.Pictures.Add(new PictureModel
            {
                Kind = PictureKind.CellRangeSnapshot,
                Anchor = new CellAddress(sheet.Id, 2, 2)
            });

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            CountApproximateRgbPixels(page, 30, 200, 30).Should().Be(0);
        });
    }

    /// <summary>Encodes a solid-color raster PNG in-process via WPF's own encoder, matching real picture bytes.</summary>
    private static byte[] CreateSolidColorPngBytes(byte r, byte g, byte b)
    {
        const int size = 8;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), null, new Rect(0, 0, size, size));

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static int CountApproximateRgbPixels(FrameworkElement page, byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        var width = Math.Max(1, (int)Math.Ceiling(page.Width));
        var height = Math.Max(1, (int)Math.Ceiling(page.Height));
        var size = new Size(width, height);
        page.Measure(size);
        page.Arrange(new Rect(size));
        page.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];

            if (Math.Abs(red - expectedRed) <= 3 &&
                Math.Abs(green - expectedGreen) <= 3 &&
                Math.Abs(blue - expectedBlue) <= 3)
            {
                count++;
            }
        }

        return count;
    }
}
