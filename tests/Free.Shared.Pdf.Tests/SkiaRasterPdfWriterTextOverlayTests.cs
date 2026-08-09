using System.Text;
using FluentAssertions;
using Free.Shared.Pdf.Skia;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// R132: FreeP's Avalonia (Skia) raster PDF export shared the same defect FreeW's Windows raster PDF
/// export had -- <see cref="PdfRasterPage.TextOverlays"/> was accepted by the model but silently
/// ignored by <see cref="SkiaRasterPdfWriter"/>, so nothing exported through it was ever
/// searchable/selectable/screen-reader-visible, no matter what the caller supplied. These tests pin
/// down that overlays are now actually drawn (as real, embedded-font text with a ToUnicode CMap --
/// Skia's canvas API has no PDF text-render-mode-3 setter, so this is drawn at a near-zero alpha
/// instead, the closest available equivalent to PDFsharp's invisible text mode) and that the
/// no-overlay path is unaffected.
///
/// Assertions decode the PDF bytes to a string and check for literal substrings (matching the byte
/// offsets PDFsharp/Skia actually emit) rather than using FluentAssertions' collection <c>Contain</c>
/// on a raw byte array -- that overload checks each individual byte VALUE is present somewhere in the
/// collection (a set-membership check), not that the bytes appear together as a contiguous substring,
/// so it would pass on essentially any non-trivial PDF regardless of whether a font was ever embedded.
/// </summary>
public sealed class SkiaRasterPdfWriterTextOverlayTests
{
    [Fact]
    public void Write_WithTextOverlays_EmbedsFontResourceForOverlayText()
    {
        var document = new PdfRasterDocument([
            new PdfRasterPage(
                100,
                80,
                MinimalPngBytes(),
                TextOverlays:
                [
                    new PdfTextOverlay(10, 20, 12, "Arial", false, false, new PdfColor(0, 0, 0), 0, "Overlay Marker Text")
                ])
        ]);

        var pdf = Encoding.Latin1.GetString(SkiaRasterPdfWriter.WriteToBytes(document));

        pdf.Should().StartWith("%PDF-");
        pdf.Should().Contain("/Font",
            "drawing the overlay text should embed a font resource, the same way the vector Skia writer does");
        pdf.Should().Contain("ToUnicode",
            "the embedded font should carry a ToUnicode CMap so the invisible overlay text is actually searchable/copyable");
    }

    [Fact]
    public void Write_WithRotatedTextOverlay_StillEmbedsFontResource()
    {
        var document = new PdfRasterDocument([
            new PdfRasterPage(
                100,
                80,
                MinimalPngBytes(),
                TextOverlays:
                [
                    new PdfTextOverlay(10, 20, 12, "Arial", false, false, new PdfColor(0, 0, 0), 15, "Rotated Overlay Text")
                ])
        ]);

        var pdf = Encoding.Latin1.GetString(SkiaRasterPdfWriter.WriteToBytes(document));

        pdf.Should().Contain("/Font");
    }

    /// <summary>
    /// Sibling no-regression check: a page with no overlays at all must not gain a font resource --
    /// the fix must be additive (only draws when the caller supplies overlays) and must not change the
    /// plain raster-only export path FreeP's raster PDF used before this fix.
    /// </summary>
    [Fact]
    public void Write_WithoutTextOverlays_OmitsFontResource()
    {
        var document = new PdfRasterDocument([
            new PdfRasterPage(100, 80, MinimalPngBytes())
        ]);

        var pdf = Encoding.Latin1.GetString(SkiaRasterPdfWriter.WriteToBytes(document));

        pdf.Should().StartWith("%PDF-");
        pdf.Should().NotContain("/Font");
    }

    /// <summary>
    /// Sibling no-regression check: the raster page image itself must still be placed normally --
    /// the overlay fix must not touch the image the overlay sits on top of.
    /// </summary>
    [Fact]
    public void Write_StillPlacesRasterPageImageAlongsideTextOverlay()
    {
        var document = new PdfRasterDocument([
            new PdfRasterPage(
                100,
                80,
                MinimalPngBytes(),
                TextOverlays: [new PdfTextOverlay(10, 20, 12, "Arial", false, false, new PdfColor(0, 0, 0), 0, "Placed Text")])
        ]);

        var pdf = Encoding.Latin1.GetString(SkiaRasterPdfWriter.WriteToBytes(document));

        pdf.Should().Contain("/Image");
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
