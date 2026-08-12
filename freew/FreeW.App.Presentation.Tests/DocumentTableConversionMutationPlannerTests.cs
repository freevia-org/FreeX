using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentTableConversionMutationPlannerTests
{
    [Fact]
    public void Text_to_table_uses_paragraphs_across_mixed_span_and_replaces_whole_span()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("A;B"));
        document.Blocks.Add(Table.Create(1, 1));
        document.Blocks.Add(new Paragraph("C"));

        var plan = DocumentTableConversionMutationPlanner.PlanTextToTable(document, [0, 2], ';');

        plan.Should().NotBeNull();
        plan!.StartIndex.Should().Be(0);
        plan.RemoveCount.Should().Be(3);
        var table = plan.Replacement.Should().ContainSingle().Which.Should().BeOfType<Table>().Which;
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells.Select(cell => cell.PlainText).Should().Equal("A", "B");
        table.Rows[1].Cells.Select(cell => cell.PlainText).Should().Equal("C", "");
        table.Formatting.Borders.Should().BeTrue();
    }

    [Fact]
    public void Text_to_table_rejects_empty_invalid_and_nonparagraph_ranges()
    {
        var document = new TextDocument();
        document.Blocks.Add(Table.Create(1, 1));

        DocumentTableConversionMutationPlanner.PlanTextToTable(document, [], ',').Should().BeNull();
        DocumentTableConversionMutationPlanner.PlanTextToTable(document, [-1, 2], ',').Should().BeNull();
        DocumentTableConversionMutationPlanner.PlanTextToTable(document, [0], ',').Should().BeNull();
    }

    [Fact]
    public void Table_to_text_builds_one_delimited_paragraph_per_row()
    {
        var document = new TextDocument();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("A");
        table.Rows[0].Cells[1] = new TableCell("B");
        table.Rows[1].Cells[0] = new TableCell("C");
        table.Rows[1].Cells[1] = new TableCell("D");
        document.Blocks.Add(table);

        var plan = DocumentTableConversionMutationPlanner.PlanTableToText(document, 0, ';');

        plan.Should().NotBeNull();
        plan!.StartIndex.Should().Be(0);
        plan.RemoveCount.Should().Be(1);
        plan.Replacement.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("A;B", "C;D");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Table_to_text_rejects_invalid_or_non_table_block(int blockIndex)
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body"));

        DocumentTableConversionMutationPlanner.PlanTableToText(document, blockIndex, ',').Should().BeNull();
    }
}
