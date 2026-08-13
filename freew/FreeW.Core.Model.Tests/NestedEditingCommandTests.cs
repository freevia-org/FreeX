namespace FreeW.Core.Model.Tests;

public sealed class NestedEditingCommandTests
{
    [Fact]
    public void TableCellRunAndParagraphCommandsHonorGridColumnsAndUndo()
    {
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].GridSpan = 2;
        var target = table.Rows[0].Cells[1];
        target.Paragraphs.Clear();
        target.Paragraphs.Add(new Paragraph("first"));
        target.Paragraphs.Add(new Paragraph("second"));
        var document = new TextDocument { Blocks = { table } };
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new ReplaceCellParagraphRunsCommand(0, 0, 2, 0, paragraph =>
        {
            paragraph.Runs.Clear();
            paragraph.Runs.Add(new Run("updated")
            {
                Formatting = RunFormatting.Default with { Bold = true },
            });
        }));

        target.Paragraphs[0].PlainText.Should().Be("updated");
        target.Paragraphs[0].Runs.Single().Formatting.Bold.Should().BeTrue();
        bus.Undo().Should().BeTrue();
        target.Paragraphs[0].PlainText.Should().Be("first");

        bus.Execute(new SpliceCellParagraphsCommand(
            0,
            0,
            2,
            0,
            2,
            [new Paragraph("joined")]));
        target.Paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal("joined");
        bus.Undo().Should().BeTrue();
        target.Paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal("first", "second");
    }

    [Fact]
    public void EnsureHeaderFooterCreatesOnlyMissingRegionsAndUndoes()
    {
        var document = new TextDocument { Header = new HeaderFooter("existing") };
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new EnsureHeaderFooterCommand(isFooter: false));
        document.Header!.PlainText.Should().Be("existing");
        bus.Undo().Should().BeTrue();
        document.Header!.PlainText.Should().Be("existing");

        bus.Execute(new EnsureHeaderFooterCommand(isFooter: true));
        document.Footer.Should().NotBeNull();
        document.Footer!.Paragraphs.Should().ContainSingle();
        bus.Undo().Should().BeTrue();
        document.Footer.Should().BeNull();
    }

    [Fact]
    public void SectionHeaderFooterRunAndParagraphCommandsUndoAgainstTheAddressedSlot()
    {
        var section = new Section(new PageSettings())
        {
            HeadersFooters = new SectionHeadersFooters
            {
                Footer = new HeaderFooter("before"),
            },
        };
        var document = new TextDocument
        {
            Blocks =
            {
                new Paragraph("section end") { SectionBreak = section },
                new Paragraph("final section"),
            },
        };
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new EditHeaderFooterParagraphCommand(
            0,
            useFinalSectionStore: false,
            slot: 1,
            paragraphIndex: 0,
            paragraph =>
            {
                paragraph.Runs.Clear();
                paragraph.Runs.Add(new Run("edited"));
            }));
        section.HeadersFooters.Footer!.PlainText.Should().Be("edited");
        bus.Undo().Should().BeTrue();
        section.HeadersFooters.Footer!.PlainText.Should().Be("before");

        bus.Execute(new SpliceHeaderFooterParagraphsCommand(
            0,
            useFinalSectionStore: false,
            slot: 1,
            firstParagraphIndex: 0,
            () => [new Paragraph("one"), new Paragraph("two")]));
        section.HeadersFooters.Footer!.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("one", "two");
        bus.Undo().Should().BeTrue();
        section.HeadersFooters.Footer!.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("before");
    }

    [Fact]
    public void HeaderFooterParagraphSplitKeepsPreservedTableAndFlatViewsAliasedAcrossUndoRedo()
    {
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("Left");
        table.Rows[0].Cells[1] = new TableCell("Right");
        var story = new HeaderFooter { Table = table };
        story.Paragraphs.AddRange(table.Rows[0].Cells.SelectMany(cell => cell.Paragraphs));
        var document = TextDocument.CreateEmpty();
        document.FinalSectionHeadersFooters.Header = story;
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new SpliceHeaderFooterParagraphsCommand(
            sectionIndex: 0,
            useFinalSectionStore: true,
            slot: 0,
            firstParagraphIndex: 0,
            () => [new Paragraph("Left A"), new Paragraph("Left B")]));

        story.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Left A", "Left B", "Right");
        table.Rows[0].Cells[0].Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Left A", "Left B");
        story.Paragraphs[0].Should().BeSameAs(table.Rows[0].Cells[0].Paragraphs[0]);

        bus.Undo().Should().BeTrue();
        story.Paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal("Left", "Right");
        story.Paragraphs[0].Should().BeSameAs(table.Rows[0].Cells[0].Paragraphs[0]);

        bus.Redo().Should().BeTrue();
        story.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Left A", "Left B", "Right");
        story.Paragraphs[1].Should().BeSameAs(table.Rows[0].Cells[0].Paragraphs[1]);
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
