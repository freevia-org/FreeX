namespace FreeW.Core.Model.Tests;

/// <summary>
/// Tests for the canonical bookmark-to-page walk shared by ComplexFieldEngine (PAGEREF),
/// CrossReferences ("As Page Number"), and DocumentIndex (INDEX \r). ComplexFieldEngineTests,
/// CrossReferencesTests, and DocumentIndexTests each still cover the walk through their own public entry
/// point; this file covers <see cref="BookmarkPageResolution"/> itself, plus a test that exercises all
/// three consumers against one document and asserts they agree.
/// </summary>
public sealed class BookmarkPageResolutionTests
{
    // Builds a 3-row, 1-column table whose row 0 carries bookmark "rowZero" and row 2 carries bookmark
    // "rowTwo", with an authored page-break-before on the row at pageBreakBeforeRowIndex (-1 for none).
    // Mirrors the fixture ComplexFieldEngineTests and CrossReferencesTests each independently built for
    // the same scenario.
    private static Table ThreeRowBookmarkedTable(int pageBreakBeforeRowIndex)
    {
        var table = new Table();
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Cell " + rowIndex));
            if (rowIndex == 0)
                paragraph.BookmarkNames.Add("rowZero");
            else if (rowIndex == 2)
                paragraph.BookmarkNames.Add("rowTwo");
            if (rowIndex == pageBreakBeforeRowIndex)
                paragraph.Formatting = ParagraphFormatting.Default with { PageBreakBefore = true };

            var cell = new TableCell();
            cell.Paragraphs.Add(paragraph);
            var row = new TableRow();
            row.Cells.Add(cell);
            table.Rows.Add(row);
        }

