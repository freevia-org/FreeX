using System.Text;
using FluentAssertions;
using Free.Shared.Pdf.Skia;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// R137: <see cref="SkiaRasterPdfWriter"/> accepted <see cref="PdfRasterPage.LinkOverlays"/> in the
/// shared model but silently ignored them -- unlike <c>WpfRasterPdfWriter</c>, which already turned
/// external-URI overlays into real <c>/Subtype /Link</c> annotations, the Skia/Avalonia raster backend
/// dropped every hyperlink FreeP's raster PDF export (its default File &gt; Export as PDF / Print &gt;
/// Full Page Slides path on Linux/macOS) tried to carry. These tests pin down that external URI
/// overlays now become real, clickable link annotations and that the no-overlay path is unaffected.
///
/// Assertions decode the PDF bytes to a string and check for literal substrings rather than relying on
/// FluentAssertions' collection <c>Contain</c> on a raw byte array (which only checks byte-VALUE
/// set-membership, not contiguous substring match) -- matching
/// <see cref="SkiaRasterPdfWriterTextOverlayTests"/>'s established verification approach for this
/// writer.
/// </summary>
public sealed class SkiaRasterPdfWriterLinkOverlayTests
{
    private const string LinkUri = "https://example.com/freep-skia-link-test";

    [Fact]
    public void Write_WithExternalLinkOverlay_EmitsClickableLinkAnnotation()
    {
        var document = new PdfRasterDocument([
            new PdfRasterPage(
                100,
                80,
                MinimalPngBytes(),
                LinkOverlays: [new PdfLinkOverlay(10, 10, 40, 20, LinkUri, "Open link")])
        ]);

        var pdf = Encoding.Latin1.GetString(SkiaRasterPdfWriter.WriteToBytes(document));

        pdf.Should().StartWith("%PDF-");
        pdf.Should().Contain("/Subtype /Link",
            "an external-URI overlay should become a real clickable link annotation, not just invisible text");
        pdf.Should().Contain(LinkUri);
    }

    /// <summary>
    /// Sibling no-regression check: a page with no link overlays at all must not gain any annotation --
    /// the fix must be additive and must not change the plain raster-only export path.
    /// </summary>
    [Fact]
    public void Write_WithoutLinkOverlays_OmitsLinkAnnotation()
    {
        var document = new PdfRasterDocument([
            new PdfRasterPage(100, 80, MinimalPngBytes())
        ]);

        var pdf = Encoding.Latin1.GetString(SkiaRasterPdfWriter.WriteToBytes(document));

        pdf.Should().StartWith("%PDF-");
        pdf.Should().NotContain("/Subtype /Link");
    }

    /// <summary>
    /// Sibling no-regression check: the raster page image itself must still be placed normally --
    /// adding the link annotation must not touch the image it sits on top of.
    /// </summary>
    [Fact]
    public void Write_StillPlacesRasterPageImageAlongsideLinkOverlay()
    {
        var document = new PdfRasterDocument([
            new PdfRasterPage(
                100,
                80,
                MinimalPngBytes(),
                LinkOverlays: [new PdfLinkOverlay(10, 10, 40, 20, LinkUri)])
        ]);

        var pdf = Encoding.Latin1.GetString(SkiaRasterPdfWriter.WriteToBytes(document));

        pdf.Should().Contain("/Image");
    }

    /// <summary>
    /// An internal (slide-to-slide) overlay has no <see cref="PdfLinkOverlay.Uri"/>, only a
    /// <see cref="PdfLinkOverlay.DestinationName"/>. This raster backend has no cross-page named
    /// destination table yet (see AddLinkAnnotations' doc comment), so it must skip such an overlay
    /// rather than emit a broken annotation.
    /// </summary>
    [Fact]
    public void Write_WithInternalOnlyLinkOverlay_SkipsItRatherThanEmittingABrokenAnnotation()
    {
        var document = new PdfRasterDocument([
            new PdfRasterPage(
                100,
                80,
                MinimalPngBytes(),
                LinkOverlays: [new PdfLinkOverlay(10, 10, 40, 20, Uri: null, DestinationName: "some-destination")])
        ]);

        var act = () => SkiaRasterPdfWriter.WriteToBytes(document);

        act.Should().NotThrow();
        var pdf = Encoding.Latin1.GetString(act());
        pdf.Should().NotContain("/Subtype /Link");
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
