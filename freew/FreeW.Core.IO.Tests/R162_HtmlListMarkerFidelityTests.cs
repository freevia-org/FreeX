using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// meta F3 (round 162): a captured bullet glyph or number-format marker (round 161's
/// <see cref="ParagraphFormatting.ListMarkerText"/>/<see cref="ParagraphFormatting.ListNumberFormat"/>,
/// already round-tripped by DocxReader/DocxWriter) was silently discarded by <see cref="HtmlFileAdapter"/> --
/// it only read/wrote <see cref="ListKind"/>, never the actual glyph or list-style-type/numFmt. These tests
/// load FOREIGN HTML (a square bullet, a lower-roman numbered list -- content FreeW itself never emits,
/// since our own writer only ever produced the default markers before this fix) and confirm the marker
/// survives both a read and a full save/reload round trip.
/// </summary>
public class R162_HtmlListMarkerFidelityTests
{
    private const string ForeignHtml =
        "<html><body>" +
        "<ul style=\"list-style-type: square\"><li>Alpha</li><li>Beta</li></ul>" +
        "<ol style=\"list-style-type: lower-roman\"><li>One</li><li>Two</li></ol>" +
        "</body></html>";

    [Fact]
    public void Load_CapturesForeignSquareBulletGlyph()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ForeignHtml));
        var document = new HtmlFileAdapter().Load(stream);

        var bulletItems = document.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Bullet)
            .ToList();

        bulletItems.Should().HaveCount(2);
        bulletItems.Should().OnlyContain(p => p.Formatting.ListMarkerText == "▪",
            because: "a CSS 'square' bullet is a different glyph than FreeW's default round '•' and must not be silently normalized to it");
    }

    [Fact]
    public void Load_CapturesForeignLowerRomanNumberFormat()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ForeignHtml));
        var document = new HtmlFileAdapter().Load(stream);

        var numberItems = document.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Number)
            .ToList();

        numberItems.Should().HaveCount(2);
        numberItems.Should().OnlyContain(p => p.Formatting.ListNumberFormat == ListNumberFormat.LowerRoman,
            because: "a CSS 'lower-roman' numbered list is not FreeW's decimal default and must not be silently normalized to it");
    }

    [Fact]
    public void SaveThenReload_RoundTripsBothTheGlyphAndTheNumberFormat()
    {
        using var loadStream = new MemoryStream(Encoding.UTF8.GetBytes(ForeignHtml));
        var loaded = new HtmlFileAdapter().Load(loadStream);

        using var saveStream = new MemoryStream();
        new HtmlFileAdapter().Save(loaded, saveStream);
        saveStream.Position = 0;
        var reloaded = new HtmlFileAdapter().Load(saveStream);

        reloaded.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Bullet)
            .Should().OnlyContain(p => p.Formatting.ListMarkerText == "▪");

        reloaded.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Number)
            .Should().OnlyContain(p => p.Formatting.ListNumberFormat == ListNumberFormat.LowerRoman);
    }

    /// <summary>
    /// Sibling no-regression: a plain, default bullet/number list (FreeW's own shape, and the overwhelming
    /// majority of real-world HTML) must keep round-tripping to null/Decimal exactly as it did before this
    /// fix -- no explicit-but-identical "list-style-type: disc" / "list-style-type: decimal" override, and
    /// no style attribute at all on the emitted &lt;ul&gt;/&lt;ol&gt;, so existing Filtered-output consumers
    /// see byte-identical markup for the common case.
    /// </summary>
    [Fact]
    public void DefaultBulletAndNumberLists_StillRoundTripToNullMarkerAndDecimalFormat()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Alpha") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet } });
        document.Blocks.Add(new Paragraph("One") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });

        using var saveStream = new MemoryStream();
        new HtmlFileAdapter().Save(document, saveStream);
        var html = Encoding.UTF8.GetString(saveStream.ToArray());

        html.Should().NotContain("list-style-type",
            because: "the default marker must not be emitted as an explicit-but-identical CSS override");

        saveStream.Position = 0;
        var reloaded = new HtmlFileAdapter().Load(saveStream);

        reloaded.Blocks.OfType<Paragraph>().First(p => p.Formatting.ListKind == ListKind.Bullet)
            .Formatting.ListMarkerText.Should().BeNull();
        reloaded.Blocks.OfType<Paragraph>().First(p => p.Formatting.ListKind == ListKind.Number)
            .Formatting.ListNumberFormat.Should().Be(ListNumberFormat.Decimal);
    }

    /// <summary>
    /// Round 162 remediation (U1): WriteBlocks grouped consecutive list paragraphs into one &lt;ul&gt;/&lt;ol&gt;
    /// keyed on <see cref="ListKind"/> ALONE, so two ADJACENT bullet (or numbered) lists with DIFFERENT
    /// captured markers merged into a single tag and the second list silently inherited the first list's
    /// marker on reload. This loads a document with two adjacent bullet paragraph runs -- square markers,
    /// then hollow-circle markers, with no plain paragraph or other block separating them -- and a save/reload
    /// round trip must keep both their own marker instead of the second one inheriting the first's.
    /// Mirrors the (ListKind, ListMarkerText, ListNumberFormat) marker-group key DocxWriter already uses
    /// (BuildRestartOverrides / RestartNumbering.MarkerGroups).
    /// </summary>
    [Fact]
    public void SaveThenReload_AdjacentBulletListsWithDifferentMarkers_DoNotMergeOrInheritEachOthersMarker()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Alpha") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListMarkerText = "▪" } });
        document.Blocks.Add(new Paragraph("Beta") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListMarkerText = "▪" } });
        document.Blocks.Add(new Paragraph("Gamma") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListMarkerText = "o" } });
        document.Blocks.Add(new Paragraph("Delta") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListMarkerText = "o" } });

        using var saveStream = new MemoryStream();
        new HtmlFileAdapter().Save(document, saveStream);
        saveStream.Position = 0;
        var html = Encoding.UTF8.GetString(saveStream.ToArray());

        // Two separate <ul> tags, not one merged tag holding all four items.
        html.Should().Contain("</ul>\r\n<ul", because: "adjacent same-kind lists with different markers must stay in separate <ul> tags");

        saveStream.Position = 0;
        var reloaded = new HtmlFileAdapter().Load(saveStream);
        var items = reloaded.Blocks.OfType<Paragraph>().Where(p => p.Formatting.ListKind == ListKind.Bullet).ToList();

        items.Should().HaveCount(4);
        items[0].Formatting.ListMarkerText.Should().Be("▪", because: "the first run's own square marker must survive");
        items[1].Formatting.ListMarkerText.Should().Be("▪");
        items[2].Formatting.ListMarkerText.Should().Be("o", because: "the second run must keep its own hollow-circle marker, not inherit the first run's square marker");
        items[3].Formatting.ListMarkerText.Should().Be("o");
    }

    /// <summary>
    /// Sibling no-regression: a single uniform-marker list (the common case, and what Wave A's own tests
    /// already covered) must still be written and read back as ONE &lt;ul&gt; tag -- proving the new grouping
    /// key does not fragment a list that never changes marker.
    /// </summary>
    [Fact]
    public void SaveThenReload_SingleUniformMarkerList_StaysOneTag()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Alpha") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListMarkerText = "▪" } });
        document.Blocks.Add(new Paragraph("Beta") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListMarkerText = "▪" } });
        document.Blocks.Add(new Paragraph("Gamma") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListMarkerText = "▪" } });

        using var saveStream = new MemoryStream();
        new HtmlFileAdapter().Save(document, saveStream);
        saveStream.Position = 0;
        var html = Encoding.UTF8.GetString(saveStream.ToArray());

        html.Should().Contain("<ul style=\"list-style-type: square\">");
        // Only one <ul> opening tag for the whole uniform-marker run.
        (html.Length - html.Replace("<ul ", "").Length).Should().Be("<ul ".Length,
            because: "a run of paragraphs that never changes marker must stay a single <ul>, not fragment per item");

        saveStream.Position = 0;
        var reloaded = new HtmlFileAdapter().Load(saveStream);
        reloaded.Blocks.OfType<Paragraph>().Where(p => p.Formatting.ListKind == ListKind.Bullet)
            .Should().OnlyContain(p => p.Formatting.ListMarkerText == "▪");
    }
}
