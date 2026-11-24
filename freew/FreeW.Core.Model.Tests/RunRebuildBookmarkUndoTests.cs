namespace FreeW.Core.Model.Tests;

public sealed class RunRebuildBookmarkUndoTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    [Theory]
    [InlineData("body")]
    [InlineData("cell-grid")]
    [InlineData("cell-stored")]
    [InlineData("header")]
    public void UnicodeDeletionUndoRedoRestoresBookmarkRunIndicesTogetherWithRuns(string target)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("a"));
        paragraph.Runs.Add(new Run("e", RunFormatting.Default with { Bold = true }));
        paragraph.Runs.Add(new Run("\u0301"));
        paragraph.Runs.Add(new Run("tail"));
        paragraph.BookmarkNames.Add("tail-bookmark");
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("b", BookmarkBoundaryKind.Start, 3, "tail-bookmark"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("b", BookmarkBoundaryKind.End, 4));
        var original = paragraph.BookmarkBoundaries.ToArray();
        void Delete(Paragraph item) => TextElementDeletionPlanner.Apply(item, new(1, 2), false, "reviewer", null);
        IDocumentCommand command;
        if (target.StartsWith("cell", StringComparison.Ordinal))
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0].Paragraphs[0] = paragraph;
            document.Blocks.Add(table);
            command = target == "cell-grid"
                ? new ReplaceCellParagraphRunsCommand(0, 0, 0, 0, Delete)
                : new ReplaceTableCellParagraphRunsCommand(0, new TableParagraphAddress(0, 0, 0), Delete);
        }
        else if (target == "header")
        {
            document.Blocks.Add(new Paragraph("body"));
            document.FinalSectionHeadersFooters.Header = new HeaderFooter();
            document.FinalSectionHeadersFooters.Header.Paragraphs.Add(paragraph);
            command = new EditHeaderFooterParagraphCommand(document.Sections.Count - 1, true, 0, 0, Delete);
        }
        else
        {
            document.Blocks.Add(paragraph);
            command = new ReplaceParagraphRunsCommand(0, Delete);
        }
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(command);
        paragraph.PlainText.Should().Be("atail");
        paragraph.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 2);
        bus.Undo().Should().BeTrue();
        paragraph.Runs.Select(run => run.Text).Should().Equal("a", "e", "\u0301", "tail");
        paragraph.BookmarkBoundaries.Should().Equal(original);
        paragraph.Runs[1].Formatting.Bold.Should().BeTrue();
        bus.Redo().Should().BeTrue();
        paragraph.PlainText.Should().Be("atail");
        paragraph.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 2);
        paragraph.BookmarkNames.Should().Equal("tail-bookmark");
    }
}
