using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Tests for the two HTML save variants: "Web Page, Filtered" (clean HTML5, the default) and
/// "Web Page" / Full (HTML with Office round-trip scaffolding — xmlns attrs, Generator meta, mso-style
/// CSS classes). Mirrors the distinction Word offers under File &gt; Save As &gt; Web Page flavours.
/// </summary>
public class HtmlVariantTests
{
    // -------------------------------------------------------------------------
    // 1. Filtered output must NOT contain Office markers
    // -------------------------------------------------------------------------

    [Fact]
    public void Filtered_DoesNotContainOfficeNamespaceDeclarations()
    {
        var html = SaveToString(HtmlFileAdapter.Filtered(), BuildRoundTripDocument());
        html.Should().NotContain("xmlns:o");
        html.Should().NotContain("xmlns:w");
    }

    [Fact]
    public void Filtered_DoesNotContainMsoStyleAnnotations()
    {
        var html = SaveToString(HtmlFileAdapter.Filtered(), BuildRoundTripDocument());
        html.Should().NotContain("mso-");
    }

    [Fact]
    public void Filtered_DoesNotContainGeneratorMeta()
    {
        var html = SaveToString(HtmlFileAdapter.Filtered(), BuildRoundTripDocument());
        html.Should().NotContain("Generator");
        html.Should().NotContain("FreeW", because: "the Generator meta is the only place FreeW appears in Filtered output");
    }

    // -------------------------------------------------------------------------
    // 2. Full (Web Page) output DOES contain Office markers
    // -------------------------------------------------------------------------

    [Fact]
    public void Full_ContainsOfficeNamespaceDeclarations()
    {
        var html = SaveToString(HtmlFileAdapter.WebPage(), BuildRoundTripDocument());
        html.Should().Contain("xmlns:o");
        html.Should().Contain("xmlns:w");
    }

    [Fact]
    public void Full_ContainsMsoStyleAnnotation()
    {
        var html = SaveToString(HtmlFileAdapter.WebPage(), BuildRoundTripDocument());
        html.Should().Contain("mso-style-name");
    }

    [Fact]
    public void Full_ContainsGeneratorMeta()
    {
        var html = SaveToString(HtmlFileAdapter.WebPage(), BuildRoundTripDocument());
        html.Should().Contain("Generator");
        html.Should().Contain("FreeW");
    }

    // -------------------------------------------------------------------------
    // 3. Both variants round-trip the intersection through the existing reader
    // -------------------------------------------------------------------------

    [Fact]
    public void Filtered_RoundTripsIntersection()
    {
        var original = BuildRoundTripDocument();
        var loaded = RoundTrip(HtmlFileAdapter.Filtered(), original);
        AssertIntersectionRoundTrip(loaded);
    }

    [Fact]
    public void Full_RoundTripsIntersection()
    {
        var original = BuildRoundTripDocument();
        var loaded = RoundTrip(HtmlFileAdapter.WebPage(), original);
        AssertIntersectionRoundTrip(loaded);
    }

    // -------------------------------------------------------------------------
    // 4. Full re-open recovers a heading paragraph's StyleId via mso-style class
    // -------------------------------------------------------------------------

    [Fact]
    public void Full_ReOpenRecoversParagraphStyleId()
    {
        // Build a document with a custom (non-heading) StyleId, so it is not emitted
        // as <h1> etc. but instead as a <p class="FreeW-CustomStyle">.
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Custom Styled") { StyleId = "CustomStyle" });
        document.Blocks.Add(new Paragraph("Normal paragraph"));

        var loaded = RoundTrip(HtmlFileAdapter.WebPage(), document);

        // The StyleId must survive the save → load cycle via the mso-style-name class.
        loaded.Blocks.Should().HaveCount(2);
        loaded.Blocks[0].Should().BeOfType<Paragraph>().Which.StyleId.Should().Be("CustomStyle");
        loaded.Blocks[1].Should().BeOfType<Paragraph>().Which.StyleId.Should().BeNull();
    }

