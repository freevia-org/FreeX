using System.Globalization;
using System.Text;
using FluentAssertions;
using Free.Shared.Pdf.Skia;

namespace Free.Shared.Pdf.Tests;

public sealed class SkiaPdfCatalogLanguageTests
{
    [Fact]
    public void AppendUpdate_ReplacesExistingLanguageAndPreservesOriginalDocumentAndTrailerEntries()
    {
        var document = new PdfContentDocument(
            [new PdfContentPage(100, 100, [])],
            new PdfDocumentProperties(Title: "Example") { Language = "en-US" });
        using var stream = new MemoryStream();
        PortablePdfWriter.Write(document, stream);
        stream.Write(" \t\r\n"u8);
        var original = stream.ToArray();
        var originalText = Encoding.ASCII.GetString(original);
        var xrefOffset = originalText.IndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var infoStart = originalText.IndexOf("/Info ", StringComparison.Ordinal);
        var infoReference = originalText[infoStart..originalText.IndexOf(" >>", infoStart, StringComparison.Ordinal)];

        SkiaPdfCatalogLanguage.AppendUpdate(stream, "fr-CA");

        var updated = stream.ToArray();
        updated[..original.Length].Should().Equal(original);
        var revision = Encoding.ASCII.GetString(updated[original.Length..]);
        revision.Should().StartWith("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Lang <FEFF00660072002D00430041> >>");
        revision.Should().NotContain("(en-US)");
        revision.Should().Contain(infoReference);
        revision.Should().Contain($"/Prev {xrefOffset.ToString(CultureInfo.InvariantCulture)}");
    }
}
