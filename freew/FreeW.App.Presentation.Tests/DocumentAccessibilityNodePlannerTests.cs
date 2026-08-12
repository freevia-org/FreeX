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
    public void Build_includes_floating_images_and_repeats_structural_ids_deterministically()
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

        first.Children[0].SemanticChildren.Select(node => node.Name).Should().Equal("Floating", "Inline");
        first.Children[0].SemanticChildren[0].IsFloatingObject.Should().BeTrue();
        second.ById.Keys.Should().Equal(first.ById.Keys);
    }

    [Fact]
    public void Build_projects_all_drawing_object_kinds_and_nested_group_children()
    {
        var paragraph = new Paragraph();
        var shape = Shape.TextBoxWith("Quarterly result", 120, 60);
        shape.AltText = "Results callout";
        shape.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square };
        paragraph.Runs.Add(Run.FromShape(shape));
        var chart = new Chart { Title = "Revenue", Kind = ChartKind.Line };
        chart.Categories.AddRange(["Q1", "Q2"]);
        chart.Series.Add(new ChartSeries("Actual", [12, 18]));
        paragraph.Runs.Add(Run.FromChart(chart));
        paragraph.Runs.Add(Run.FromWordArt(new WordArt { Text = "Launch", AltText = "Launch banner" }));
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(new SmartArtNode("Director", [new SmartArtNode("Manager")]));
        paragraph.Runs.Add(Run.FromSmartArt(smartArt));
        var group = new DrawingGroup();
        group.Children.Add(new InlineImage([], 40, 30) { AltText = "Logo" });
        group.Children.Add(Shape.TextBoxWith("Grouped note", 80, 30));
        group.ChildOffsets.Add((0, 0));
        group.ChildOffsets.Add((45, 0));
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        paragraph.Runs.Add(Run.FromEmbeddedObject(EmbeddedObject.Create([], "Excel.Sheet.12")));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var objects = DocumentAccessibilityNodePlanner.Build(document).Children[0].SemanticChildren;

        objects.Select(node => node.Kind).Should().Equal(
            DocumentAccessibilityNodeKind.Shape,
            DocumentAccessibilityNodeKind.Chart,
            DocumentAccessibilityNodeKind.WordArt,
            DocumentAccessibilityNodeKind.SmartArt,
            DocumentAccessibilityNodeKind.DrawingGroup,
            DocumentAccessibilityNodeKind.EmbeddedObject);
        objects[0].Name.Should().Be("Results callout");
        objects[0].Value.Should().Be("Quarterly result");
        objects[0].IsFloatingObject.Should().BeTrue();
        objects[1].Name.Should().Be("Revenue");
        objects[1].Value.Should().Be("Revenue. Categories: Q1, Q2. Actual: 12, 18");
        objects[2].Value.Should().Be("Launch");
        objects[3].Value.Should().Be("Director; Manager");
        objects[4].SemanticChildren.Select(node => node.Name).Should().Equal("Logo", "Grouped note");
        objects[4].SemanticChildren[1].ObjectPath.Should().Equal(1);
        objects[5].Name.Should().Be("Excel.Sheet.12");
    }

    [Fact]
    public void Build_projects_embedded_object_inside_table_cell_with_grid_coordinates()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(1, 2);
        var objectParagraph = table.Rows[0].Cells[1].Paragraphs[0];
        objectParagraph.Runs.Clear();
        objectParagraph.Runs.Add(new Run("Before "));
        objectParagraph.Runs.Add(Run.FromEmbeddedObject(EmbeddedObject.Create(
            [1, 2, 3],
            "Excel.Sheet.12",
            new InlineImage([4, 5, 6], 32, 24) { AltText = "Quarterly workbook" })));
        objectParagraph.Runs.Add(new Run(" after"));
        document.Blocks.Add(table);

        var tableNode = DocumentAccessibilityNodePlanner.Build(document).Children.Single();
        var objectNode = tableNode.SemanticChildren[0]
            .SemanticChildren[1]
            .SemanticChildren[0]
            .SemanticChildren.Single(node => node.Kind == DocumentAccessibilityNodeKind.EmbeddedObject);

        objectNode.Name.Should().Be("Quarterly workbook");
        objectNode.HelpText.Should().Be("Embedded Excel.Sheet.12 object");
        objectNode.BlockIndex.Should().Be(0);
        objectNode.RowIndex.Should().Be(0);
        objectNode.ColumnIndex.Should().Be(1);
        objectNode.ParagraphIndex.Should().Be(0);
        objectNode.RunIndex.Should().Be(1);
    }

    [Fact]
    public void Build_projects_each_section_header_footer_story_with_stable_context()
    {
        var document = TextDocument.CreateEmpty();
        document.Header = new HeaderFooter("Default heading");
        document.FirstFooter = new HeaderFooter("First-page footer");
        var earlierSection = new Section(new PageSettings(), SectionBreakKind.NextPage);
        earlierSection.HeadersFooters.EvenHeader = new HeaderFooter("Earlier even heading");
        ((Paragraph)document.Blocks[0]).SectionBreak = earlierSection;

        var tree = DocumentAccessibilityNodePlanner.Build(document);
        var stories = tree.Children
            .Where(node => node.Kind == DocumentAccessibilityNodeKind.HeaderFooterStory)
            .ToArray();

        stories.Select(node => node.Id).Should().Equal(
            "section:0:story:even-header",
            "section:1:story:default-header",
            "section:1:story:first-footer");
        stories.Select(node => node.Value).Should().Equal(
            "Earlier even heading",
            "Default heading",
            "First-page footer");
        var finalHeaderParagraph = stories[1].SemanticChildren.Should().ContainSingle().Subject;
        finalHeaderParagraph.SectionIndex.Should().Be(1);
        finalHeaderParagraph.StoryKind.Should().Be(DocumentAccessibilityStoryKind.Header);
        finalHeaderParagraph.Id.Should().Be("section:1:story:default-header:paragraph:0");
        tree.ById.Should().ContainKey(finalHeaderParagraph.Id);
    }
}
