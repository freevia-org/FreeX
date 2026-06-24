using System.IO;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the Header &amp; Footer Design surface (W9 task):
/// a document with distinct default/first/even header AND footer content, with
/// DifferentFirstPage + DifferentOddEvenPages on, and custom header/footer distances,
/// must survive DocxWriter→DocxReader with every slot + toggle + distance intact.
/// </summary>
public class HeaderFooterDesignRoundTripTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void AllSixSlots_WithTogglesAndDistances_SurviveRoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));

        // Enable both header/footer variant toggles.
        doc.Page.DifferentFirstPage = true;
        doc.Page.DifferentOddEvenPages = true;

        // Set custom header/footer distances.
        doc.Page.HeaderDistancePt = 27;
        doc.Page.FooterDistancePt = 45;

        // Populate all six slots with distinct text.
        doc.Header       = new HeaderFooter("Default Header");
        doc.Footer       = new HeaderFooter("Default Footer");
        doc.EvenHeader   = new HeaderFooter("Even-Page Header");
        doc.EvenFooter   = new HeaderFooter("Even-Page Footer");
        doc.FirstHeader  = new HeaderFooter("First-Page Header");
        doc.FirstFooter  = new HeaderFooter("First-Page Footer");

        var read = RoundTrip(doc);

        // Toggles survive.
        read.Page.DifferentFirstPage.Should().BeTrue("DifferentFirstPage must round-trip");
        read.Page.DifferentOddEvenPages.Should().BeTrue("DifferentOddEvenPages must round-trip");

        // Distances survive.
        read.Page.HeaderDistancePt.Should().Be(27, "HeaderDistancePt must round-trip");
        read.Page.FooterDistancePt.Should().Be(45, "FooterDistancePt must round-trip");

        // All six H&F slots survive with correct text.
        read.Header!.PlainText.Should().Be("Default Header",     "default header slot must round-trip");
        read.Footer!.PlainText.Should().Be("Default Footer",     "default footer slot must round-trip");
        read.EvenHeader!.PlainText.Should().Be("Even-Page Header", "even header slot must round-trip");
        read.EvenFooter!.PlainText.Should().Be("Even-Page Footer", "even footer slot must round-trip");
        read.FirstHeader!.PlainText.Should().Be("First-Page Header", "first header slot must round-trip");
        read.FirstFooter!.PlainText.Should().Be("First-Page Footer", "first footer slot must round-trip");
    }

    [Fact]
    public void DefaultSlots_NoToggles_SurviveRoundTrip()
    {
        // Simplest case: just default header + footer, no variant toggles.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Header = new HeaderFooter("My Header");
        doc.Footer = new HeaderFooter("My Footer");

        var read = RoundTrip(doc);

        read.Header!.PlainText.Should().Be("My Header", "default header must survive minimal round-trip");
        read.Footer!.PlainText.Should().Be("My Footer", "default footer must survive minimal round-trip");
        read.Page.DifferentFirstPage.Should().BeFalse("no DifferentFirstPage was set");
        read.Page.DifferentOddEvenPages.Should().BeFalse("no DifferentOddEvenPages was set");
    }

    [Fact]
    public void DifferentFirstPageToggle_WithPageNumberInDefaultHeader_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Page body"));
        doc.Page.DifferentFirstPage = true;

        // Default header carries a page-number field.
        var defaultHeader = new HeaderFooter();
        var para = new Paragraph();
        para.Runs.Add(Run.PageNumberField());
        defaultHeader.Paragraphs.Add(para);
        doc.Header = defaultHeader;

        // First-page header is deliberately empty (common Word pattern: blank cover page header).
        doc.FirstHeader = null;

        var read = RoundTrip(doc);

        read.Page.DifferentFirstPage.Should().BeTrue("DifferentFirstPage must persist");
        read.Header.Should().NotBeNull("default header with page-number field must survive");
        var pageNumberRuns = read.Header!.Paragraphs
            .SelectMany(p => p.Runs)
            .Where(r => r.FieldKind == RunFieldKind.PageNumber)
            .ToList();
        pageNumberRuns.Should().HaveCount(1, "the page-number field run must round-trip");
    }

    [Fact]
    public void HeaderDistancePt_ZeroDoesNotWriteMeasure()
    {
        // Explicit zero for both distances: the writer should NOT emit a w:header / w:footer measure
        // (or emit it as zero) and the reader must map it back to 0.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.HeaderDistancePt = 0;
        doc.Page.FooterDistancePt = 0;

        var read = RoundTrip(doc);
        read.Page.HeaderDistancePt.Should().Be(0, "explicit zero distance must survive");
        read.Page.FooterDistancePt.Should().Be(0, "explicit zero distance must survive");
    }
}
