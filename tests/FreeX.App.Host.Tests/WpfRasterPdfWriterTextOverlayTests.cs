using System.Text;
using FluentAssertions;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Wpf;

namespace FreeX.App.Host.Tests;

// FreeX.App.Host (the enclosing namespace of this test class) declares its own internal
// PdfTextOverlay record with an unrelated shape; the enclosing-namespace type wins over both plain
// and aliased using-directives, so the tests below spell out the shared-tier type's full name.
using SharedPdfTextOverlay = Free.Shared.Pdf.PdfTextOverlay;

/// <summary>
/// Direct unit tests for <see cref="WpfRasterPdfWriter"/>'s selectable-text overlay contract: the
/// overlay text must be drawn in the PDF invisible text render mode (<c>3 Tr</c>) so it does not
/// double-draw on top of the identical pre-rendered raster glyphs.
/// </summary>
public sealed class WpfRasterPdfWriterTextOverlayTests
{
    [Fact]
    public void Write_DrawsTextOverlayInInvisibleRenderMode()
    {
        StaTestRunner.Run(() =>
        {
            var document = new PdfRasterDocument([
                new PdfRasterPage(
                    100,
                    80,
                    MinimalPngBytes(),
                    TextOverlays:
                    [
                        new SharedPdfTextOverlay(
                            10,
                            20,
                            12,
                            "Arial",
                            false,
                            false,
                            new PdfColor(0, 0, 0),
                            0,
                            "Invisible Overlay Text")
                    ])
            ]);

            var pdf = Encoding.Latin1.GetString(WpfRasterPdfWriter.WriteToBytes(document));

            // The overlay must be preceded by "3 Tr" (invisible render mode) and followed by
            // "0 Tr" (restore fill mode) so it does not double-draw on top of the raster image.
            pdf.Should().MatchRegex(@"3 Tr[\s\S]*?\(Invisible Overlay Text\) Tj[\s\S]*?0 Tr");
        });
    }

    [Fact]
    public void Write_DrawsRotatedTextOverlayInInvisibleRenderModeToo()
    {
        StaTestRunner.Run(() =>
        {
            var document = new PdfRasterDocument([
                new PdfRasterPage(
                    100,
                    80,
                    MinimalPngBytes(),
                    TextOverlays:
                    [
                        new SharedPdfTextOverlay(
                            10,
                            20,
                            12,
                            "Arial",
                            false,
                            false,
                            new PdfColor(0, 0, 0),
                            15,
                            "Rotated Invisible Text")
                    ])
            ]);

            var pdf = Encoding.Latin1.GetString(WpfRasterPdfWriter.WriteToBytes(document));

            pdf.Should().MatchRegex(@"3 Tr[\s\S]*?\(Rotated Invisible Text\) Tj[\s\S]*?0 Tr");
        });
    }

    /// <summary>
    /// Sibling no-regression check: the raster page image itself (the pre-rendered background the
    /// overlay sits on top of) must still be placed normally — the invisible-text-mode fix must not
    /// touch the image XObject placement it lives alongside.
    /// </summary>
    [Fact]
    public void Write_StillPlacesRasterPageImageAlongsideInvisibleTextOverlay()
    {
        StaTestRunner.Run(() =>
        {
            var document = new PdfRasterDocument([
                new PdfRasterPage(
                    100,
                    80,
                    MinimalPngBytes(),
                    TextOverlays:
                    [
                        new SharedPdfTextOverlay(
                            10,
                            20,
                            12,
                            "Arial",
                            false,
                            false,
                            new PdfColor(0, 0, 0),
                            0,
                            "Placed Text")
                    ])
            ]);

            var pdf = Encoding.Latin1.GetString(WpfRasterPdfWriter.WriteToBytes(document));

            pdf.Should().Contain("/Subtype/Image");
            pdf.Should().Contain(" Do", "the raster background image must still be drawn via the Do operator");
        });
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
