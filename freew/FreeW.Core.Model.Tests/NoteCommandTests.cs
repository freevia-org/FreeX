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
}
