using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class CommentListPlannerTests
{
    [Fact]
    public void Build_ReturnsCommentThreadsInDocumentOrderIncludingTables()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var body = new Paragraph();
        body.Runs.Add(new Run("Before "));
        body.Runs.Add(new Run("Body") { CommentId = 2 });
        body.Runs.Add(Run.CommentReference(2));
        doc.Blocks.Add(body);

        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        var tableParagraph = new Paragraph();
        tableParagraph.Runs.Add(new Run("Table") { CommentId = 7 });
        tableParagraph.Runs.Add(Run.CommentReference(7));
        cell.Paragraphs.Add(tableParagraph);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        doc.Comments[7] = new Comment(7, "Table note", "Taylor", "T") { Resolved = true };
        doc.Comments[2] = new Comment(2, "Body note", "Alex", "A");
        doc.Comments[2].AddReply(3, "Reply", "Casey", "C");

        var items = CommentListPlanner.Build(doc);

        items.Select(item => item.Id).Should().Equal(2, 7);
        items[0].Author.Should().Be("Alex");
        items[0].Text.Should().Be("Body note");
        items[0].Anchor.Offset.Should().Be(7);
        items[0].Anchor.IsTableAnchor.Should().BeFalse();
        items[0].ReplyCount.Should().Be(1);
        items[0].Resolved.Should().BeFalse();
        items[1].Author.Should().Be("Taylor");
        items[1].BlockIndex.Should().Be(1);
        items[1].Anchor.Offset.Should().Be(0);
        items[1].Anchor.TableRowIndex.Should().Be(0);
        items[1].Anchor.TableGridColumnIndex.Should().Be(0);
        items[1].Anchor.TableParagraphIndex.Should().Be(0);
        items[1].Resolved.Should().BeTrue();
    }

    [Fact]
    public void SelectAdjacent_WrapsAroundDocumentOrder()
    {
        var items = new[]
        {
            new CommentListItem(2, new CommentAnchorPosition(0, 0), "A", "First", 0, false),
            new CommentListItem(7, new CommentAnchorPosition(1, 0), "B", "Second", 0, false),
        };

        CommentListPlanner.SelectAdjacent(items, null, 1)!.Id.Should().Be(2);
        CommentListPlanner.SelectAdjacent(items, null, -1)!.Id.Should().Be(7);
        CommentListPlanner.SelectAdjacent(items, 2, 1)!.Id.Should().Be(7);
        CommentListPlanner.SelectAdjacent(items, 2, -1)!.Id.Should().Be(7);
        CommentListPlanner.SelectAdjacent(items, 7, 1)!.Id.Should().Be(2);
        CommentListPlanner.SelectAdjacent(items, 7, -1)!.Id.Should().Be(2);
    }

    /// <summary>
    /// F2: a comment anchored in a header, footer, footnote, or endnote must still show up in the
    /// Comments list (and the printed/exported markup-balloon strip, which shares this same Build call
    /// per ReviewBalloonLayoutPlanner.BuildSources) even though the body has no other comments at all.
    /// Before the fix, ParagraphsInBlock only recursed Paragraph/Table body blocks, so Build never saw
    /// any of these four anchor kinds and this comment was permanently absent everywhere in the review UI.
    /// </summary>
    [Fact]
    public void Build_IncludesCommentsAnchoredInHeaderFooterFootnoteAndEndnote()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Plain body paragraph, no comments"));

        var header = new HeaderFooter();
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(new Run("Header text") { CommentId = 10 });
        headerParagraph.Runs.Add(Run.CommentReference(10));
        header.Paragraphs.Add(headerParagraph);
        doc.FinalSectionHeadersFooters.Header = header;

        var footer = new HeaderFooter();
        var footerParagraph = new Paragraph();
        footerParagraph.Runs.Add(new Run("Footer text") { CommentId = 11 });
        footerParagraph.Runs.Add(Run.CommentReference(11));
        footer.Paragraphs.Add(footerParagraph);
        doc.FinalSectionHeadersFooters.Footer = footer;

        var footnote = new Footnote(1);
        var footnoteParagraph = new Paragraph();
        footnoteParagraph.Runs.Add(new Run("Footnote text") { CommentId = 12 });
        footnoteParagraph.Runs.Add(Run.CommentReference(12));
        footnote.Content.Add(footnoteParagraph);
        doc.Footnotes[1] = footnote;

        var endnote = new Endnote(1);
        var endnoteParagraph = new Paragraph();
        endnoteParagraph.Runs.Add(new Run("Endnote text") { CommentId = 13 });
        endnoteParagraph.Runs.Add(Run.CommentReference(13));
        endnote.Content.Add(endnoteParagraph);
        doc.Endnotes[1] = endnote;

        doc.Comments[10] = new Comment(10, "Header note", "Alex", "A");
        doc.Comments[11] = new Comment(11, "Footer note", "Alex", "A");
        doc.Comments[12] = new Comment(12, "Footnote note", "Alex", "A");
        doc.Comments[13] = new Comment(13, "Endnote note", "Alex", "A");

        var items = CommentListPlanner.Build(doc);

        items.Select(item => item.Id).Should().BeEquivalentTo([10, 11, 12, 13]);
        items.Should().OnlyContain(item => item.Anchor.IsHeaderFooterOrNoteAnchor);
        items.Should().OnlyContain(item => item.BlockIndex >= doc.Blocks.Count);
    }

    /// <summary>
    /// Sibling no-regression: Next/Previous Comment (SelectAdjacent) must keep cycling through only the
    /// body/table comments the shells can actually place a caret in -- a header/footer/footnote/endnote
    /// comment now appearing in Build's output must not get "stuck" in the cycle (the shells have no way
    /// to move the caret there yet, so if SelectAdjacent returned it, pressing Next/Previous again would
    /// recompute the identical unreachable target forever, permanently blocking navigation to every
    /// comment beyond it). It must still be excluded from the cycle, exactly as when Build never reported
    /// it at all.
    /// </summary>
    [Fact]
    public void SelectAdjacent_SkipsHeaderFooterOrNoteAnchoredComments()
    {
        var items = new[]
        {
            new CommentListItem(2, new CommentAnchorPosition(0, 0), "A", "Body one", 0, false),
            new CommentListItem(
                10,
                new CommentAnchorPosition(5, 0, IsHeaderFooterOrNoteAnchor: true),
                "B",
                "Header note",
                0,
                false),
            new CommentListItem(7, new CommentAnchorPosition(1, 0), "C", "Body two", 0, false),
        };

        CommentListPlanner.SelectAdjacent(items, null, 1)!.Id.Should().Be(2);
        CommentListPlanner.SelectAdjacent(items, null, -1)!.Id.Should().Be(7);
        CommentListPlanner.SelectAdjacent(items, 2, 1)!.Id.Should().Be(7);
        CommentListPlanner.SelectAdjacent(items, 7, 1)!.Id.Should().Be(2);
        CommentListPlanner.SelectAdjacent(items, 2, -1)!.Id.Should().Be(7);
    }
}
