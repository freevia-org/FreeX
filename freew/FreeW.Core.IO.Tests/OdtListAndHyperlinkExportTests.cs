using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the ODT writer's list export (text:list/text:list-item) and for the
/// hyperlink-run structural-node bug where AppendText's tab/space/newline nodes could be stranded
/// outside &lt;text:a&gt; for default-formatted hyperlink runs.
/// </summary>
public class OdtListAndHyperlinkExportTests
{
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    private static byte[] Save(TextDocument document)
    {
        using var ms = new MemoryStream();
        OdtFileAdapter.Odt().Save(document, ms);
        return ms.ToArray();
    }

    private static TextDocument Load(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return OdtFileAdapter.Odt().Load(ms);
    }

    private static XDocument ContentXml(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("content.xml")!.Open();
        return XDocument.Load(stream);
    }

    // ------------------------------------------------------------------------------------------
    // (a) HIGH — list export
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void BulletList_ExportsTextListListItemStructure_NotPlainParagraphs()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First bullet") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });
        document.Blocks.Add(new Paragraph("Second bullet") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });

        var bytes = Save(document);
        var content = ContentXml(bytes);
        var body = content.Descendants(TextNs + "list").ToList();

        body.Should().HaveCount(1, "the two bullet paragraphs should be grouped into a single text:list");
        var items = body[0].Elements(TextNs + "list-item").ToList();
        items.Should().HaveCount(2);
        items[0].Descendants(TextNs + "p").Should().Contain(p => p.Value == "First bullet");
        items[1].Descendants(TextNs + "p").Should().Contain(p => p.Value == "Second bullet");
    }

    [Fact]
    public void NumberedList_ExportsListLevelStyleNumber()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Step one") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 0 } });

        var bytes = Save(document);
        var content = ContentXml(bytes);

        var listStyle = content.Descendants(TextNs + "list-style").Single();
        listStyle.Elements(TextNs + "list-level-style-number").Should().NotBeEmpty();
        listStyle.Elements(TextNs + "list-level-style-bullet").Should().BeEmpty();
    }

    [Fact]
    public void List_RoundTrip_PreservesListKindAndLevel_ThroughFreeWsOwnReader()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Top level") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });
        document.Blocks.Add(new Paragraph("Nested") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 1 } });
        document.Blocks.Add(new Paragraph("Back to top") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });

        var reloaded = Load(Save(document));
        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();

        paragraphs.Should().HaveCount(3);
        paragraphs[0].PlainText.Should().Be("Top level");
        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraphs[0].Formatting.ListLevel.Should().Be(0);

        paragraphs[1].PlainText.Should().Be("Nested");
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraphs[1].Formatting.ListLevel.Should().Be(1);

        paragraphs[2].PlainText.Should().Be("Back to top");
        paragraphs[2].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraphs[2].Formatting.ListLevel.Should().Be(0);
    }

    [Fact]
    public void NumberedList_RoundTrip_PreservesNumberKind()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("One") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 0 } });
        document.Blocks.Add(new Paragraph("Two") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 0 } });

        var reloaded = Load(Save(document));
        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs.Should().OnlyContain(p => p.Formatting.ListKind == ListKind.Number);
    }

    [Fact]
    public void TableCell_ListParagraphs_RoundTrip_PreserveListKind()
    {
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph("Cell bullet") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });
        row.Cells.Add(cell);
        table.Rows.Add(row);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var reloaded = Load(Save(document));
        var reloadedCell = reloaded.Blocks.OfType<Table>().Single().Rows.Single().Cells.Single();
        reloadedCell.Paragraphs.Single().Formatting.ListKind.Should().Be(ListKind.Bullet);
        reloadedCell.Paragraphs.Single().PlainText.Should().Be("Cell bullet");
    }

    // ------------------------------------------------------------------------------------------
    // (a) r133 remediation — a list whose first (and only) paragraph starts at a deep level must
    //     not synthesize visible phantom bullets for the skipped intermediate levels.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Level3FirstList_DoesNotSynthesizePhantomEmptyListItems()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        // The very first (and only) list paragraph starts at level 3 (0-based) — levels 0..2 are
        // never used by any paragraph, so OpenListFrame/EnsureLastItem must pass through them
        // without materializing a real, visible, empty bullet at each one.
        document.Blocks.Add(new Paragraph("Deep item") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 3 } });

        var bytes = Save(document);
        var content = ContentXml(bytes);

        // Structural proof: every text:list-item in the document must either host a nested text:list
        // (a pass-through container for a skipped intermediate level) or a real text:p/text:h with
        // actual paragraph content — never a bare empty text:p (the phantom-bullet bug).
        var items = content.Descendants(TextNs + "list-item").ToList();
        items.Should().HaveCount(4, "levels 0..2 each synthesize one pass-through item, plus the real level-3 item");
        foreach (var item in items)
        {
            var paragraphs = item.Elements(TextNs + "p").Concat(item.Elements(TextNs + "h")).ToList();
            var nestedLists = item.Elements(TextNs + "list").ToList();
            if (paragraphs.Count > 0)
            {
                // A visible item: must carry real text, never an empty phantom bullet.
                nestedLists.Should().BeEmpty("a visible item hosts either text or a nested list, not both");
                paragraphs.Should().Contain(p => p.Value == "Deep item");
            }
            else
            {
                // A pass-through container for a skipped intermediate level: no paragraph at all,
                // just the nested list — this is what makes it render with no bullet/number.
                nestedLists.Should().HaveCount(1);
            }
        }

        // Round-trip proof: reading back must yield exactly the one real paragraph, not phantom empties.
        var reloaded = Load(bytes);
        var reloadedParagraphs = reloaded.Blocks.OfType<Paragraph>().ToList();
        reloadedParagraphs.Should().HaveCount(1, "no phantom empty paragraphs should appear for the skipped intermediate levels");
        reloadedParagraphs[0].PlainText.Should().Be("Deep item");
        reloadedParagraphs[0].Formatting.ListKind.Should().Be(ListKind.Bullet);
        reloadedParagraphs[0].Formatting.ListLevel.Should().Be(3);
    }

    // ------------------------------------------------------------------------------------------
    // (b) r133 remediation — MultiLevel (outline/legal) lists must round-trip as MultiLevel, not
    //     collapse into plain Number lists.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void MultiLevelList_RoundTrip_PreservesMultiLevelKind()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Section 1") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 0 } });
        document.Blocks.Add(new Paragraph("Section 1.1") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 1 } });

        var bytes = Save(document);
        var content = ContentXml(bytes);

        // Structural proof: the MultiLevel style must use text:display-levels > 1 to accumulate
        // ancestor counters (the ODF idiom for outline/legal numbering), distinguishing it from a
        // plain Number list style.
        var listStyle = content.Descendants(TextNs + "list-style").Single();
        listStyle.Elements(TextNs + "list-level-style-number")
            .Should().Contain(lvl => (int?)lvl.Attribute(TextNs + "display-levels") > 1);

        var reloaded = Load(bytes);
        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.MultiLevel, "MultiLevel must not collapse into plain Number on round-trip");
        paragraphs[0].Formatting.ListLevel.Should().Be(0);
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraphs[1].Formatting.ListLevel.Should().Be(1);
    }

    [Fact]
    public void NumberedList_And_MultiLevelList_UseDistinctListStyles()
    {
        // A plain Number list must keep its non-accumulating style even when a MultiLevel list is
        // also present in the document, proving the two kinds are not folded onto one shared style.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Plain number") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 0 } });
        document.Blocks.Add(new Paragraph("Outline number") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 0 } });

        var bytes = Save(document);
        var content = ContentXml(bytes);

        var listStyles = content.Descendants(TextNs + "list-style").ToList();
        listStyles.Should().HaveCount(2, "Number and MultiLevel must not share a list style");

        var reloaded = Load(bytes);
        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.Number);
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.MultiLevel);
    }

    // ------------------------------------------------------------------------------------------
    // (c) r133 remediation — the two sibling writer paths (footnote/endnote body, comment body)
    //     must route through the same list-grouping helper as body blocks and table cells.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void FootnoteBody_ListParagraphs_ExportAsTextListAndRoundTrip()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(p);

        var footnote = new Footnote(1);
        footnote.Content.Add(new Paragraph("Footnote bullet one") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });
        footnote.Content.Add(new Paragraph("Footnote bullet two") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });
        document.Footnotes[1] = footnote;

        var bytes = Save(document);
        var content = ContentXml(bytes);

        var noteBody = content.Descendants(TextNs + "note-body").Single();
        var lists = noteBody.Elements(TextNs + "list").ToList();
        lists.Should().HaveCount(1, "the two footnote-body bullet paragraphs must be grouped into a single text:list");
        lists[0].Elements(TextNs + "list-item").Should().HaveCount(2);

        var reloaded = Load(bytes);
        var reloadedFootnote = reloaded.Footnotes[1];
        reloadedFootnote.Content.Should().HaveCount(2);
        reloadedFootnote.Content.Should().OnlyContain(para => para.Formatting.ListKind == ListKind.Bullet);
        reloadedFootnote.Content[0].PlainText.Should().Be("Footnote bullet one");
        reloadedFootnote.Content[1].PlainText.Should().Be("Footnote bullet two");
    }

    [Fact]
    public void CommentBody_ListParagraphs_ExportAsTextListAndRoundTrip()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.CommentReference(1));
        document.Blocks.Add(p);

        var comment = new Comment(1) { Author = "Reviewer" };
        comment.Content.Add(new Paragraph("Comment bullet one") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });
        comment.Content.Add(new Paragraph("Comment bullet two") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });
        document.Comments[1] = comment;

        var bytes = Save(document);
        var content = ContentXml(bytes);

        var annotation = content.Descendants(XName.Get("annotation", "urn:oasis:names:tc:opendocument:xmlns:office:1.0")).Single();
        var lists = annotation.Elements(TextNs + "list").ToList();
        lists.Should().HaveCount(1, "the two comment-body bullet paragraphs must be grouped into a single text:list");
        lists[0].Elements(TextNs + "list-item").Should().HaveCount(2);

        var reloaded = Load(bytes);
        // Comment ids are reallocated on read (NextCommentId starts fresh from the loaded document),
        // so fetch the single reloaded comment by content rather than assuming id 1 survives.
        var reloadedComment = reloaded.Comments.Values.Single();
        reloadedComment.Content.Should().HaveCount(2);
        reloadedComment.Content.Should().OnlyContain(para => para.Formatting.ListKind == ListKind.Bullet);
        reloadedComment.Content[0].PlainText.Should().Be("Comment bullet one");
        reloadedComment.Content[1].PlainText.Should().Be("Comment bullet two");
    }

    // ------------------------------------------------------------------------------------------
    // Sibling no-regression: non-list paragraphs must NOT be wrapped in text:list/list-item.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void PlainParagraphs_AreNotWrappedInListStructures()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Not a list item"));
        document.Blocks.Add(new Paragraph("Also not a list item"));

        var bytes = Save(document);
        var content = ContentXml(bytes);

        content.Descendants(TextNs + "list").Should().BeEmpty();
        content.Descendants(TextNs + "list-item").Should().BeEmpty();

        var reloaded = Load(bytes);
        reloaded.Blocks.OfType<Paragraph>().Should().OnlyContain(p => p.Formatting.ListKind == ListKind.None);
    }

    [Fact]
    public void ListThenPlainParagraph_OnlyListParagraphIsWrapped()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("A bullet") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } });
        document.Blocks.Add(new Paragraph("Back to normal"));

        var reloaded = Load(Save(document));
        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.None);
        paragraphs[1].PlainText.Should().Be("Back to normal");
    }

    // ------------------------------------------------------------------------------------------
    // (b) MED — hyperlink run with tab/newline/2+ spaces must not strand structural nodes
    //     outside <text:a>, which duplicates content on the next read.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void DefaultFormattedHyperlink_WithTab_DoesNotDuplicateTextOnReload()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Before\tAfter") { HyperlinkUrl = "https://example.com" });
        document.Blocks.Add(p);

        var bytes = Save(document);

        // Structural proof: the text:tab must live INSIDE the text:a, not as a sibling after it.
        var content = ContentXml(bytes);
        var paragraphEl = content.Descendants(TextNs + "p").Single();
        var anchor = paragraphEl.Element(TextNs + "a");
        anchor.Should().NotBeNull();
        anchor!.Descendants(TextNs + "tab").Should().HaveCount(1);
        // No text:tab should be a direct/sibling child of the paragraph outside the anchor.
        paragraphEl.Elements(TextNs + "tab").Should().BeEmpty();

        var reloaded = Load(bytes);
        var runs = reloaded.Blocks.OfType<Paragraph>().Single().Runs;
        var combinedText = string.Concat(runs.Select(r => r.Text));
        combinedText.Should().Be("Before\tAfter", "the tab must not be duplicated by orphaned structural nodes");
        runs.Should().OnlyContain(r => r.HyperlinkUrl == "https://example.com");
    }

    [Fact]
    public void DefaultFormattedHyperlink_WithMultipleSpaces_DoesNotDuplicateTextOnReload()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Left   Right") { HyperlinkUrl = "https://example.com" }); // 3 spaces
        document.Blocks.Add(p);

        var bytes = Save(document);
        var reloaded = Load(bytes);
        var runs = reloaded.Blocks.OfType<Paragraph>().Single().Runs;
        var combinedText = string.Concat(runs.Select(r => r.Text));
        combinedText.Should().Be("Left   Right");
    }

    [Fact]
    public void DefaultFormattedHyperlink_WithNewline_DoesNotDuplicateTextOnReload()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Line1\nLine2") { HyperlinkUrl = "https://example.com" });
        document.Blocks.Add(p);

        var bytes = Save(document);
        var reloaded = Load(bytes);
        var runs = reloaded.Blocks.OfType<Paragraph>().Single().Runs;
        var combinedText = string.Concat(runs.Select(r => r.Text));
        combinedText.Should().Be("Line1\nLine2");
    }

    // Sibling no-regression: a plain (no tab/newline/multi-space) hyperlink run must still round-trip.
    [Fact]
    public void DefaultFormattedHyperlink_SimpleText_StillRoundTrips()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("simple") { HyperlinkUrl = "https://example.com" });
        document.Blocks.Add(p);

        var reloaded = Load(Save(document));
        var run = reloaded.Blocks.OfType<Paragraph>().Single().Runs.Single();
        run.Text.Should().Be("simple");
        run.HyperlinkUrl.Should().Be("https://example.com");
    }

    // Sibling no-regression: a STYLED (non-default) hyperlink run with a tab already worked via the
    // spanStyle branch (span.Add(textHolder) never appended raw text to `parent`) — must keep working.
    [Fact]
    public void StyledHyperlink_WithTab_StillRoundTripsCorrectly()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Bold\tLink", new RunFormatting { Bold = true }) { HyperlinkUrl = "https://example.com" });
        document.Blocks.Add(p);

        var bytes = Save(document);
        var reloaded = Load(bytes);
        var runs = reloaded.Blocks.OfType<Paragraph>().Single().Runs;
        var combinedText = string.Concat(runs.Select(r => r.Text));
        combinedText.Should().Be("Bold\tLink");
        runs.Should().OnlyContain(r => r.Formatting.Bold && r.HyperlinkUrl == "https://example.com");
    }
}
