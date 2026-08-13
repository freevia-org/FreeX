using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterTextEditPlannerTests
{
    [Fact]
    public void PlanDelete_removes_a_same_paragraph_range_and_preserves_run_metadata()
    {
        var story = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Alpha", new RunFormatting { Bold = true })
        {
            HyperlinkUrl = "https://example.test"
        });
        story.Paragraphs.Add(paragraph);

        var plan = HeaderFooterTextEditPlanner.PlanDelete(
            story,
            new HeaderFooterTextRange(
                new HeaderFooterTextPosition(0, 1),
                new HeaderFooterTextPosition(0, 4)));

        plan.Should().NotBeNull();
        plan!.FirstParagraphIndex.Should().Be(0);
        plan.RemoveCount.Should().Be(1);
        plan.Caret.Should().Be(new HeaderFooterTextPosition(0, 1));
        plan.ReplacementParagraphs.Should().ContainSingle();
        plan.ReplacementParagraphs[0].PlainText.Should().Be("Aa");
        plan.ReplacementParagraphs[0].Runs.Should().ContainSingle();
        plan.ReplacementParagraphs[0].Runs[0].Formatting.Bold.Should().BeTrue();
        plan.ReplacementParagraphs[0].Runs[0].HyperlinkUrl.Should().Be("https://example.test");
    }

    [Fact]
    public void PlanDelete_merges_cross_paragraph_prefix_and_suffix()
    {
        var story = Story("Alpha", "Middle", "Omega");

        var plan = HeaderFooterTextEditPlanner.PlanDelete(
            story,
            new HeaderFooterTextRange(
                new HeaderFooterTextPosition(0, 2),
                new HeaderFooterTextPosition(2, 3)));

        plan.Should().NotBeNull();
        plan!.RemoveCount.Should().Be(3);
        plan.Caret.Should().Be(new HeaderFooterTextPosition(0, 2));
        plan.ReplacementParagraphs.Should().ContainSingle();
        plan.ReplacementParagraphs[0].PlainText.Should().Be("Alga");
    }

    [Fact]
    public void PlanDelete_snaps_a_partial_field_selection_to_the_whole_field()
    {
        var story = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Before "));
        paragraph.Runs.Add(new Run("PAGE") { FieldKind = RunFieldKind.PageNumber });
        paragraph.Runs.Add(new Run(" after"));
        story.Paragraphs.Add(paragraph);

        var plan = HeaderFooterTextEditPlanner.PlanDelete(
            story,
            new HeaderFooterTextRange(
                new HeaderFooterTextPosition(0, 8),
                new HeaderFooterTextPosition(0, 9)));

        plan.Should().NotBeNull();
        plan!.Caret.Should().Be(new HeaderFooterTextPosition(0, 7));
        plan.ReplacementParagraphs[0].PlainText.Should().Be("Before  after");
        plan.ReplacementParagraphs[0].Runs.Should().NotContain(run => run.FieldKind == RunFieldKind.PageNumber);
    }

    [Fact]
    public void Shared_paragraph_commands_apply_and_revert_header_footer_edits()
    {
        var document = TextDocument.CreateEmpty();
        document.FinalSectionHeadersFooters.Header = Story("Alpha", "Beta");
        var context = new TestCommandContext(document);
        var edit = new FreeW.App.Presentation.DocumentView.EditHeaderFooterParagraphCommand(
            sectionIndex: 0,
            useFinalSectionStore: true,
            slot: 0,
            paragraphIndex: 0,
            paragraph => paragraph.Runs[0].Text = "Changed");

        edit.Apply(context);
        document.FinalSectionHeadersFooters.Header.Paragraphs[0].PlainText.Should().Be("Changed");
        edit.Revert(context);
        document.FinalSectionHeadersFooters.Header.Paragraphs[0].PlainText.Should().Be("Alpha");

        var splice = new FreeW.App.Presentation.DocumentView.SpliceHeaderFooterParagraphsCommand(
            sectionIndex: 0,
            useFinalSectionStore: true,
            slot: 0,
            firstParagraphIndex: 0,
            removeCount: 2,
            buildReplacement: () => [new Paragraph("Merged")]);
        splice.Apply(context);
        document.FinalSectionHeadersFooters.Header.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Merged");
        splice.Revert(context);
        document.FinalSectionHeadersFooters.Header.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Alpha", "Beta");
    }

    private static HeaderFooter Story(params string[] paragraphs)
    {
        var story = new HeaderFooter();
        foreach (var text in paragraphs)
            story.Paragraphs.Add(new Paragraph(text));
        return story;
    }

    private sealed class TestCommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
