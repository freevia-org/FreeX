using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentSortMutationPlannerTests
{
    [Fact]
    public void Paragraph_sort_reorders_only_paragraph_slots_and_pins_header()
    {
        var document = new TextDocument();
        var header = new Paragraph("Heading");
        var table = Table.Create(1, 1);
        document.Blocks.Add(header);
        document.Blocks.Add(new Paragraph("Zulu"));
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph("alpha"));

        var plan = DocumentSortMutationPlanner.PlanParagraphSort(
            document,
            [0, 1, 3],
            SortKind.Text,
            ascending: true,
            caseSensitive: false,
            hasHeaderRow: true);

        plan.Should().NotBeNull();
        plan!.StartIndex.Should().Be(0);
        plan.RemoveCount.Should().Be(4);
        plan.Replacement[0].Should().BeSameAs(header);
        plan.Replacement[1].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("alpha");
        plan.Replacement[2].Should().BeSameAs(table);
        plan.Replacement[3].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Zulu");
    }

    [Fact]
    public void Paragraph_sort_rejects_empty_invalid_and_single_paragraph_ranges()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Only"));

        DocumentSortMutationPlanner.PlanParagraphSort(
            document, [], SortKind.Text, true, false, false).Should().BeNull();
        DocumentSortMutationPlanner.PlanParagraphSort(
            document, [0], SortKind.Text, true, false, false).Should().BeNull();
        DocumentSortMutationPlanner.PlanParagraphSort(
            document, [-1, 2], SortKind.Text, true, false, false).Should().BeNull();
    }

    [Fact]
    public void Table_sort_preserves_table_shell_and_row_identity()
    {
        var document = new TextDocument();
        var table = Table.Create(0, 0);
        table.ColumnWidthsPt.AddRange([80, 120]);
        table.Formatting = table.Formatting with { HeaderRow = true };
        var header = Row("Rank", "Name");
        var zulu = Row("10", "Zulu");
        var alpha = Row("2", "Alpha");
        table.Rows.AddRange([header, zulu, alpha]);
        document.Blocks.Add(table);

        var plan = DocumentSortMutationPlanner.PlanTableRowSort(
            document,
            0,
            0,
            SortKind.Number,
            ascending: true,
            caseSensitive: false,
            hasHeaderRow: true);

        plan.Should().NotBeNull();
        var replacement = plan!.Replacement.Should().ContainSingle().Which.Should().BeOfType<Table>().Which;
        replacement.Should().NotBeSameAs(table);
        replacement.Formatting.Should().Be(table.Formatting);
        replacement.ColumnWidthsPt.Should().Equal(80, 120);
        replacement.Rows.Should().Equal(header, alpha, zulu);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Table_sort_rejects_non_table_blocks(int blockIndex)
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Not a table"));

        DocumentSortMutationPlanner.PlanTableRowSort(
            document, blockIndex, 0, SortKind.Text, true, false, false).Should().BeNull();
    }

    private static TableRow Row(params string[] values)
    {
        var row = new TableRow();
        foreach (var value in values)
        {
            var cell = new TableCell();
            cell.Paragraphs.Add(new Paragraph(value));
            row.Cells.Add(cell);
        }
        return row;
    }
}
