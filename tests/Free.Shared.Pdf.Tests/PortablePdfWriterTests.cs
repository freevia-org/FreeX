using System.Text;
using FluentAssertions;
using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Tests;

public sealed class PortablePdfWriterTests
{
    [Fact]
    public void Write_EmitsValidPdfWithTextFillAndStroke()
    {
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfFillRect(36, 700, 100, 22, new PdfColor(238, 242, 247)),
            new PdfStrokeRect(36, 700, 100, 22, new PdfColor(196, 202, 210), 0.5),
            new PdfText(40, 706, 12, PdfFontFace.Bold, PdfColor.Black, "Hello"),
        });
        var document = new PdfContentDocument(new[] { page });

        var bytes = PortablePdfWriter.WriteToBytes(document);

        var pdf = Encoding.ASCII.GetString(bytes);
        pdf.Should().StartWith("%PDF-1.7");
        pdf.Should().Contain("/Type /Catalog");
        pdf.Should().Contain("/Encoding /WinAnsiEncoding");
        pdf.Should().Contain("(Hello) Tj");
        pdf.Should().Contain("100 22 re f");
        pdf.Should().Contain("0.5 w");
        pdf.Should().Contain("xref");
        pdf.Should().EndWith("%%EOF\n");
    }

    [Fact]
    public void Write_EncodesWinAnsiTextAsHex()
    {
        // C=43 a=61 f=66 e-acute(é)=E9 space=20 euro(€)=80
        var text = "Café €";
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, text),
        });

        var bytes = PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));

        // C=43 a=61 f=66 é=E9 space=20 €=80
        var pdf = Encoding.ASCII.GetString(bytes);
        pdf.Should().Contain("<436166E92080> Tj");
    }

    [Fact]
    public void Write_ThrowsForNonWinAnsiText()
    {
        var text = "Київ"; // Kyiv (Cyrillic)
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, text),
        });

        var act = () => PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Portable PDF export currently supports ASCII and WinAnsi text only;*");
    }

    [Fact]
    public void Write_SupportsMultiplePagesWithDifferentSizes()
    {
        var pages = new[]
        {
            new PdfContentPage(612, 792, new PdfDrawOp[] { new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, "P1") }),
            new PdfContentPage(842, 595, new PdfDrawOp[] { new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, "P2") }),
        };

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(pages)));

        pdf.Should().Contain("/Count 2");
        pdf.Should().Contain("MediaBox [0 0 612 792]");
        pdf.Should().Contain("MediaBox [0 0 842 595]");
    }

    [Fact]
    public void Write_EmitsPdfLineAsMoveThenLineStroke()
    {
        // PdfLine should emit a PDF path: m (moveto), l (lineto), S (stroke).
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfLine(36, 700, 576, 700, new PdfColor(180, 185, 190), 0.4),
        });

        var bytes = PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));
        var pdf   = Encoding.ASCII.GetString(bytes);

        pdf.Should().Contain("36 700 m",   "PdfLine must emit PDF moveto at (x1, y1)");
        pdf.Should().Contain("576 700 l S","PdfLine must emit lineto then stroke");
        pdf.Should().Contain("0.4 w",      "PdfLine must emit the specified line width");
    }

    [Fact]
    public void Write_PdfLineRoundTripsCorrectCoordinates()
    {
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfLine(10, 20, 100, 200, new PdfColor(0, 0, 0), 1.0),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        // Coordinates must appear verbatim in the content stream.
        pdf.Should().Contain("10 20 m",    "moveto x1 y1");
        pdf.Should().Contain("100 200 l S","lineto x2 y2 then stroke");
    }

    [Fact]
    public void Write_EmitsFilledTrianglePath()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfFilledTriangle(20, 30, 12, 25, 12, 35, new PdfColor(0x11, 0x22, 0x33)),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("0.067 0.133 0.2 rg");
        pdf.Should().Contain("20 30 m");
        pdf.Should().Contain("12 25 l");
        pdf.Should().Contain("12 35 l f");
    }

    [Fact]
    public void Write_EmitsFilledAndStrokedCustomPath()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfPath(
                [
                    new PdfPathContour(
                        new PdfPathPoint(10, 20),
                        [
                            PdfPathSegment.LineTo(new PdfPathPoint(30, 20)),
                            PdfPathSegment.BezierTo(
                                new PdfPathPoint(35, 35),
                                new PdfPathPoint(20, 45),
                                new PdfPathPoint(10, 40)),
                        ],
                        Closed: true),
                ],
                new PdfColor(0x11, 0x22, 0x33),
                new PdfColor(0x44, 0x55, 0x66),
                1.25),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("0.067 0.133 0.2 rg");
        pdf.Should().Contain("0.267 0.333 0.4 RG");
        pdf.Should().Contain("1.25 w");
        pdf.Should().Contain("10 20 m\n30 20 l\n35 35 20 45 10 40 c\nh\nB");
    }

    [Fact]
    public void Write_EmitsFilledAndStrokedEllipsePaths()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfFillEllipse(10, 20, 40, 20, new PdfColor(0x11, 0x22, 0x33)),
            new PdfStrokeEllipse(10, 20, 40, 20, new PdfColor(0x44, 0x55, 0x66), 1.5),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("0.067 0.133 0.2 rg");
        pdf.Should().Contain("50 30 m");
        pdf.Should().Contain("50 35.523 41.046 40 30 40 c");
        pdf.Should().Contain("18.954 20 30 20 c");
        pdf.Should().Contain("f");
        pdf.Should().Contain("0.267 0.333 0.4 RG");
        pdf.Should().Contain("1.5 w");
        pdf.Should().Contain("S");
    }

    [Fact]
    public void Write_EmitsRotationGroupSaveTransformAndRestore()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfRotationGroup(
                20,
                20,
                90,
                new PdfDrawOp[]
                {
                    new PdfFillRect(10, 15, 20, 10, new PdfColor(0x11, 0x22, 0x33)),
                    new PdfText(12, 18, 8, PdfFontFace.Bold, PdfColor.Black, "Rotated"),
                }),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("q\n0 -1 1 0 0 40 cm\nq");
        pdf.Should().Contain("10 15 20 10 re f");
        pdf.Should().Contain("(Rotated) Tj");
        pdf.Should().Contain("Q\nendstream", "the grouped content must restore the graphics state before closing the stream");
    }

    [Fact]
    public void Write_EmitsPngImageXObjectAndPlacement()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, MinimalPngBytes(), "image/png"),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("/XObject << /Im1 ");
        pdf.Should().Contain("/Subtype /Image");
        pdf.Should().Contain("/Width 1 /Height 1");
        pdf.Should().Contain("/ColorSpace /DeviceRGB");
        pdf.Should().Contain("/Filter /FlateDecode");
        pdf.Should().Contain("20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_EmitsRotatedImagePlacement()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, MinimalPngBytes(), "image/png", RotationDegrees: 90),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("0 -20 10 0 15 45 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_EmitsImageOpacityExtGState()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, MinimalPngBytes(), "image/png", Opacity: 0.5),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("/ExtGState << /GS1 ");
        pdf.Should().Contain("<< /Type /ExtGState /ca 0.5 /CA 0.5 >>");
        pdf.Should().Contain("/GS1 gs\n20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_EmitsSourceCroppedImagePlacementWithDestinationClip()
    {
        var page = new PdfContentPage(120, 90, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                20,
                80,
                40,
                MinimalJpegBytes(),
                "image/jpeg",
                SourceCrop: new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375)),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("10 20 80 40 re W n");
        pdf.Should().Contain("160 0 0 80 -30 -10 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_ClipsImageToEllipse()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                30,
                20,
                10,
                MinimalPngBytes(),
                "image/png",
                ClipKind: PdfImageClipKind.Ellipse),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("30 35 m");
        pdf.Should().Contain("30 37.761 25.523 40 20 40 c");
        pdf.Should().Contain("W n\n20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_ClipsImageToRoundedRectangle()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                30,
                20,
                10,
                MinimalPngBytes(),
                "image/png",
                ClipKind: PdfImageClipKind.RoundedRectangle),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("11.8 30 m");
        pdf.Should().Contain("28.2 30 l");
        pdf.Should().Contain("W n\n20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_EmbedsJpegImageWithDctDecode()
    {
        var page = new PdfContentPage(120, 90, new PdfDrawOp[]
        {
            new PdfImage(12, 20, 48, 36, MinimalJpegBytes(), "image/jpeg"),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("/Subtype /Image");
        pdf.Should().Contain("/Width 16 /Height 16");
        pdf.Should().Contain("/ColorSpace /DeviceRGB");
        pdf.Should().Contain("/Filter /DCTDecode");
        pdf.Should().Contain("48 0 0 36 12 20 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_SkipsUnsupportedImageContentTypes()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, new byte[] { 1, 2, 3, 4 }, "image/gif"),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().NotContain("/Subtype /Image");
        pdf.Should().NotContain("/XObject");
        pdf.Should().NotContain("/Im1 Do");
    }

    [Fact]
    public void Write_SkipsMalformedSupportedImageBytes()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png"),
            new PdfImage(40, 30, 20, 10, new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"),
            new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, "Still exports"),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().NotContain("/Subtype /Image");
        pdf.Should().NotContain("/XObject");
        pdf.Should().NotContain("/Im1 Do");
        pdf.Should().Contain("(Still exports) Tj");
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

    private static byte[] MinimalJpegBytes() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==");
}
