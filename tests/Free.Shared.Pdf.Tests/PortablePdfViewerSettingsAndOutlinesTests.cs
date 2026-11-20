using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Tests;

public sealed class PortablePdfViewerSettingsAndOutlinesTests
{
    [Fact]
    public void Write_WithViewerSettings_EmitsCatalogPreferencesWithoutCreatingAnOutline()
    {
        var document = new PdfContentDocument([new PdfContentPage(100, 80, [])],
            new PdfDocumentProperties(Title: "Published worksheet")
            {
                ViewerSettings = new PdfDocumentViewerSettings(
                    PdfViewerPageLayout.TwoColumnRight, PdfViewerOpenMode.FullScreen, DisplayDocumentTitle: true),
            });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(document));
        var catalog = ReadObject(pdf, 1);

        catalog.Should().Contain("/Version /1.7");
        catalog.Should().Contain("/PageLayout /TwoColumnRight");
        catalog.Should().Contain("/PageMode /FullScreen");
        catalog.Should().Contain("/ViewerPreferences <<");
        catalog.Should().Contain("/DisplayDocTitle true");
        catalog.Should().Contain("/PrintScaling /None").And.Contain("/PickTrayByPDFSize true");
        catalog.Should().NotContain("/Outlines");
    }

    [Fact]
    public void Write_WithSheetOutlines_LinksUnicodeTitlesToTheOutputPagesAndPreservesExistingRefs()
    {
        const string firstTitle = "報告 (北) 📊";
        const string secondTitle = @"Q2 \ Summary";
        var pages = new PdfContentPage[]
        {
            new(
                100, 80, [],
                [new PdfLinkOverlay(10, 10, 40, 20, null, "Jump to target", "Target")]),
            new(
                200, 90, [],
                NamedDestinations: [new PdfNamedDestination("Target", 12, 20)]),
        };
        var document = new PdfContentDocument(pages,
            new PdfDocumentProperties(Title: "Published worksheet")
            {
                Language = "en-US",
                Outlines = [new PdfOutlineEntry(firstTitle, 1), new PdfOutlineEntry(secondTitle, 0)],
            });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(document));
        var catalog = ReadObject(pdf, 1);
        catalog.Should().Contain("/Lang (en-US)");
        catalog.Should().Contain("/PageMode /UseOutlines");

        var pagesTree = ReadObject(pdf, 2);
        var kids = Regex.Match(pagesTree, @"/Kids \[(\d+) 0 R (\d+) 0 R\]");
        kids.Success.Should().BeTrue();
        var firstPageId = int.Parse(kids.Groups[1].Value, CultureInfo.InvariantCulture);
        var secondPageId = int.Parse(kids.Groups[2].Value, CultureInfo.InvariantCulture);
        ReadObject(pdf, secondPageId).Should().Contain("/MediaBox [0 0 200 90]");

        var outlineRootId = ReadReference(catalog, "Outlines");
        var outlineRoot = ReadObject(pdf, outlineRootId);
        outlineRoot.Should().Contain("/Type /Outlines").And.Contain("/Count 2");
        var firstId = ReadReference(outlineRoot, "First");
        var lastId = ReadReference(outlineRoot, "Last");
        var first = ReadObject(pdf, firstId);
        var last = ReadObject(pdf, lastId);
        ReadTitle(first).Should().Be(firstTitle);
        ReadTitle(last).Should().Be(secondTitle);
        ReadReference(first, "Parent").Should().Be(outlineRootId);
        ReadReference(last, "Parent").Should().Be(outlineRootId);
        ReadReference(first, "Next").Should().Be(lastId);
        ReadReference(last, "Prev").Should().Be(firstId);
        first.Should().NotContain("/Prev").And.Contain($"/Dest [{secondPageId} 0 R /XYZ null null null]");
        last.Should().NotContain("/Next").And.Contain($"/Dest [{firstPageId} 0 R /XYZ null null null]");

        var firstPage = ReadObject(pdf, firstPageId);
        var annotation = Regex.Match(firstPage, @"/Annots \[(\d+) 0 R\]");
        annotation.Success.Should().BeTrue();
        var annotationId = int.Parse(annotation.Groups[1].Value, CultureInfo.InvariantCulture);
        ReadObject(pdf, annotationId).Should().Contain($"/Dest [{secondPageId} 0 R /XYZ 12 70 null]");
        ReadObject(pdf, ReadReference(pdf, "Info")).Should().Contain("/Title (Published worksheet)");
    }

    [Fact]
    public void Write_WithNoValidOutlinesAndNoSettings_PreservesTheDefaultBytes()
    {
        var pages = new[] { new PdfContentPage(100, 80, []) };
        var defaultBytes = PortablePdfWriter.WriteToBytes(new PdfContentDocument(pages));
        var invalidOutlines = new PdfDocumentProperties
        {
            Outlines =
            [
                new PdfOutlineEntry("No such page", -1),
                new PdfOutlineEntry("Also absent", 1),
                new PdfOutlineEntry("   ", 0),
            ],
        };

        ReadObject(Encoding.Latin1.GetString(defaultBytes), 1).Should().Be("<< /Type /Catalog /Pages 2 0 R >>");
        PortablePdfWriter.WriteToBytes(new PdfContentDocument(pages, invalidOutlines)).Should().Equal(defaultBytes);
    }

    private static string ReadObject(string pdf, int id)
    {
        var match = Regex.Match(pdf, $@"(?m)^{id} 0 obj\n([^\n]+)\nendobj$");
        match.Success.Should().BeTrue($"object {id} should exist");
        return match.Groups[1].Value;
    }

    private static int ReadReference(string value, string key)
    {
        var match = Regex.Match(value, $@"/{key} (\d+) 0 R\b");
        match.Success.Should().BeTrue($"the /{key} reference should exist");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string ReadTitle(string value)
    {
        var match = Regex.Match(value, @"/Title <FEFF([0-9A-F]+)>");
        match.Success.Should().BeTrue("the title should be a UTF-16BE PDF text string");
        return Encoding.BigEndianUnicode.GetString(Convert.FromHexString(match.Groups[1].Value));
    }
}
