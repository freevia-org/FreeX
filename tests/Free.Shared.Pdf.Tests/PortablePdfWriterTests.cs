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
}
