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
}