        return table;
    }

    [Fact]
    public void Find_LocatesBookmarkOnTableRow_RowAware()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(ThreeRowBookmarkedTable(pageBreakBeforeRowIndex: -1));

        var target = BookmarkPageResolution.Find(doc, "rowTwo");

        target.Should().NotBeNull();
        target!.Value.BlockIndex.Should().Be(0);
        target.Value.TableRowIndex.Should().Be(2);
        target.Value.StoryKind.Should().Be(DocumentFieldStoryKind.MainDocument);
        target.Value.Paragraph.PlainText.Should().Be("Cell 2");
    }

    [Fact]
    public void Find_ReturnsNull_WhenBookmarkDoesNotExistAnywhere()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Nothing bookmarked here"));

        BookmarkPageResolution.Find(doc, "Missing").Should().BeNull();
    }

    [Fact]
    public void Find_LocatesBookmarkInHeaderOrFooter()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        var headerParagraph = new Paragraph("header text") { BookmarkName = "HeaderMark" };
        doc.Header = new HeaderFooter { Paragraphs = { headerParagraph } };

        var target = BookmarkPageResolution.Find(doc, "HeaderMark");

        target.Should().NotBeNull();
        target!.Value.BlockIndex.Should().Be(-1);
        target.Value.TableRowIndex.Should().BeNull();
        target.Value.StoryKind.Should().Be(DocumentFieldStoryKind.HeaderFooter);
    }

    [Fact]
    public void Find_LocatesBookmarkInFootnote()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        var notePara = new Paragraph("note text") { BookmarkName = "NoteMark" };
        var footnote = new Footnote(1);
        footnote.Content.Add(notePara);
        doc.Footnotes[1] = footnote;

        var target = BookmarkPageResolution.Find(doc, "NoteMark");

        target.Should().NotBeNull();
        target!.Value.BlockIndex.Should().Be(-1);
        target.Value.StoryKind.Should().Be(DocumentFieldStoryKind.Footnote);
    }

    [Fact]
    public void Find_LocatesBookmarkInEndnote()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        var notePara = new Paragraph("note text") { BookmarkName = "EndMark" };
        var endnote = new Endnote(1);
        endnote.Content.Add(notePara);
        doc.Endnotes[1] = endnote;

        var target = BookmarkPageResolution.Find(doc, "EndMark");

        target.Should().NotBeNull();
        target!.Value.BlockIndex.Should().Be(-1);
        target.Value.StoryKind.Should().Be(DocumentFieldStoryKind.Endnote);
    }

    [Fact]
    public void Find_LocatesBookmarkInTextBox_UsingItsAnchorParagraphsBlockIndex()
    {
        var doc = new TextDocument();
        var anchor = new Paragraph("anchor");
        var textBoxParagraph = new Paragraph("box text") { BookmarkName = "BoxMark" };
        anchor.Runs.Add(new Run(string.Empty) { Shape = new Shape { TextParagraphs = { textBoxParagraph } } });
        doc.Blocks.Add(anchor);

        var target = BookmarkPageResolution.Find(doc, "BoxMark");

        target.Should().NotBeNull();
        target!.Value.BlockIndex.Should().Be(0);
        target.Value.StoryKind.Should().Be(DocumentFieldStoryKind.TextBox);
        // Sibling no-regression: a text box anchored outside any table has no row to carry.
        target.Value.TableRowIndex.Should().BeNull();
    }

    // THE FAILING-BEFORE PROOF: a text box anchored to a paragraph inside a table row must resolve with
    // that row's index, not null, so ResolvePageText's row-offset math actually runs for it. Before the
    // fix, DocumentFieldStories.Enumerate never carried per-row addressing at all, so this always came
    // back null even though BlockIndex correctly pointed at the table.
    [Fact]
    public void Find_LocatesBookmarkInTextBoxAnchoredInsideTableRow_RowAware()
    {
        var doc = new TextDocument();
        var table = new Table();
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Cell " + rowIndex));
            if (rowIndex == 2)
            {
                // The authored page break sits on the table row itself, past which the text box's own
                // bookmark (not "rowTwo" -- inside the text box's nested paragraph) must resolve.
                paragraph.Formatting = ParagraphFormatting.Default with { PageBreakBefore = true };
                var textBoxParagraph = new Paragraph("box text") { BookmarkName = "BoxMark" };
                paragraph.Runs.Add(new Run(string.Empty)
                {
                    Shape = new Shape { TextParagraphs = { textBoxParagraph } },
                });
            }

            var cell = new TableCell();
            cell.Paragraphs.Add(paragraph);
            var row = new TableRow();
            row.Cells.Add(cell);
            table.Rows.Add(row);
        }
        doc.Blocks.Add(table);

        var target = BookmarkPageResolution.Find(doc, "BoxMark");

        target.Should().NotBeNull();
        target!.Value.BlockIndex.Should().Be(0);
        target.Value.TableRowIndex.Should().Be(2);
        target.Value.StoryKind.Should().Be(DocumentFieldStoryKind.TextBox);

        var pageText = BookmarkPageResolution.ResolvePageText(
            doc, target.Value, pageOf: blockIndex => blockIndex == 0 ? 3 : null, pageTextOf: null);

        pageText.Should().Be("4");
    }

    // THE FAILING-BEFORE PROOF: a page-break-before authored on a table's own row 0 must not count toward
    // a later row's offset. PageBreaksBeforeTableRow's guard already refuses to apply any offset to row 0
    // itself (its own break is presumed already reflected in the host's page answer for the table's
    // block), so counting that same break again while walking up to a later row double-counts it. The two
    // hand-written copies this method replaces got this wrong: their loop started at row 0, so a break
    // authored there silently leaked into every row after it even though row 0's own answer never saw it.
    [Fact]
    public void PageBreaksBeforeTableRow_BreakAuthoredOnRowZero_DoesNotLeakIntoLaterRowsOffset()
    {
        var table = ThreeRowBookmarkedTable(pageBreakBeforeRowIndex: 0);

        BookmarkPageResolution.PageBreaksBeforeTableRow(table, rowIndex: 2).Should().Be(0);
    }

    // Sibling no-regression: row 0's own guarded answer is unaffected by the fix either way -- it was
    // always zero, break or no break, before and after.
    [Fact]
    public void PageBreaksBeforeTableRow_RowZero_AlwaysZero_RegardlessOfItsOwnBreak()
    {
        var table = ThreeRowBookmarkedTable(pageBreakBeforeRowIndex: 0);

        BookmarkPageResolution.PageBreaksBeforeTableRow(table, rowIndex: 0).Should().Be(0);
    }

    // Sibling no-regression: a break authored on an intermediate (non-zero) row must still be counted for
    // a later row -- only row 0's own break gets the special exclusion.
    [Fact]
    public void PageBreaksBeforeTableRow_BreakOnLaterRow_IsStillCounted()
    {
        var table = ThreeRowBookmarkedTable(pageBreakBeforeRowIndex: 2);

        BookmarkPageResolution.PageBreaksBeforeTableRow(table, rowIndex: 2).Should().Be(1);
    }

    // THE THREE-PATHS-AGREE PROOF: PAGEREF (ComplexFieldEngine), "As Page Number" (CrossReferences), and
    // INDEX \r (DocumentIndex) each resolve the same bookmark, on the same table row, past the same
    // authored page break, through the same host-observed page for the table's own block -- and must land
    // on the identical page.
    [Fact]
    public void AllThreeConsumers_AgreeOnTheSameTableRowBookmarksPage()
    {
        int? PageOf(int blockIndex) => blockIndex == 0 ? 3 : null;

        // ComplexFieldEngine: PAGEREF rowTwo.
        var fieldDoc = new TextDocument();
        fieldDoc.Blocks.Add(ThreeRowBookmarkedTable(pageBreakBeforeRowIndex: 2));
        var fieldParagraph = new Paragraph();
        fieldParagraph.Runs.Add(Run.ComplexFieldRun(" PAGEREF rowTwo \\h ", "stale"));
        fieldDoc.Blocks.Add(fieldParagraph);
        var pageRefResult = ComplexFieldEngine.Recompute(fieldDoc, 1, 0, pageOf: PageOf);

        // CrossReferences: "As Page Number" cross-reference to rowTwo.
        var crossRefDoc = new TextDocument();
        crossRefDoc.Blocks.Add(ThreeRowBookmarkedTable(pageBreakBeforeRowIndex: 2));
        var crossRefField = new CrossReferenceField(
            CrossRefFieldKind.PageRef, "rowTwo", CrossRefInsertAs.PageNumber, Hyperlink: false);
        var crossRefResult = CrossReferences.ResolveField(
            crossRefDoc, crossRefField, "stale", sourceBlockIndex: 1, pageOf: PageOf);

        // DocumentIndex: INDEX \r rowTwo, via the production pageReferenceOf shape (a physical page index
        // plus a display label per block, exactly as DocumentReferenceEditingCoordinator.InsertIndex wires
        // it -- not the simpler pageTextOf override, which deliberately never receives the row offset).
        var indexDoc = new TextDocument();
        indexDoc.Blocks.Add(ThreeRowBookmarkedTable(pageBreakBeforeRowIndex: 2));
        indexDoc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "rowTwo")) }
        });
        var indexEntry = DocumentIndex.Build(
                indexDoc,
                pageReferenceOf: blockIndex => blockIndex == 0 ? new IndexPageReferenceAddress(2, "3") : null)
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        pageRefResult.Should().Be("4");
        crossRefResult.Should().Be("4");
        indexEntry.PlainText.Should().Be("Alpha, 4");
    }
}