    [Fact]
    public void Full_ReOpenRecoversHeadingStyleId()
    {
        // "Heading1" renders as <h1> so the StyleId is recovered by ReadHeading even without a class;
        // verify it survives for the standard heading ids.
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Section") { StyleId = "Heading1" });

        var loaded = RoundTrip(HtmlFileAdapter.WebPage(), document);

        loaded.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.StyleId.Should().Be("Heading1");
    }

    // -------------------------------------------------------------------------
    // 4b. Filtered does NOT recover non-standard StyleIds (no class in output)
    // -------------------------------------------------------------------------

    [Fact]
    public void Filtered_DoesNotRecoverNonHeadingStyleId()
    {
        // A custom StyleId cannot survive Filtered output (there is no mso-style class).
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Custom Styled") { StyleId = "CustomStyle" });

        var loaded = RoundTrip(HtmlFileAdapter.Filtered(), document);

        loaded.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.StyleId.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // 5. Parameterless constructor equals Filtered
    // -------------------------------------------------------------------------

    [Fact]
    public void ParameterlessCtor_ProducesSameOutputAsFiltered()
    {
        var document = BuildRoundTripDocument();
        var defaultHtml = SaveToString(new HtmlFileAdapter(), document);
        var filteredHtml = SaveToString(HtmlFileAdapter.Filtered(), document);
        defaultHtml.Should().Be(filteredHtml);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a document that exercises the modelled subset both variants must round-trip:
    /// paragraphs, a heading with StyleId, a bold run, and a hyperlink.
    /// </summary>
    private static TextDocument BuildRoundTripDocument()
    {
        var document = new TextDocument();

        // Heading with StyleId.
        document.Blocks.Add(new Paragraph("Introduction") { StyleId = "Heading1" });

        // Paragraph with bold run and hyperlink.
        var body = new Paragraph();
        body.Runs.Add(new Run("Click "));
        body.Runs.Add(new Run("here", new RunFormatting { Bold = true }) { HyperlinkUrl = "https://example.test/" });
        body.Runs.Add(new Run(" for details."));
        document.Blocks.Add(body);

        // Bullet list.
        document.Blocks.Add(new Paragraph("Alpha") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet } });
        document.Blocks.Add(new Paragraph("Beta") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet } });

        return document;
    }

    /// <summary>
    /// Asserts that the round-tripped document contains the expected structural elements: heading StyleId,
    /// bold run, and hyperlink.
    /// </summary>
    private static void AssertIntersectionRoundTrip(TextDocument loaded)
    {
        // At least 4 blocks: heading + body paragraph + 2 list items.
        loaded.Blocks.Count.Should().BeGreaterThanOrEqualTo(4);

        // First block is a heading paragraph with StyleId Heading1.
        loaded.Blocks[0].Should().BeOfType<Paragraph>()
            .Which.StyleId.Should().Be("Heading1");

        // Second block contains a bold run and a hyperlink.
        var bodyParagraph = loaded.Blocks[1].Should().BeOfType<Paragraph>().Which;
        bodyParagraph.Runs.Should().Contain(r => r.Formatting.Bold, because: "the bold run must survive");
        bodyParagraph.Runs.Should().Contain(r => r.HyperlinkUrl == "https://example.test/", because: "the link must survive");

        // Third and fourth blocks are bullet items.
        loaded.Blocks[2].Should().BeOfType<Paragraph>()
            .Which.Formatting.ListKind.Should().Be(ListKind.Bullet);
        loaded.Blocks[3].Should().BeOfType<Paragraph>()
            .Which.Formatting.ListKind.Should().Be(ListKind.Bullet);
    }

    private static string SaveToString(HtmlFileAdapter adapter, TextDocument document)
    {
        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static TextDocument RoundTrip(HtmlFileAdapter adapter, TextDocument document)
    {
        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
