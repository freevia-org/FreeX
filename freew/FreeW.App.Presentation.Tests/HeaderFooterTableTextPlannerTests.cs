using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterTableTextPlannerTests
{
    [Fact]
    public void Maps_flat_story_indices_to_authored_table_cells_and_back()
    {
        var story = LayoutStory(
            ["Left one", "Left two"],
            ["Center"],
            ["Right"]);

        HeaderFooterTableTextPlanner.TryResolveAddress(story, 1, out var leftSecond).Should().BeTrue();
        leftSecond.Should().Be(new HeaderFooterTableParagraphAddress(0, 0, 1));
        HeaderFooterTableTextPlanner.ResolveParagraphIndex(story, leftSecond).Should().Be(1);

        HeaderFooterTableTextPlanner.TryResolveAddress(story, 2, out var center).Should().BeTrue();
        center.Should().Be(new HeaderFooterTableParagraphAddress(0, 1, 0));
        HeaderFooterTableTextPlanner.ResolveParagraphIndex(story, center).Should().Be(2);
    }

    [Fact]
    public void Splice_scope_never_crosses_an_authored_cell_boundary()
    {
        var story = LayoutStory(
            ["Left one", "Left two"],
            ["Center"],
            ["Right"]);

        HeaderFooterTableTextPlanner.CanSplice(story, 0, 1).Should().BeTrue();
        HeaderFooterTableTextPlanner.CanSplice(story, 0, 2).Should().BeTrue();
        HeaderFooterTableTextPlanner.CanSplice(story, 1, 2).Should().BeFalse();
        HeaderFooterTableTextPlanner.AreInSameCell(story, 0, 1).Should().BeTrue();
        HeaderFooterTableTextPlanner.AreInSameCell(story, 1, 2).Should().BeFalse();
    }

    [Fact]
    public void Splice_command_updates_flat_and_cell_views_with_identical_instances_and_reverts()
    {
        var document = TextDocument.CreateEmpty();
        var story = LayoutStory(
            ["Left one", "Left two"],
            ["Center"],
            ["Right"]);
        document.FinalSectionHeadersFooters.Header = story;
        var context = new CommandContext(document);
        var first = new Paragraph("First split");
        var second = new Paragraph("Second split");
        var command = new SpliceHeaderFooterParagraphsCommand(
            sectionIndex: 0,
            useFinalSectionStore: true,
            slot: 0,
            firstParagraphIndex: 0,
            removeCount: 1,
            buildReplacement: () => [first, second]);

        command.Apply(context);

        story.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("First split", "Second split", "Left two", "Center", "Right");
        story.Table!.Rows[0].Cells[0].Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("First split", "Second split", "Left two");
        story.Paragraphs[0].Should().BeSameAs(story.Table.Rows[0].Cells[0].Paragraphs[0]);
        story.Paragraphs[1].Should().BeSameAs(story.Table.Rows[0].Cells[0].Paragraphs[1]);

        command.Revert(context);

        story.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Left one", "Left two", "Center", "Right");
        story.Table.Rows[0].Cells[0].Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Left one", "Left two");
        story.Paragraphs[0].Should().BeSameAs(story.Table.Rows[0].Cells[0].Paragraphs[0]);
    }

    [Fact]
    public void Splice_command_rejects_cross_cell_mutation_without_touching_either_projection()
    {
        var document = TextDocument.CreateEmpty();
        var story = LayoutStory(["Left"], ["Center"], ["Right"]);
        document.FinalSectionHeadersFooters.Header = story;
        var context = new CommandContext(document);
        var command = new SpliceHeaderFooterParagraphsCommand(
            sectionIndex: 0,
            useFinalSectionStore: true,
            slot: 0,
            firstParagraphIndex: 0,
            removeCount: 2,
            buildReplacement: () => [new Paragraph("Merged incorrectly")]);

        command.Apply(context);

        story.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Left", "Center", "Right");
        story.Table!.Rows[0].Cells.Select(cell => cell.Paragraphs.Single().PlainText)
            .Should().Equal("Left", "Center", "Right");
    }

    [Fact]
    public void Cross_cell_delete_plan_removes_text_without_merging_or_removing_cells()
    {
        var document = TextDocument.CreateEmpty();
        var story = LayoutStory(["Alpha", "Beta"], ["Center"], ["Omega"]);
        document.FinalSectionHeadersFooters.Header = story;
        var context = new CommandContext(document);
        var bus = new DocumentCommandBus(context);

        var plan = HeaderFooterTableTextPlanner.PlanDelete(
            story,
            new HeaderFooterTextRange(
                new HeaderFooterTextPosition(0, 2),
                new HeaderFooterTextPosition(3, 2)));

        plan.Should().NotBeNull();
        plan!.Caret.Should().Be(new HeaderFooterTextPosition(0, 2));
        plan.CellPlans.Should().HaveCount(3);
        bus.BeginUndoGroup();
        foreach (var cellPlan in plan.CellPlans.Reverse())
        {
            bus.Execute(new SpliceHeaderFooterParagraphsCommand(
                sectionIndex: 0,
                useFinalSectionStore: true,
                slot: 0,
                cellPlan.FirstParagraphIndex,
                cellPlan.RemoveCount,
                () => cellPlan.ReplacementParagraphs));
        }
        bus.CommitUndoGroup("Delete header/footer table selection");

        story.Table!.Rows[0].Cells.Should().HaveCount(3);
        story.Table.Rows[0].Cells[0].Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Al");
        story.Table.Rows[0].Cells[1].Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("");
        story.Table.Rows[0].Cells[2].Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("ega");
        story.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Al", "", "ega");
        story.Paragraphs[0].Should().BeSameAs(story.Table.Rows[0].Cells[0].Paragraphs[0]);
        story.Paragraphs[1].Should().BeSameAs(story.Table.Rows[0].Cells[1].Paragraphs[0]);
        story.Paragraphs[2].Should().BeSameAs(story.Table.Rows[0].Cells[2].Paragraphs[0]);

        bus.Undo().Should().BeTrue();
        story.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Alpha", "Beta", "Center", "Omega");
        story.Table.Rows[0].Cells[0].Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Alpha", "Beta");
        story.Paragraphs[2].Should().BeSameAs(story.Table.Rows[0].Cells[1].Paragraphs[0]);

        bus.Redo().Should().BeTrue();
        story.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Al", "", "ega");
        story.Paragraphs[1].Should().BeSameAs(story.Table.Rows[0].Cells[1].Paragraphs[0]);
    }

    private static HeaderFooter LayoutStory(params string[][] cells)
    {
        var story = new HeaderFooter();
        var table = Table.Create(1, cells.Length);
        for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
        {
            var cell = new TableCell();
            foreach (var text in cells[cellIndex])
                cell.Paragraphs.Add(new Paragraph(text));
            table.Rows[0].Cells[cellIndex] = cell;
            story.Paragraphs.AddRange(cell.Paragraphs);
        }

        story.Table = table;
        return story;
    }

    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
