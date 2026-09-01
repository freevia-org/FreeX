using System.Text;
using FluentAssertions;
using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// r189 (backlog item 6): PortablePdfWriter never emitted an /Info dictionary.
/// <see cref="PdfContentDocument"/> has carried <c>Properties</c> all along and both the Skia and
/// WPF writers stamp them, but this fallback -- the one used whenever Skia is unavailable -- threw
/// them away, so a PDF exported on that path had no Title, Author, Subject or Keywords.
/// </summary>
public sealed class R189_PortablePdfInfoDictionaryTests
{
    private static PdfContentPage Page() =>
        new(612, 792, new PdfDrawOp[]
        {
            new PdfText(40, 706, 12, PdfFontFace.Regular, PdfColor.Black, "Body"),
        });

    [Fact]
    public void Write_WithDocumentProperties_StampsThemIntoTheInfoDictionary()
    {
        var document = new PdfContentDocument(
            new[] { Page() },
            Properties: new PdfDocumentProperties(
                Title: "Quarterly Report",
                Author: "A. Writer",
                Subject: "Revenue",
                Keywords: "revenue; quarterly",
                Creator: "FreeX"));

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(document));

        pdf.Should().Contain("/Title (Quarterly Report)");
        pdf.Should().Contain("/Author (A. Writer)");
        pdf.Should().Contain("/Subject (Revenue)");
        pdf.Should().Contain("/Keywords (revenue; quarterly)");
        pdf.Should().Contain("/Creator (FreeX)");

        // The trailer must actually point at it, or a reader never looks.
        pdf.Should().MatchRegex(@"trailer\s*<< /Size \d+ /Root 1 0 R /Info \d+ 0 R >>");
    }

    [Fact]
    public void Write_WithNoProperties_OmitsTheInfoDictionaryEntirely()
    {
        // An empty /Info is worse than none: a reader then reports the document as having blank
        // metadata rather than unspecified metadata.
        var pdf = Encoding.ASCII.GetString(
            PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { Page() })));

        pdf.Should().NotContain("/Info");
        pdf.Should().Contain("/Root 1 0 R >>");
    }

    [Fact]
    public void Write_WithWhitespaceOnlyProperties_OmitsThoseEntries()
    {
        var document = new PdfContentDocument(
            new[] { Page() },
            Properties: new PdfDocumentProperties(Title: "Real Title", Author: "   "));

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(document));

        pdf.Should().Contain("/Title (Real Title)");
        pdf.Should().NotContain("/Author", "a blank properties field must not claim an author");
    }

    [Fact]
    public void Write_WithNonAsciiTitle_StillProducesAReadableFile()
    {
        // Metadata goes through the same WinAnsi hex-escaping path as page text, so a non-ASCII
        // title must not corrupt the dictionary.
        var document = new PdfContentDocument(
            new[] { Page() },
            Properties: new PdfDocumentProperties(Title: "Rapport trimestriel été"));

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(document));

        pdf.Should().StartWith("%PDF-1.7");
        pdf.Should().Contain("/Title <");
        pdf.Should().Contain("/Info");
        pdf.Should().EndWith("%%EOF\n");
    }
}
