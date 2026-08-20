using System.Xml.Linq;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentAccessibilityNodePlannerTests
{
    [Fact]
    public void Build_groups_native_body_lists_and_exposes_marker_semantics()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(ListParagraph("First bullet", ListKind.Bullet));
        document.Blocks.Add(ListParagraph("Second bullet", ListKind.Bullet));
        document.Blocks.Add(new Paragraph("Interruption"));
        document.Blocks.Add(ListParagraph("Third item", ListKind.Number, startAt: 3));
        document.Blocks.Add(new Paragraph("Another interruption"));
        document.Blocks.Add(ListParagraph("Fourth item", ListKind.Number));

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        tree.Children.Select(node => node.Kind).Should().Equal(
            DocumentAccessibilityNodeKind.List,
            DocumentAccessibilityNodeKind.Paragraph,
            DocumentAccessibilityNodeKind.List,
            DocumentAccessibilityNodeKind.Paragraph,
            DocumentAccessibilityNodeKind.List);
        var bullets = tree.Children[0];
        bullets.Id.Should().Be("block:0:list:bullet");
        bullets.Name.Should().Be("Bulleted list");
        bullets.SemanticChildren.Select(node => node.Id).Should().Equal(
            "block:0:list-item",
            "block:1:list-item");
        bullets.SemanticChildren.Select(node => node.Value).Should().Equal(
            "• First bullet",
            "• Second bullet");
        bullets.SemanticChildren.Should().OnlyContain(node =>
            node.Kind == DocumentAccessibilityNodeKind.ListItem
            && node.ListKind == ListKind.Bullet
            && node.ListLevel == 0
            && node.ListMarker == "•");
        bullets.SemanticChildren[0].SemanticChildren.Should().ContainSingle()
            .Which.Id.Should().Be("block:0:paragraph");
        tree.Children[2].SemanticChildren.Should().ContainSingle()
            .Which.Value.Should().Be("3. Third item");
        tree.Children[4].SemanticChildren.Should().ContainSingle()
            .Which.Value.Should().Be("4. Fourth item",
                "native numbering continues across intervening non-list body paragraphs like WPF");
    }

    [Fact]
    public void Build_splits_numbered_restarts_and_keeps_lone_multilevel_heading_as_a_heading()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(ListParagraph("One", ListKind.Number));
        document.Blocks.Add(ListParagraph("Restart", ListKind.Number, startAt: 7));
        document.Blocks.Add(new Paragraph("Outline")
        {
            StyleId = "Heading1",
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.MultiLevel,
                ListLevel = 0
            }
        });

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        tree.Children.Select(node => node.Kind).Should().Equal(
            DocumentAccessibilityNodeKind.List,
            DocumentAccessibilityNodeKind.List,
            DocumentAccessibilityNodeKind.Heading);
        tree.Children[0].SemanticChildren.Single().Value.Should().Be("1. One");
        tree.Children[1].SemanticChildren.Single().Value.Should().Be("7. Restart");
        tree.Children[2].Id.Should().Be("block:2:paragraph");
        tree.Children[2].HeadingLevel.Should().Be(1);
        tree.Children[2].ListMarker.Should().Be("1.");
        tree.Children[2].Value.Should().Be("1. Outline");
    }

    [Fact]
    public void Build_prefixes_preserved_word_numbering_without_claiming_native_list_structure()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Preserved.OriginalNumbering = XElement.Parse(
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="10">
                <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="Section %1."/></w:lvl>
              </w:abstractNum>
              <w:num w:numId="2"><w:abstractNumId w:val="10"/></w:num>
            </w:numbering>
            """);
        document.Blocks.Add(new Paragraph("Scope")
        {
            PreservedNumbering = new PreservedNumbering(2, 0)
        });

        var node = DocumentAccessibilityNodePlanner.Build(document).Children.Should().ContainSingle().Subject;

        node.Kind.Should().Be(DocumentAccessibilityNodeKind.Paragraph);
        node.Value.Should().Be("Section I. Scope");
        node.ListMarker.Should().Be("Section I.");
        node.HelpText.Should().Be("List marker Section I.");
    }

    [Fact]
    public void Avalonia_peer_maps_shared_list_roles_and_list_item_values()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "DocumentViewAutomationPeer.cs"));

        var normalized = source.ReplaceLineEndings("\n");
        normalized.Should().Contain("DocumentAccessibilityNodeKind.List => AutomationControlType.List")
            .And.Contain("DocumentAccessibilityNodeKind.ListItem => AutomationControlType.ListItem")
            .And.Contain("DocumentAccessibilityNodeKind.ListItem =>\n                new DocumentValueAutomationPeer")
            .And.Contain("or DocumentAccessibilityNodeKind.ListItem");
    }

    [Fact]
    public void Build_exposes_numbered_footnote_and_endnote_bodies_as_semantic_stories()
    {
        var document = TextDocument.CreateEmpty();
        document.FootnoteNumbering.StartAt = 3;
        document.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerRoman;
        document.Footnotes[9] = new Footnote(9, "First visible footnote");
        document.Footnotes[12] = new Footnote(12, "Second visible footnote");
        document.EndnoteNumbering.NumberFormat = NoteNumberFormat.UpperLetter;
        document.Endnotes[4] = new Endnote(4, "Document endnote");

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        var footnotes = tree.Children.Single(node => node.Kind == DocumentAccessibilityNodeKind.Footnotes);
        footnotes.Id.Should().Be("story:footnotes");
        footnotes.Name.Should().Be("Footnotes");
        footnotes.SemanticChildren.Select(node => node.Id).Should().Equal("footnotes:9", "footnotes:12");
        footnotes.SemanticChildren.Select(node => node.Name).Should().Equal(
            "Footnote iii: First visible footnote",
            "Footnote iv: Second visible footnote");
        footnotes.SemanticChildren.Select(node => node.Value).Should().Equal(
            "iii First visible footnote",
            "iv Second visible footnote");
        footnotes.SemanticChildren.Should().OnlyContain(node =>
            node.Kind == DocumentAccessibilityNodeKind.Footnote && node.BlockIndex == -1);

        var endnotes = tree.Children.Single(node => node.Kind == DocumentAccessibilityNodeKind.Endnotes);
        endnotes.Id.Should().Be("story:endnotes");
        endnotes.SemanticChildren.Should().ContainSingle()
            .Which.Should().Match<DocumentAccessibilityNode>(node =>
                node.Id == "endnotes:4"
                && node.Kind == DocumentAccessibilityNodeKind.Endnote
                && node.Name == "Endnote A: Document endnote"
                && node.Value == "A Document endnote");
        tree.ById.Should().ContainKeys("story:footnotes", "footnotes:9", "story:endnotes", "endnotes:4");
    }

    [Fact]
    public void Build_orders_note_story_children_by_reading_position_not_by_internal_id()
    {
        // r152 remediation H1: mirrors DocumentNoteRegionPlannerNumberRestartTests'
        // Continuous_OrdersFootnotesByReadingPosition_NotByInternalId shape. Insert a footnote after the
        // SECOND sentence first (it becomes id 1), then insert one after the FIRST sentence (it becomes
        // id 2, since NextFootnoteId() = max(existing)+1). Id 2's reference mark reads first, so it must
        // display -- and appear -- as "Footnote 1" before id 1's "Footnote 2", even though AddStory used
        // to lay the story's children out in raw id order.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var firstSentence = new Paragraph();
        firstSentence.Runs.Add(new Run("First sentence."));
        firstSentence.Runs.Add(Run.FootnoteReference(2));
        document.Blocks.Add(firstSentence);

        var secondSentence = new Paragraph();
        secondSentence.Runs.Add(new Run("Second sentence."));
        secondSentence.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(secondSentence);

        document.Footnotes[1] = new Footnote(1, "inserted second, reads second");
        document.Footnotes[2] = new Footnote(2, "inserted last, reads first");

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        var footnotes = tree.Children.Single(node => node.Kind == DocumentAccessibilityNodeKind.Footnotes);
        footnotes.SemanticChildren.Select(node => node.Id).Should().Equal(
            ["footnotes:2", "footnotes:1"],
            "id 2's reference reads first, so its node must be presented before id 1's even though its internal id is higher");
        footnotes.SemanticChildren.Select(node => node.Name).Should().Equal(
            "Footnote 1: inserted last, reads first",
            "Footnote 2: inserted second, reads second");
    }

    [Fact]
    public void Build_note_story_child_order_unaffected_when_ids_already_match_reading_position()
    {
        // Sibling no-regression check: the ordinary case, where insertion order, id order, and
        // reading-order position all agree, must keep the story's child order exactly as before.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var firstSentence = new Paragraph();
        firstSentence.Runs.Add(new Run("First sentence."));
        firstSentence.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(firstSentence);

        var secondSentence = new Paragraph();
        secondSentence.Runs.Add(new Run("Second sentence."));
        secondSentence.Runs.Add(Run.FootnoteReference(2));
        document.Blocks.Add(secondSentence);

        document.Footnotes[1] = new Footnote(1, "inserted first, reads first");
        document.Footnotes[2] = new Footnote(2, "inserted second, reads second");

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        var footnotes = tree.Children.Single(node => node.Kind == DocumentAccessibilityNodeKind.Footnotes);
        footnotes.SemanticChildren.Select(node => node.Id).Should().Equal("footnotes:1", "footnotes:2");
        footnotes.SemanticChildren.Select(node => node.Name).Should().Equal(
            "Footnote 1: inserted first, reads first",
            "Footnote 2: inserted second, reads second");
    }

    [Fact]
    public void Avalonia_peer_maps_note_stories_to_groups_and_note_bodies_to_read_only_values()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "DocumentViewAutomationPeer.cs"));

        var normalized = source.ReplaceLineEndings("\n");
        normalized.Should().Contain(
                "DocumentAccessibilityNodeKind.Footnotes or DocumentAccessibilityNodeKind.Endnotes => AutomationControlType.Group")
            .And.Contain(
                "DocumentAccessibilityNodeKind.Footnote or DocumentAccessibilityNodeKind.Endnote => AutomationControlType.Text")
            .And.Contain(
                "DocumentAccessibilityNodeKind.Footnote or DocumentAccessibilityNodeKind.Endnote =>\n                new DocumentValueAutomationPeer");
    }

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
            DocumentAccessibilityNodeKind.TextRun,
            DocumentAccessibilityNodeKind.Hyperlink,
            DocumentAccessibilityNodeKind.Image);
        var textRun = paragraphNode.SemanticChildren[0];
        textRun.Id.Should().Be("block:0:paragraph:run:0:text");
        textRun.Value.Should().Be("Read ");
        textRun.TextStart.Should().Be(0);
        textRun.TextLength.Should().Be(5);
        var link = paragraphNode.SemanticChildren[1];
        link.Id.Should().Be("block:0:paragraph:run:1:hyperlink");
        link.TextStart.Should().Be(5);
        link.TextLength.Should().Be(13);
        link.HyperlinkTarget.Should().Be("https://example.test/docs");
        link.IsInternalHyperlink.Should().BeFalse();
        link.SemanticChildren.Should().ContainSingle()
            .Which.Kind.Should().Be(DocumentAccessibilityNodeKind.TextRun);
        paragraphNode.SemanticChildren[2].Name.Should().Be("Architecture diagram");
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
        link.SemanticChildren.Select(node => node.Kind).Should().Equal(
            DocumentAccessibilityNodeKind.TextRun,
            DocumentAccessibilityNodeKind.TextRun);
        link.SemanticChildren[1].HelpText.Should().Contain("bold");
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

    private static Paragraph ListParagraph(
        string text,
        ListKind kind,
        int level = 0,
        int? startAt = null) =>
        new(text)
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = kind,
                ListLevel = level,
                ListStartOverride = startAt
            }
        };

    /// <summary>
    /// A content control is a form field, and the projection is the only place a screen reader can learn
    /// that: without a node of its own a filled-in field reaches the user as ordinary prose, with no name,
    /// no kind and no hint that the document forbids changing it.
    /// </summary>
    [Fact]
    public void Build_projects_a_content_control_as_a_named_form_field()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Name: "));
        paragraph.Runs.Add(Run.PlainTextControl("Bob", tag: "NameField", alias: "Applicant"));
        paragraph.Runs.Add(new Run(" (staff)"));
        document.Blocks.Add(paragraph);

        var field = DocumentAccessibilityNodePlanner.Build(document)
            .ById.Values.Single(node => node.Kind == DocumentAccessibilityNodeKind.ContentControl);

        field.Name.Should().Be("Applicant, plain-text field", "the title is what Word announces");
        field.Value.Should().Be("Bob");
        field.HelpText.Should().Be("Plain-text field");
        field.ContentControlKind.Should().Be(ContentControlKind.PlainText);
        field.IsReadOnly.Should().BeFalse();
        field.TextStart.Should().Be(6, "the field starts after \"Name: \"");
        field.TextLength.Should().Be(3);
        field.SemanticChildren.Should().ContainSingle(child => child.Value == "Bob");
    }

    [Fact]
    public void Build_names_an_untitled_field_by_its_kind_and_groups_its_split_runs()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        var first = Run.CheckBoxControl(@checked: true);
        // A field split across runs — mixed formatting, or a tracked edit — is still ONE field.
        var second = new Run("!") { Control = first.Control };
        paragraph.Runs.Add(first);
        paragraph.Runs.Add(second);
        document.Blocks.Add(paragraph);

        var fields = DocumentAccessibilityNodePlanner.Build(document)
            .ById.Values.Where(node => node.Kind == DocumentAccessibilityNodeKind.ContentControl).ToList();

        fields.Should().ContainSingle();
        fields[0].Name.Should().Be("Check box");
        fields[0].ContentControlKind.Should().Be(ContentControlKind.CheckBox);
        fields[0].Value.Should().Be(ContentControl.CheckedGlyph + "!");
    }

    [Theory]
    [InlineData(ContentControlLockMode.NotSpecified, ProtectionMode.None, false)]
    [InlineData(ContentControlLockMode.ContentLocked, ProtectionMode.None, true)]
    [InlineData(ContentControlLockMode.ControlAndContentLocked, ProtectionMode.None, true)]
    // Filling in Forms exists to let these very fields be filled in, so they stay editable...
    [InlineData(ContentControlLockMode.NotSpecified, ProtectionMode.FillingForms, false)]
    // ...while a read-only document makes every field untouchable, and saying otherwise misleads
    // exactly the user who cannot see the greyed-out ribbon.
    [InlineData(ContentControlLockMode.NotSpecified, ProtectionMode.ReadOnly, true)]
    public void Build_reports_whether_a_field_can_actually_be_filled_in(
        ContentControlLockMode lockMode,
        ProtectionMode protection,
        bool expectedReadOnly)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var control = Run.PlainTextControl("Bob", alias: "Applicant");
        control.Control = control.Control! with { LockMode = lockMode };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(control);
        document.Blocks.Add(paragraph);
        document.Protection = new ProtectionSettings(protection);

        var field = DocumentAccessibilityNodePlanner.Build(document)
            .ById.Values.Single(node => node.Kind == DocumentAccessibilityNodeKind.ContentControl);

        field.IsReadOnly.Should().Be(expectedReadOnly);
        field.HelpText.Should().Be(expectedReadOnly ? "Plain-text field, locked" : "Plain-text field");
    }

    [Fact]
    public void Build_groups_the_blocks_a_body_region_owns_and_reports_its_lock()
    {
        var region = new BlockContentControl(
            BlockContentControlKind.Group,
            Tag: "Terms",
            LockMode: ContentControlLockMode.ContentLocked);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Before"));
        document.Blocks.Add(new Paragraph("Inside one") { BlockContentControl = region });
        document.Blocks.Add(new Paragraph("Inside two") { BlockContentControl = region });
        document.Blocks.Add(new Paragraph("After"));

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        var regionNode = tree.Children.Should()
            .ContainSingle(node => node.Kind == DocumentAccessibilityNodeKind.ContentControl).Subject;
        regionNode.Name.Should().Be("Terms, document region");
        regionNode.HelpText.Should().Be("Document region, locked");
        regionNode.IsReadOnly.Should().BeTrue();
        regionNode.SemanticChildren.Select(child => child.Value)
            .Should().Equal(["Inside one", "Inside two"]);
        tree.Children.Select(node => node.Value)
            .Should().Contain([null, "Before", "After"], "blocks outside the region are untouched");
    }

    [Fact]
    public void Build_projects_fields_in_headers_and_table_cells_too()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Clear();
        var cellParagraph = new Paragraph();
        cellParagraph.Runs.Add(Run.PlainTextControl("Bob", alias: "Applicant"));
        cell.Paragraphs.Add(cellParagraph);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var header = new HeaderFooter();
        header.Paragraphs.Clear();
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(Run.PlainTextControl("Report", alias: "Title"));
        header.Paragraphs.Add(headerParagraph);
        document.FinalSectionHeadersFooters.Header = header;

        var fields = DocumentAccessibilityNodePlanner.Build(document)
            .ById.Values
            .Where(node => node.Kind == DocumentAccessibilityNodeKind.ContentControl)
            .Select(node => node.Name)
            .ToList();

        fields.Should().Contain("Applicant, plain-text field");
        fields.Should().Contain("Title, plain-text field",
            "a document-property field in a header is exactly where Word puts one");
    }

    [Fact]
    public void Build_keeps_a_hyperlinked_field_a_field()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var control = Run.PlainTextControl("Bob", alias: "Applicant");
        control.HyperlinkUrl = "https://example.test/";
        var paragraph = new Paragraph();
        paragraph.Runs.Add(control);
        document.Blocks.Add(paragraph);

        var tree = DocumentAccessibilityNodePlanner.Build(document);

        tree.ById.Values.Should().Contain(node => node.Kind == DocumentAccessibilityNodeKind.ContentControl);
        tree.ById.Values.Should().NotContain(
            node => node.Kind == DocumentAccessibilityNodeKind.Hyperlink,
            "the field is the meaningful control; its link is a run mark inside it");
    }
}
