using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentBlockInsertionMutationPlannerTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(20, 2)]
    [InlineData(int.MaxValue, 2)]
    public void Insertion_index_is_clamped_after_caret(int caretBlockIndex, int expectedIndex)
    {
        var document = DocumentWithTwoParagraphs();

        var plan = DocumentBlockInsertionMutationPlanner.PlanPageBreak(document, caretBlockIndex);

        plan.StartIndex.Should().Be(expectedIndex);
        plan.RemoveCount.Should().Be(0);
        plan.Replacement.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.Formatting.PageBreakBefore.Should().BeTrue();
    }

    [Fact]
    public void Blank_page_is_one_atomic_two_block_replacement()
    {
        var plan = DocumentBlockInsertionMutationPlanner.PlanBlankPage(DocumentWithTwoParagraphs(), 0);

        plan.StartIndex.Should().Be(1);
        plan.RemoveCount.Should().Be(0);
        plan.Replacement.Should().HaveCount(2);
        plan.Replacement.Cast<Paragraph>()
            .Should().OnlyContain(paragraph => paragraph.Formatting.PageBreakBefore);
    }

    [Fact]
    public void Horizontal_rule_and_column_break_use_shared_model_factories()
    {
        var document = DocumentWithTwoParagraphs();

        var rule = DocumentBlockInsertionMutationPlanner.PlanHorizontalRule(document, 0)
            .Replacement.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        rule.Formatting.Border.Should().NotBeNull();
        rule.Formatting.Border!.BottomOnly.Should().BeTrue();

        var columnBreak = DocumentBlockInsertionMutationPlanner.PlanColumnBreak(document, 0)
            .Replacement.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        columnBreak.Runs.Should().ContainSingle().Which.IsColumnBreak.Should().BeTrue();
    }

    [Theory]
    [InlineData(SectionBreakKind.NextPage)]
    [InlineData(SectionBreakKind.Continuous)]
    [InlineData(SectionBreakKind.EvenPage)]
    [InlineData(SectionBreakKind.OddPage)]
    public void Section_break_inherits_document_page_settings(SectionBreakKind kind)
    {
        var document = DocumentWithTwoParagraphs();
        document.Page.WidthPt = 700;
        document.Page.HeightPt = 900;

        var paragraph = DocumentBlockInsertionMutationPlanner.PlanSectionBreak(document, 1, kind)
            .Replacement.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;

        paragraph.SectionBreak.Should().NotBeNull();
        paragraph.SectionBreak!.BreakKind.Should().Be(kind);
        paragraph.SectionBreak.Page.WidthPt.Should().Be(700);
        paragraph.SectionBreak.Page.HeightPt.Should().Be(900);
    }

    private static TextDocument DocumentWithTwoParagraphs()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("First"));
        document.Blocks.Add(new Paragraph("Second"));
        return document;
    }
}
