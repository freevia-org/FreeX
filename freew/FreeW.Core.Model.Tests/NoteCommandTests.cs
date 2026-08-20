namespace FreeW.Core.Model.Tests;

public sealed class NoteCommandTests
{
    [Fact]
    public void InsertTableCellNote_InsertsAtTextOffset_AndUndoRedoRestoresOneEdit()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph("before after"));
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new InsertTableCellNoteCommand(1, true, "note", 0, 0, 0, 0, 7));

        cell.Paragraphs[0].Runs.Select(run => run.Text).Should().Equal("before ", "1", "after");
        document.Footnotes[1].PlainText.Should().Be("note");

        bus.Undo();
        cell.Paragraphs[0].PlainText.Should().Be("before after");
        document.Footnotes.Should().BeEmpty();

        bus.Redo();
        cell.Paragraphs[0].Runs.Should().Contain(run => run.FootnoteId == 1);
        document.Footnotes[1].PlainText.Should().Be("note");
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    [Fact]
    public void InsertNote_InsertsAtTextOffset_SplitsFormatting_AndUndoRedo()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var original = new Run("before after")
        {
            Formatting = RunFormatting.Default with { Bold = true },
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(original);
        document.Blocks.Add(paragraph);
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new InsertNoteCommand(1, footnote: true, "note text", 0, 7));

        document.Footnotes[1].PlainText.Should().Be("note text");
        paragraph.Runs.Select(run => run.Text).Should().Equal("before ", "1", "after");
        paragraph.Runs[0].Formatting.Bold.Should().BeTrue();
        paragraph.Runs[1].FootnoteId.Should().Be(1);
        paragraph.Runs[1].Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
        paragraph.Runs[2].Formatting.Bold.Should().BeTrue();

        bus.Undo().Should().BeTrue();
        document.Footnotes.Should().NotContainKey(1);
        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(original);

        bus.Redo().Should().BeTrue();
        document.Footnotes[1].PlainText.Should().Be("note text");
        paragraph.Runs.Select(run => run.Text).Should().Equal("before ", "1", "after");
    }

    [Fact]
    public void InsertNote_DoesNotOverwriteAnExistingNoteId()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("body"));
        document.Endnotes[2] = new Endnote(2, "existing");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new InsertNoteCommand(2, footnote: false, "replacement", 0, 2));

        document.Endnotes[2].PlainText.Should().Be("existing");
        ((Paragraph)document.Blocks[0]).Runs.Should().NotContain(run => run.EndnoteId == 2);
    }

    [Fact]
    public void ReplaceNoteContent_PreservesRichParagraphs_AndUndoRedo()
    {
        var document = TextDocument.CreateEmpty();
        var original = new Paragraph();
        original.Runs.Add(new Run("original")
        {
            Formatting = RunFormatting.Default with { Italic = true },
        });
        var footnote = new Footnote(7);
        footnote.Content.Add(original);
        document.Footnotes[7] = footnote;
        var bus = new DocumentCommandBus(new Context(document));

        var replacement = new Paragraph { StyleId = "FootnoteText" };
        replacement.Runs.Add(new Run("updated")
        {
            Formatting = RunFormatting.Default with { Bold = true, ColorHex = "#C00000" },
        });
        bus.Execute(new ReplaceNoteContentCommand(7, footnote: true, [replacement, new Paragraph("more")]));

        document.Footnotes[7].PlainText.Should().Be("updated\nmore");
        document.Footnotes[7].Content[0].StyleId.Should().Be("FootnoteText");
        document.Footnotes[7].Content[0].Runs.Single().Formatting.Bold.Should().BeTrue();

        bus.Undo().Should().BeTrue();
        document.Footnotes[7].PlainText.Should().Be("original");
        document.Footnotes[7].Content.Single().Runs.Single().Formatting.Italic.Should().BeTrue();

        bus.Redo().Should().BeTrue();
        document.Footnotes[7].PlainText.Should().Be("updated\nmore");
        document.Footnotes[7].Content[0].Runs.Single().Formatting.ColorHex.Should().Be("#C00000");
    }

    [Fact]
    public void ReplaceNoteContent_EmptyReplacement_LeavesEditableBlankParagraph()
    {
        var document = TextDocument.CreateEmpty();
        document.Endnotes[3] = new Endnote(3, "old");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new ReplaceNoteContentCommand(3, footnote: false, []));

        document.Endnotes[3].Content.Should().ContainSingle();
        document.Endnotes[3].PlainText.Should().BeEmpty();
        bus.Undo().Should().BeTrue();
        document.Endnotes[3].PlainText.Should().Be("old");
    }

    [Fact]
    public void DeleteNote_RemovesNestedTableMarker_AndUndoRedoRestoresExactRuns()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var body = new Paragraph();
        body.Runs.Add(new Run("body"));
        body.Runs.Add(Run.FootnoteReference(4));
        document.Blocks.Add(body);

        var table = Table.Create(1, 1);
        var cellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Clear();
        cellParagraph.Runs.Add(new Run("cell-before"));
        cellParagraph.Runs.Add(Run.FootnoteReference(4));
        cellParagraph.Runs.Add(new Run("cell-after"));
        document.Blocks.Add(table);
        document.Footnotes[4] = new Footnote(4, "nested marker note");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new DeleteNoteCommand(4, footnote: true));

        document.Footnotes.Should().NotContainKey(4);
        body.Runs.Should().NotContain(run => run.FootnoteId == 4);
        cellParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);

        bus.Undo().Should().BeTrue();
        document.Footnotes[4].PlainText.Should().Be("nested marker note");
        body.Runs.Select(run => (run.Text, run.FootnoteId))
            .Should().Equal(("body", null), ("4", 4));
        cellParagraph.Runs.Select(run => (run.Text, run.FootnoteId))
            .Should().Equal(("cell-before", null), ("4", 4), ("cell-after", null));

        bus.Redo().Should().BeTrue();
        document.Footnotes.Should().NotContainKey(4);
        cellParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);
    }

    [Fact]
    public void DeleteNote_RemovesMarkerInHeaderAndFooter_AndUndoRedoRestoresExactRuns()
    {
        // Word allows a footnote/endnote reference mark inside a header or footer. DeleteNoteCommand
        // must scan those regions too, or the mark is left dangling (pointing at a note that no
        // longer exists) after the note is deleted.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("body text"));
        document.Footnotes[9] = new Footnote(9, "orphaned-marker note");

        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(new Run("header-before"));
        headerParagraph.Runs.Add(Run.FootnoteReference(9));
        headerParagraph.Runs.Add(new Run("header-after"));
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(headerParagraph);

        var footerParagraph = new Paragraph();
        footerParagraph.Runs.Add(new Run("footer-before"));
        footerParagraph.Runs.Add(Run.FootnoteReference(9));
        footerParagraph.Runs.Add(new Run("footer-after"));
        document.Footer = new HeaderFooter();
        document.Footer.Paragraphs.Add(footerParagraph);

        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new DeleteNoteCommand(9, footnote: true));

        document.Footnotes.Should().NotContainKey(9);
        headerParagraph.Runs.Should().NotContain(run => run.FootnoteId == 9);
        footerParagraph.Runs.Should().NotContain(run => run.FootnoteId == 9);

        bus.Undo().Should().BeTrue();
        document.Footnotes[9].PlainText.Should().Be("orphaned-marker note");
        headerParagraph.Runs.Select(run => (run.Text, run.FootnoteId))
            .Should().Equal(("header-before", null), ("9", 9), ("header-after", null));
        footerParagraph.Runs.Select(run => (run.Text, run.FootnoteId))
            .Should().Equal(("footer-before", null), ("9", 9), ("footer-after", null));

        bus.Redo().Should().BeTrue();
        document.Footnotes.Should().NotContainKey(9);
        headerParagraph.Runs.Should().NotContain(run => run.FootnoteId == 9);
        footerParagraph.Runs.Should().NotContain(run => run.FootnoteId == 9);
    }

    [Fact]
    public void DeleteNote_StillRemovesBodyAndTableMarkers_WhenDocumentHasNoHeaderFooter()
    {
        // Sibling no-regression: a document with no headers/footers at all (Sections still yields the
        // single trailing final section) must keep working exactly as before — body + table markers
        // removed, header/footer scan finding nothing to do.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var body = new Paragraph();
        body.Runs.Add(new Run("body"));
        body.Runs.Add(Run.FootnoteReference(5));
        document.Blocks.Add(body);
        document.Footnotes[5] = new Footnote(5, "plain note");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new DeleteNoteCommand(5, footnote: true));

        document.Footnotes.Should().NotContainKey(5);
        body.Runs.Should().NotContain(run => run.FootnoteId == 5);

        bus.Undo().Should().BeTrue();
        document.Footnotes[5].PlainText.Should().Be("plain note");
        body.Runs.Should().Contain(run => run.FootnoteId == 5);
    }

    [Fact]
    public void SetNoteNumberingOptions_UpdatesBothKinds_AndUndoRedoRestoresExactSettings()
    {
        var document = TextDocument.CreateEmpty();
        document.FootnoteNumbering.NumberFormat = NoteNumberFormat.Decimal;
        document.FootnoteNumbering.StartAt = 2;
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.Continuous;
        document.EndnoteNumbering.NumberFormat = NoteNumberFormat.UpperLetter;
        document.EndnoteNumbering.StartAt = 3;
        document.EndnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new SetNoteNumberingOptionsCommand(
            NoteNumberFormat.UpperRoman,
            7,
            NoteNumberRestart.EachPage,
            NoteNumberFormat.LowerLetter,
            11,
            NoteNumberRestart.Continuous));

        (document.FootnoteNumbering.NumberFormat,
                document.FootnoteNumbering.StartAt,
                document.FootnoteNumbering.NumberRestart)
            .Should().Be((NoteNumberFormat.UpperRoman, 7, NoteNumberRestart.EachPage));
        (document.EndnoteNumbering.NumberFormat,
                document.EndnoteNumbering.StartAt,
                document.EndnoteNumbering.NumberRestart)
            .Should().Be((NoteNumberFormat.LowerLetter, 11, NoteNumberRestart.Continuous));

        bus.Undo().Should().BeTrue();
        (document.FootnoteNumbering.NumberFormat,
                document.FootnoteNumbering.StartAt,
                document.FootnoteNumbering.NumberRestart)
            .Should().Be((NoteNumberFormat.Decimal, 2, NoteNumberRestart.Continuous));
        (document.EndnoteNumbering.NumberFormat,
                document.EndnoteNumbering.StartAt,
                document.EndnoteNumbering.NumberRestart)
            .Should().Be((NoteNumberFormat.UpperLetter, 3, NoteNumberRestart.EachSection));

        bus.Redo().Should().BeTrue();
        document.FootnoteNumbering.StartAt.Should().Be(7);
        document.EndnoteNumbering.StartAt.Should().Be(11);
    }

    [Fact]
    public void DeleteNote_RemovesMarkerInsideTableNestedInCell_AndUndoRedoRestoresExactRuns()
    {
        // Distinct from DeleteNote_RemovesNestedTableMarker_AndUndoRedoRestoresExactRuns above: here the
        // marker paragraph lives inside a table that is itself nested inside the outer table's cell.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var body = new Paragraph();
        body.Runs.Add(new Run("body"));
        body.Runs.Add(Run.FootnoteReference(4));
        document.Blocks.Add(body);

        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        var cellParagraph = nestedTable.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Clear();
        cellParagraph.Runs.Add(new Run("cell-before"));
        cellParagraph.Runs.Add(Run.FootnoteReference(4));
        cellParagraph.Runs.Add(new Run("cell-after"));
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        document.Blocks.Add(outerTable);
        document.Footnotes[4] = new Footnote(4, "deeply nested marker note");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new DeleteNoteCommand(4, footnote: true));

        document.Footnotes.Should().NotContainKey(4);
        body.Runs.Should().NotContain(run => run.FootnoteId == 4);
        cellParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);

        bus.Undo().Should().BeTrue();
        document.Footnotes[4].PlainText.Should().Be("deeply nested marker note");
        cellParagraph.Runs.Select(run => (run.Text, run.FootnoteId))
            .Should().Equal(("cell-before", null), ("4", 4), ("cell-after", null));

        bus.Redo().Should().BeTrue();
        document.Footnotes.Should().NotContainKey(4);
        cellParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);
    }

    // freew-footnote-numbering F4: a footnote's only reference marker lives inside a text box (Run.Shape)
    // in the body. DeleteNoteCommand's own private paragraph walk never descended into Run.Shape.
    // TextParagraphs, unlike the shared BodyParagraphWalk used elsewhere for the identical orphan-cleanup
    // job (DocumentInspector.PruneOrphanedNoteAndCommentAnchors), so the marker run was left behind still
    // carrying the now-nonexistent FootnoteId after the note's content was removed.
    [Fact]
    public void DeleteNote_RemovesMarkerInsideBodyTextBox_AndUndoRedoRestoresExactRuns()
    {
        var shape = new Shape(ShapeKind.TextBox, 100, 50);
        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(new Run("box-before"));
        shapeParagraph.Runs.Add(Run.FootnoteReference(1));
        shapeParagraph.Runs.Add(new Run("box-after"));
        shape.TextParagraphs.Add(shapeParagraph);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var hostParagraph = new Paragraph();
        hostParagraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(hostParagraph);
        document.Footnotes[1] = new Footnote(1, "boxed marker note");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new DeleteNoteCommand(1, footnote: true));

        document.Footnotes.Should().NotContainKey(1);
        shapeParagraph.Runs.Should().NotContain(run => run.FootnoteId == 1);

        bus.Undo().Should().BeTrue();
        document.Footnotes[1].PlainText.Should().Be("boxed marker note");
        shapeParagraph.Runs.Select(run => (run.Text, run.FootnoteId))
            .Should().Equal(("box-before", null), ("1", 1), ("box-after", null));

        bus.Redo().Should().BeTrue();
        document.Footnotes.Should().NotContainKey(1);
        shapeParagraph.Runs.Should().NotContain(run => run.FootnoteId == 1);
    }

    // Sibling no-regression, endnote flavor plus a text box embedded in a header (Word allows note
    // references there too, and DeleteNoteCommand's header/footer scan must reach into a shape the same
    // way the body scan does).
    [Fact]
    public void DeleteNote_RemovesEndnoteMarkerInsideHeaderTextBox_AndUndoRedoRestoresExactRuns()
    {
        var shape = new Shape(ShapeKind.TextBox, 100, 50);
        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(new Run("header-box-before"));
        shapeParagraph.Runs.Add(Run.EndnoteReference(2));
        shapeParagraph.Runs.Add(new Run("header-box-after"));
        shape.TextParagraphs.Add(shapeParagraph);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("body text"));
        document.Header = new HeaderFooter();
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(Run.FromShape(shape));
        document.Header.Paragraphs.Add(headerParagraph);
        document.Endnotes[2] = new Endnote(2, "boxed header endnote");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new DeleteNoteCommand(2, footnote: false));

        document.Endnotes.Should().NotContainKey(2);
        shapeParagraph.Runs.Should().NotContain(run => run.EndnoteId == 2);

        bus.Undo().Should().BeTrue();
        document.Endnotes[2].PlainText.Should().Be("boxed header endnote");
        shapeParagraph.Runs.Select(run => (run.Text, run.EndnoteId))
            .Should().Equal(("header-box-before", null), ("2", 2), ("header-box-after", null));

        bus.Redo().Should().BeTrue();
        document.Endnotes.Should().NotContainKey(2);
        shapeParagraph.Runs.Should().NotContain(run => run.EndnoteId == 2);
    }

    // Sibling no-regression: a plain (non-shape) body/table marker deletion, and a clean text box with no
    // marker at all, must both keep behaving exactly as before -- the shape descent must not remove or
    // touch runs it shouldn't.
    [Fact]
    public void DeleteNote_WithUnrelatedCleanTextBoxPresent_LeavesBoxUntouchedAndStillRemovesBodyMarker()
    {
        var shape = new Shape(ShapeKind.TextBox, 100, 50);
        var cleanBoxParagraph = new Paragraph();
        cleanBoxParagraph.Runs.Add(new Run("unrelated box text"));
        shape.TextParagraphs.Add(cleanBoxParagraph);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var body = new Paragraph();
        body.Runs.Add(new Run("body"));
        body.Runs.Add(Run.FootnoteReference(6));
        document.Blocks.Add(body);

        var hostParagraph = new Paragraph();
        hostParagraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(hostParagraph);
        document.Footnotes[6] = new Footnote(6, "plain note with unrelated box nearby");
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new DeleteNoteCommand(6, footnote: true));

        document.Footnotes.Should().NotContainKey(6);
        body.Runs.Should().NotContain(run => run.FootnoteId == 6);
        cleanBoxParagraph.Runs.Select(run => run.Text).Should().Equal("unrelated box text");

        bus.Undo().Should().BeTrue();
        document.Footnotes[6].PlainText.Should().Be("plain note with unrelated box nearby");
        body.Runs.Should().Contain(run => run.FootnoteId == 6);
        cleanBoxParagraph.Runs.Select(run => run.Text).Should().Equal("unrelated box text");
    }
}
