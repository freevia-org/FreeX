using System.IO;
using FluentAssertions;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Wpf;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R132 (part b): <see cref="WpfRasterPdfWriter"/> decoded each page's raster image with an unguarded
/// <c>BitmapDecoder.Create</c> -- a single undecodable page image (corrupt/truncated bytes, or bytes
/// no installed WIC codec recognizes) threw and aborted the whole PDF export/print operation, unlike
/// <see cref="Free.Shared.Pdf.PortablePdfWriter"/> and <c>SkiaPdfWriter</c>, which already omit just
/// the one bad embedded image and keep going. These tests pin down that the WPF/PDFsharp raster
/// writer now agrees: a bad page image renders that one page blank (not fabricated) and the rest of
/// the document still exports, with the loss reported through an optional diagnostics sink rather
/// than silently or via a thrown exception.
/// </summary>
public sealed class WpfRasterPdfWriterImageDecodeFallbackTests
{
    private static readonly byte[] UndecodableBytes = [0x00, 0x01, 0x02, 0x03, 0x04];

    [Fact]
    public void Write_DoesNotThrowForUndecodablePageImage()
    {
        StaTestRunner.Run(() =>
        {
            // Before the fix, DecodeBitmap's unguarded BitmapDecoder.Create call let the WIC decode
            // exception propagate straight out of Write, aborting the whole export. This subject has
            // no successfully-decodable page at all, so passing depends entirely on the fallback
            // path, not on some other page masking the bug.
            var document = new PdfRasterDocument([new PdfRasterPage(100, 80, UndecodableBytes)]);

            var act = () => WpfRasterPdfWriter.WriteToBytes(document);

            act.Should().NotThrow();
        });
    }

    [Fact]
    public void Write_StillExportsRemainingPagesWhenOnePageImageIsUndecodable()
    {
        StaTestRunner.Run(() =>
        {
            var document = new PdfRasterDocument([
                new PdfRasterPage(100, 80, MinimalPngBytes()),
                new PdfRasterPage(100, 80, UndecodableBytes),
                new PdfRasterPage(100, 80, MinimalPngBytes()),
            ]);

            using var stream = new MemoryStream();
            var pageCount = WpfRasterPdfWriter.Write(document, stream);

            pageCount.Should().Be(3, "one bad page image must not remove the surrounding good pages");
            stream.Length.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Write_SurfacesDiagnosticForUndecodablePageImage()
    {
        StaTestRunner.Run(() =>
        {
            var document = new PdfRasterDocument([new PdfRasterPage(100, 80, UndecodableBytes)]);
            var diagnostics = new List<string>();
            using var stream = new MemoryStream();

            WpfRasterPdfWriter.Write(document, stream, imageDiagnostics: diagnostics);

            diagnostics.Should().ContainSingle()
                .Which.Should().Contain("Page 1").And.Contain("could not be decoded");
        });
    }

    [Fact]
    public void Write_DoesNotEmitDiagnosticForSuccessfullyDecodedPageImage()
    {
        StaTestRunner.Run(() =>
        {
            var document = new PdfRasterDocument([new PdfRasterPage(100, 80, MinimalPngBytes())]);
            var diagnostics = new List<string>();
            using var stream = new MemoryStream();

            WpfRasterPdfWriter.Write(document, stream, imageDiagnostics: diagnostics);

            diagnostics.Should().BeEmpty();
        });
    }

    [Fact]
    public void WriteToBytes_OverloadWithDiagnostics_SurfacesUndecodableImage()
    {
        StaTestRunner.Run(() =>
        {
            var document = new PdfRasterDocument([new PdfRasterPage(100, 80, UndecodableBytes)]);
            var diagnostics = new List<string>();

            var bytes = WpfRasterPdfWriter.WriteToBytes(document, diagnostics);

            bytes.Length.Should().BeGreaterThan(0, "the document must still be produced");
            diagnostics.Should().ContainSingle();
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
