using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentAccessibilityNodePlannerTests
{
    [Fact]
    public void Build_creates_stable_paragraph_link_and_image_nodes()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Read "));
        paragraph.Runs.Add(new Run("documentation")
        {
            HyperlinkUrl = "https://example.test/docs",
            HyperlinkTooltip = "Open documentation"
        });
        paragraph.Runs.Add(Run.FromImage(new InlineImage([], 120, 80) { AltText = "Architecture diagram" }));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        var paragraphNode = tree.Children.Should().ContainSingle().Subject;
        paragraphNode.Kind.Should().Be(DocumentAccessibilityNodeKind.Paragraph);
        paragraphNode.Id.Should().Be("block:0:paragraph");
        paragraphNode.Value.Should().Be("Read documentation");
        paragraphNode.SemanticChildren.Select(node => node.Kind).Should().Equal(
            DocumentAccessibilityNodeKind.Hyperlink,
            DocumentAccessibilityNodeKind.Image);
        var link = paragraphNode.SemanticChildren[0];
        link.Id.Should().Be("block:0:paragraph:run:1:hyperlink");
        link.TextStart.Should().Be(5);
        link.TextLength.Should().Be(13);
        link.HyperlinkTarget.Should().Be("https://example.test/docs");
        link.IsInternalHyperlink.Should().BeFalse();
        paragraphNode.SemanticChildren[1].Name.Should().Be("Architecture diagram");
    }

    [Fact]
    public void Build_creates_table_row_grid_cell_and_cell_paragraph_hierarchy()
    {
        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(new TableCell("Merged") { GridSpan = 2 });
        row.Cells.Add(new TableCell("Final"));
        table.Rows.Add(row);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        var tableNode = tree.Children.Should().ContainSingle().Subject;
        tableNode.Name.Should().Be("Table 1, 1 rows, 3 columns");
        var rowNode = tableNode.SemanticChildren.Should().ContainSingle().Subject;
        rowNode.Kind.Should().Be(DocumentAccessibilityNodeKind.TableRow);
        rowNode.SemanticChildren.Select(node => node.ColumnIndex).Should().Equal(0, 2);
        rowNode.SemanticChildren[0].HelpText.Should().Be("Table cell spanning 2 columns and 1 row");
        rowNode.SemanticChildren[1].Id.Should().Be("block:0:table:row:0:column:2");
        rowNode.SemanticChildren[1].SemanticChildren.Should().ContainSingle()
            .Which.Kind.Should().Be(DocumentAccessibilityNodeKind.Paragraph);
    }

    [Fact]
    public void Build_coalesces_formatted_runs_that_belong_to_one_link()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Open ") { HyperlinkUrl = "https://example.test" });
        paragraph.Runs.Add(new Run("site", new RunFormatting { Bold = true }) { HyperlinkUrl = "https://example.test" });
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var link = DocumentAccessibilityNodePlanner.Build(document).Children[0].SemanticChildren.Should().ContainSingle().Subject;

        link.Kind.Should().Be(DocumentAccessibilityNodeKind.Hyperlink);
        link.Id.Should().Be("block:0:paragraph:run:0:hyperlink");
        link.Value.Should().Be("Open site");
        link.TextLength.Should().Be(9);
    }

    [Fact]
    public void Build_uses_internal_link_target_and_fallback_image_name()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Jump") { HyperlinkAnchor = "TargetBookmark" });
        paragraph.Runs.Add(Run.FromImage(new InlineImage([], 10, 20)));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var children = DocumentAccessibilityNodePlanner.Build(document).Children[0].SemanticChildren;

        children[0].IsInternalHyperlink.Should().BeTrue();
        children[0].HyperlinkTarget.Should().Be("TargetBookmark");
        children[1].Name.Should().Be("Image");
        children[1].HelpText.Should().Be("Image, 10 by 20 points");
    }

    [Fact]
    public void Build_projects_headings_nested_tables_and_vertical_merge_semantics()
    {
        var heading = new Paragraph("Scope") { StyleId = "Heading2" };
        var table = new Table { Formatting = TableFormatting.Default with { HeaderRow = true } };
        var firstRow = new TableRow();
        var restart = new TableCell("Merged vertically") { VerticalMerge = VerticalMergeState.Restart };
        var nested = new Table();
        var nestedRow = new TableRow();
        nestedRow.Cells.Add(new TableCell("Nested"));
        nested.Rows.Add(nestedRow);
        restart.NestedTables.Add(nested);
        firstRow.Cells.Add(restart);
        table.Rows.Add(firstRow);
        var secondRow = new TableRow();
        secondRow.Cells.Add(new TableCell { VerticalMerge = VerticalMergeState.Continue });
        table.Rows.Add(secondRow);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(heading);
        document.Blocks.Add(table);

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        tree.Children[0].Kind.Should().Be(DocumentAccessibilityNodeKind.Heading);
        tree.Children[0].HeadingLevel.Should().Be(2);
        var tableNode = tree.Children[1];
        var mergedCell = tableNode.SemanticChildren[0].SemanticChildren.Should().ContainSingle().Subject;
        mergedCell.RowSpan.Should().Be(2);
        mergedCell.IsHeader.Should().BeTrue();
        mergedCell.SemanticChildren[0].Kind.Should().Be(DocumentAccessibilityNodeKind.Table,
            "nested tables precede the cell's paragraphs in semantic order");
        tableNode.SemanticChildren[1].SemanticChildren.Should().BeEmpty(
            "vertical-merge continuation cells are represented by the restart cell's row span");
    }

    [Fact]
    public void Build_excludes_floating_images_and_repeats_structural_ids_deterministically()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage([], 40, 30)
        {
            AltText = "Floating",
            Wrapping = ImageWrapping.Square
        }));
        paragraph.Runs.Add(Run.FromImage(new InlineImage([], 40, 30) { AltText = "Inline" }));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var first = DocumentAccessibilityNodePlanner.Build(document);
        var second = DocumentAccessibilityNodePlanner.Build(document);

        first.Children[0].SemanticChildren.Should().ContainSingle().Which.Name.Should().Be("Inline");
        second.ById.Keys.Should().Equal(first.ById.Keys);
    }
}
