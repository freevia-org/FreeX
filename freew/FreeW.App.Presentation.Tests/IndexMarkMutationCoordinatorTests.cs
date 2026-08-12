using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class IndexMarkMutationCoordinatorTests
{
    [Fact]
    public void TryMark_PreservesRubyAsOneSemanticRun()
    {
        var ruby = new RubyAnnotation();
        ruby.BaseFragments.Add(new RubyTextFragment("Alpha Beta", RunFormatting.Default));
        ruby.PhoneticFragments.Add(new RubyTextFragment("alpha beta", RunFormatting.Default));
        var paragraph = new Paragraph();
        var rubyRun = Run.FromRuby(ruby);
        paragraph.Runs.Add(rubyRun);
        var document = DocumentWith(paragraph);
        var bus = new DocumentCommandBus(new Context(document));

        var marked = IndexMarkMutationCoordinator.TryMark(
            document,
            bus,
            blockIndex: 0,
            textOffset: 5,
            new IndexMark("Alpha"));

        marked.Should().BeTrue();
        paragraph.Runs.Should().HaveCount(2);
        paragraph.Runs[0].Should().BeSameAs(rubyRun);
        paragraph.Runs[0].Ruby.Should().BeSameAs(ruby);
        DocumentIndex.MarkedEntry(paragraph.Runs[1]).Should().Be(new IndexMark("Alpha"));

        bus.Undo().Should().BeTrue();
        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(rubyRun);
    }

    [Fact]
    public void TryMark_RejectsEquivalentMarkWithoutAddingHistory()
    {
        var paragraph = new Paragraph("Alpha");
        paragraph.Runs.Add(DocumentIndex.MarkRun(new IndexMark("Alpha")));
        var document = DocumentWith(paragraph);
        var bus = new DocumentCommandBus(new Context(document));

        var marked = IndexMarkMutationCoordinator.TryMark(
            document,
            bus,
            0,
            paragraph.PlainText.Length,
            new IndexMark("alpha"));

        marked.Should().BeFalse();
        bus.CanUndo.Should().BeFalse();
        paragraph.Runs.Count(run => DocumentIndex.MarkedEntry(run) is not null).Should().Be(1);
    }

    [Fact]
    public void MarkAll_MarksBodyAndTableParagraphsAsOneUndoableMutation()
    {
        var body = new Paragraph("Alpha body");
        var table = Table.Create(1, 1);
        var cellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Clear();
        cellParagraph.Runs.Add(new Run("alpha cell"));
        var document = DocumentWith(body, table);
        var bus = new DocumentCommandBus(new Context(document));

        var count = IndexMarkMutationCoordinator.MarkAll(
            document,
            bus,
            "Alpha",
            new IndexMark("Alpha", Subentry: "Examples"));

        count.Should().Be(2);
        body.Runs.Should().ContainSingle(run => DocumentIndex.MarkedEntry(run) != null);
        cellParagraph.Runs.Should().ContainSingle(run => DocumentIndex.MarkedEntry(run) != null);

        bus.Undo().Should().BeTrue();
        body.Runs.Should().NotContain(run => DocumentIndex.MarkedEntry(run) != null);
        cellParagraph.Runs.Should().NotContain(run => DocumentIndex.MarkedEntry(run) != null);
        bus.CanUndo.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Missing")]
    public void MarkAll_WithNoValidTargets_DoesNotAddHistory(string sourceText)
    {
        var document = DocumentWith(new Paragraph("Alpha"));
        var bus = new DocumentCommandBus(new Context(document));

        IndexMarkMutationCoordinator.MarkAll(
            document,
            bus,
            sourceText,
            new IndexMark("Alpha")).Should().Be(0);

        bus.CanUndo.Should().BeFalse();
    }

    private static TextDocument DocumentWith(params Block[] blocks)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(blocks);
        return document;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
