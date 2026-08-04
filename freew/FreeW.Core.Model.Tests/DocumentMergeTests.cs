using System.Xml.Linq;

namespace FreeW.Core.Model.Tests;

public class DocumentMergeTests
{
    [Fact]
    public void Merge_TransfersPreservedNumberingWithCollisionSafeIds()
    {
        var wordprocessing = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var source = new TextDocument();
        source.Preserved.OriginalNumbering = Numbering(wordprocessing, 12, 12, "source");
        source.Styles["RawList"] = new DocumentStyle
        {
            Id = "RawList", Name = "Source raw list", PreservedNumbering = new PreservedNumbering(12, 2)
        };
        var sourceParagraph = new Paragraph("Source item")
        {
            StyleId = "RawList",
            PreservedNumbering = new PreservedNumbering(12, 1)
        };
        sourceParagraph.Runs.Add(Run.FootnoteReference(1));
        source.Blocks.Add(sourceParagraph);
        var sourceFootnote = new Footnote(1, "Source note");
        sourceFootnote.Content[0].PreservedNumbering = new PreservedNumbering(12, 0);
        source.Footnotes[1] = sourceFootnote;

        var target = new TextDocument();
        target.Preserved.OriginalNumbering = Numbering(wordprocessing, 12, 12, "target");
        target.Styles["RawList"] = new DocumentStyle { Id = "RawList", Name = "Target raw list" };
        target.Blocks.Add(new Paragraph("Target item") { PreservedNumbering = new PreservedNumbering(12, 0) });

        var inserted = DocumentMerge.Merge(target, target.Blocks.Count, source);

        inserted.Single().Should().BeOfType<Paragraph>().Which.PreservedNumbering.Should().Be(new PreservedNumbering(13, 1));
        inserted.Single().Should().BeOfType<Paragraph>().Which.StyleId.Should().Be("RawList_FreeW1");
        target.Styles["RawList_FreeW1"].PreservedNumbering.Should().Be(new PreservedNumbering(13, 2));
        target.Footnotes[1].Content.Single().PreservedNumbering.Should().Be(new PreservedNumbering(13, 0));
        target.Blocks[0].Should().BeOfType<Paragraph>().Which.PreservedNumbering.Should().Be(new PreservedNumbering(12, 0));
        target.Preserved.OriginalNumbering!.Elements(wordprocessing + "num")
            .Select(element => (string?)element.Attribute(wordprocessing + "numId"))
            .Should().Equal("12", "13");
        source.Blocks.Single().Should().BeOfType<Paragraph>().Which.PreservedNumbering.Should().Be(new PreservedNumbering(12, 1));
        source.Styles["RawList"].PreservedNumbering.Should().Be(new PreservedNumbering(12, 2));
    }

    [Fact]
    public void Merge_TransfersReferencedCitationSources_AndRemapsConflictingTags()
    {
        var imported = new Source
        {
            Tag = "Shared Source",
            Type = SourceType.JournalArticle,
            Author = "Ada Lovelace",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")],
            Editors = [SourceAuthorPerson.Create("Edna", string.Empty, "Editor")],
            Title = "Notes on the Analytical Engine",
            Year = "1843",
            Journal = "Scientific Memoirs",
            Volume = "3",
            Pages = "1-5"
        };
        var reusable = new Source
        {
            Tag = "Reusable",
            Author = "Grace Hopper",
            PersonalAuthors = [SourceAuthorPerson.Create("Grace", string.Empty, "Hopper")],
            Title = "Compiler work",
            Year = "1952"
        };
        var source = new TextDocument();
        source.Sources.Add(imported);
        source.Sources.Add(reusable);
        var sourceParagraph = new Paragraph();
        sourceParagraph.Runs.Add(Run.ComplexFieldRun(" CITATION \"Shared Source\" \\l 4 ", "[source]"));
        sourceParagraph.Runs.Add(Run.ComplexFieldRun(" CITATION Reusable ", "[reused]"));
        sourceParagraph.Runs.Add(Run.FootnoteReference(1));
        source.Blocks.Add(sourceParagraph);
        var sourceFootnote = new Footnote(1);
        sourceFootnote.Content.Add(new Paragraph());
        sourceFootnote.Content[0].Runs.Add(Run.ComplexFieldRun(" CITATION \"Shared Source\" ", "[source note]"));
        source.Footnotes[1] = sourceFootnote;

        var target = new TextDocument { BibliographyStyle = CitationStyle.Ieee };
        target.Sources.Add(new Source { Tag = "Shared Source", Author = "Different author", Title = "Different work", Year = "2026" });
        target.Sources.Add(reusable);

        var inserted = DocumentMerge.Merge(target, 0, source);

        target.Sources.Select(entry => entry.Tag).Should().Equal("Shared Source", "Reusable", "Shared Source_FreeW1");
        var copied = target.Sources[2];
        copied.Type.Should().Be(SourceType.JournalArticle);
        copied.PersonalAuthors.Should().Equal(SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace"));
        copied.Editors.Should().Equal(SourceAuthorPerson.Create("Edna", string.Empty, "Editor"));
        copied.Journal.Should().Be("Scientific Memoirs");
        copied.Pages.Should().Be("1-5");

        var bodyCitation = inserted.Single().Should().BeOfType<Paragraph>().Which.Runs[0];
        bodyCitation.ComplexField!.Instruction.Should().Be(" CITATION \"Shared Source_FreeW1\" \\l 4 ");
        ComplexFieldEngine.Argument(bodyCitation.ComplexField.Instruction).Should().Be("Shared Source_FreeW1");
        Citations.ResolveCitationField(target, bodyCitation.ComplexField, bodyCitation.Text).Should().Be("[3]");
        target.Footnotes[1].Content.Single().Runs.Single().ComplexField!.Instruction
            .Should().Be(" CITATION \"Shared Source_FreeW1\" ");

        source.Sources.Should().Equal(imported, reusable);
        sourceParagraph.Runs[0].ComplexField!.Instruction.Should().Be(" CITATION \"Shared Source\" \\l 4 ");
        sourceFootnote.Content[0].Runs.Single().ComplexField!.Instruction.Should().Be(" CITATION \"Shared Source\" ");
    }

    [Fact]
    public void Merge_TransfersSectionBreakHeadersAndStyles_WithoutAliasingTheSource()
    {
        var source = new TextDocument();
        source.Styles["HeaderStyle"] = new DocumentStyle
        {
            Id = "HeaderStyle", Name = "Source header", Run = new RunFormatting { Bold = true, ColorHex = "#0066AA" }
        };
        var section = new Section(new PageSettings
        {
            WidthPt = 792,
            HeightPt = 612,
            Landscape = true,
            MarginLeftPt = 54,
            MarginRightPt = 63,
            DifferentFirstPage = true
        }, SectionBreakKind.OddPage);
        section.HeadersFooters.Header = new HeaderFooter();
        section.HeadersFooters.Header.Paragraphs.Add(new Paragraph("Source header") { StyleId = "HeaderStyle" });
        section.HeadersFooters.FirstFooter = new HeaderFooter("Source first footer");
        source.Blocks.Add(new Paragraph("Section one end") { SectionBreak = section });
        source.Blocks.Add(new Paragraph("Section two body"));

        var target = new TextDocument();
        target.Styles["HeaderStyle"] = new DocumentStyle
        {
            Id = "HeaderStyle", Name = "Target header", Run = new RunFormatting { Italic = true, ColorHex = "#AA0000" }
        };

        var inserted = DocumentMerge.Merge(target, 0, source);

        var copiedSection = inserted[0].Should().BeOfType<Paragraph>().Which.SectionBreak!;
        copiedSection.Should().NotBeNull();
        copiedSection.Should().NotBeSameAs(section);
        copiedSection.Page.Should().NotBeSameAs(section.Page);
        copiedSection.BreakKind.Should().Be(SectionBreakKind.OddPage);
        copiedSection.Page.WidthPt.Should().Be(792);
        copiedSection.Page.Landscape.Should().BeTrue();
        copiedSection.Page.MarginRightPt.Should().Be(63);
        copiedSection.HeadersFooters.Header.Should().NotBeSameAs(section.HeadersFooters.Header);
        copiedSection.HeadersFooters.Header!.Paragraphs.Single().StyleId.Should().Be("HeaderStyle_FreeW1");
        copiedSection.HeadersFooters.FirstFooter!.Paragraphs.Single().PlainText.Should().Be("Source first footer");
        target.Styles["HeaderStyle_FreeW1"].Run.ColorHex.Should().Be("#0066AA");
        target.Styles["HeaderStyle"].Run.ColorHex.Should().Be("#AA0000");

        copiedSection.Page.MarginLeftPt = 108;
        copiedSection.HeadersFooters.Header.Paragraphs.Single().Runs.Single().Text = "Changed header";
        section.Page.MarginLeftPt.Should().Be(54);
        section.HeadersFooters.Header.Paragraphs.Single().PlainText.Should().Be("Source header");
    }

    [Fact]
    public void Merge_ClonesDrawingRunsAndRunMarks_AndTransfersShapeTextStyles()
    {
        var source = new TextDocument();
        source.Styles["ShapeText"] = new DocumentStyle
        {
            Id = "ShapeText", Name = "Source shape text", Run = new RunFormatting { Bold = true, ColorHex = "#0066AA" }
        };
        var shape = Shape.TextBoxWith("Shape text", 144, 72, "#4472C4");
        shape.TextParagraphs.Single().StyleId = "ShapeText";
        shape.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square, HorizontalOffsetPt = 18, ZOrderIndex = 5 };
        shape.ExtendedFill = ShapeFill.LinearGradient(5400000, new GradientStop(0, "#4472C4"), new GradientStop(100000, "#FFFFFF"));
        shape.Effects = new ShapeEffectLst { HasGlow = true, GlowRad = 64000 };
        shape.CustomGeometry = CustomGeometry.RectanglePoly();

        var group = new DrawingGroup
        {
            WidthPt = 180,
            HeightPt = 90,
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront, HorizontalOffsetPt = 36, ZOrderIndex = 8 }
        };
        var groupShape = Shape.Preset(ShapeKind.Ellipse, 36, 24, "#ED7D31");
        group.Children.Add(groupShape);
        group.Children.Add(WordArt.Create("Group label"));
        group.ChildOffsets.Add((0, 0));
        group.ChildOffsets.Add((54, 12));

        var sourceParagraph = new Paragraph();
        sourceParagraph.Runs.Add(Run.FromShape(shape));
        sourceParagraph.Runs.Add(Run.FromDrawingGroup(group));
        sourceParagraph.Runs.Add(Run.TableFormulaFieldRun(new TableFormulaField("=SUM(ABOVE)"), "12"));
        sourceParagraph.Runs.Add(Run.PageBreak());
        sourceParagraph.Runs.Add(new Run("Moved")
        {
            Revision = RevisionKind.Inserted,
            MoveRevisionId = 42,
            FormatRevision = new FormatRevision(RunFormatting.Default, "Reviewer", "2026-07-26T00:00:00Z")
        });
        sourceParagraph.ParagraphFormatRevision = new ParagraphFormatRevision(
            ParagraphFormatting.Default, "Reviewer", "2026-07-26T00:00:00Z");
        source.Blocks.Add(sourceParagraph);

        var target = new TextDocument();
        target.Styles["ShapeText"] = new DocumentStyle
        {
            Id = "ShapeText", Name = "Target shape text", Run = new RunFormatting { Italic = true, ColorHex = "#AA0000" }
        };

        var copied = DocumentMerge.Merge(target, 0, source).Single().Should().BeOfType<Paragraph>().Which;
        var copiedShape = copied.Runs[0].Shape!;
        var copiedGroup = copied.Runs[1].DrawingGroup!;

        copiedShape.Should().NotBeSameAs(shape);
        copiedShape.Placement.Should().NotBeSameAs(shape.Placement);
        copiedShape.ExtendedFill.Should().NotBeSameAs(shape.ExtendedFill);
        copiedShape.Effects.Should().NotBeSameAs(shape.Effects);
        copiedShape.CustomGeometry.Should().NotBeSameAs(shape.CustomGeometry);
        copiedShape.TextParagraphs.Single().StyleId.Should().Be("ShapeText_FreeW1");
        target.Styles["ShapeText_FreeW1"].Run.ColorHex.Should().Be("#0066AA");
        copiedGroup.Should().NotBeSameAs(group);
        copiedGroup.Placement.Should().NotBeSameAs(group.Placement);
        copiedGroup.Children[0].Should().BeOfType<Shape>().Which.Should().NotBeSameAs(groupShape);
        copiedGroup.ChildOffsets.Should().Equal((0d, 0d), (54d, 12d));
        copied.Runs[2].TableFormula.Should().Be(new TableFormulaField("=SUM(ABOVE)"));
        copied.Runs[3].IsPageBreak.Should().BeTrue();
        copied.Runs[4].MoveRevisionId.Should().Be(42);
        copied.Runs[4].FormatRevision.Should().Be(new FormatRevision(RunFormatting.Default, "Reviewer", "2026-07-26T00:00:00Z"));
        copied.ParagraphFormatRevision.Should().Be(new ParagraphFormatRevision(
            ParagraphFormatting.Default, "Reviewer", "2026-07-26T00:00:00Z"));

        copiedShape.Placement.HorizontalOffsetPt = 72;
        copiedShape.ExtendedFill.GradientStops[0] = new GradientStop(0, "#000000");
        copiedGroup.Placement.HorizontalOffsetPt = 72;
        ((Shape)copiedGroup.Children[0]).FillColorHex = "#000000";
        shape.Placement.HorizontalOffsetPt.Should().Be(18);
        shape.ExtendedFill.GradientStops[0].ColorHex.Should().Be("#4472C4");
        group.Placement.HorizontalOffsetPt.Should().Be(36);
        groupShape.FillColorHex.Should().Be("#ED7D31");
    }

    [Fact]
    public void Merge_TransfersConflictingStyleClosureWithoutOverwritingTargetStyles()
    {
        var source = new TextDocument();
        source.Styles["Base"] = new DocumentStyle
        {
            Id = "Base", Name = "Source base", Run = new RunFormatting { ColorHex = "#AA0000" }
        };
        source.Styles["Follow"] = new DocumentStyle
        {
            Id = "Follow", Name = "Source follow", Paragraph = new ParagraphFormatting { SpaceAfterPt = 24 }
        };
        source.Styles["SourceStyle"] = new DocumentStyle
        {
            Id = "SourceStyle", Name = "Source style", BasedOnStyleId = "Base", NextStyleId = "Follow",
            Run = new RunFormatting { Bold = true }
        };
        source.Styles["TableSource"] = new DocumentStyle
        {
            Id = "TableSource", Name = "Source table", Type = StyleType.Table, TableBorders = true,
            PreservedTableStyleXml = "<w:style xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" w:type=\"table\" w:styleId=\"TableSource\"><w:name w:val=\"Source table\"/><w:tblStylePr w:type=\"firstRow\"/></w:style>"
        };
        source.Blocks.Add(new Paragraph("Source paragraph") { StyleId = "SourceStyle" });
        var sourceTable = Table.Create(1, 1);
        sourceTable.TableStyleId = "TableSource";
        sourceTable.PreferredWidthPt = 360;
        sourceTable.Alignment = TableAlignment.Center;
        sourceTable.FloatingPosition = new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Page,
            VerticalAnchor: TableVerticalAnchor.Margin,
            HorizontalOffsetPt: -18,
            VerticalAlignment: TableVerticalPositionAlignment.Bottom,
            LeftFromTextPt: 4.5);
        sourceTable.FloatingTableAllowsOverlap = false;
        sourceTable.DefaultCellMargins = new TableCellMargins(1, 2, 3, 4);
        sourceTable.CellSpacingPt = 2;
        sourceTable.AutoFit = AutoFitMode.Contents;
        sourceTable.Rows[0].HeightPt = 42;
        sourceTable.Rows[0].HeightRule = TableRowHeightRule.Exact;
        sourceTable.Rows[0].AllowBreakAcrossPages = false;
        sourceTable.Rows[0].Cells[0].VerticalAlignment = TableCellVerticalAlignment.Center;
        sourceTable.Rows[0].Cells[0].Margins = new TableCellMargins(5, 6, 7, 8);
        sourceTable.Rows[0].Cells[0].TextDirection = CellTextDirection.Rotate90;
        sourceTable.Rows[0].Cells[0].WrapText = false;
        sourceTable.Rows[0].Cells[0].FitText = true;
        source.Blocks.Add(sourceTable);

        var target = new TextDocument();
        target.Styles["Base"] = new DocumentStyle { Id = "Base", Name = "Target base", Run = new RunFormatting { ColorHex = "#0000AA" } };
        target.Styles["Follow"] = new DocumentStyle { Id = "Follow", Name = "Target follow" };
        target.Styles["SourceStyle"] = new DocumentStyle { Id = "SourceStyle", Name = "Target paragraph" };
        target.Styles["TableSource"] = new DocumentStyle { Id = "TableSource", Name = "Target table", Type = StyleType.Table };

        var inserted = DocumentMerge.Merge(target, 0, source);

        inserted[0].Should().BeOfType<Paragraph>().Which.StyleId.Should().Be("SourceStyle_FreeW1");
        var insertedTable = inserted[1].Should().BeOfType<Table>().Subject;
        insertedTable.TableStyleId.Should().Be("TableSource_FreeW1");
        insertedTable.PreferredWidthPt.Should().Be(360);
        insertedTable.Alignment.Should().Be(TableAlignment.Center);
        insertedTable.TextWrapping.Should().BeTrue();
        insertedTable.FloatingPosition.Should().Be(sourceTable.FloatingPosition);
        insertedTable.FloatingTableAllowsOverlap.Should().BeFalse();
        insertedTable.DefaultCellMargins.Should().Be(new TableCellMargins(1, 2, 3, 4));
        insertedTable.CellSpacingPt.Should().Be(2);
        insertedTable.AutoFit.Should().Be(AutoFitMode.Contents);
        insertedTable.Rows[0].HeightPt.Should().Be(42);
        insertedTable.Rows[0].HeightRule.Should().Be(TableRowHeightRule.Exact);
        insertedTable.Rows[0].AllowBreakAcrossPages.Should().BeFalse();
        insertedTable.Rows[0].Cells[0].VerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        insertedTable.Rows[0].Cells[0].Margins.Should().Be(new TableCellMargins(5, 6, 7, 8));
        insertedTable.Rows[0].Cells[0].TextDirection.Should().Be(CellTextDirection.Rotate90);
        insertedTable.Rows[0].Cells[0].WrapText.Should().BeFalse();
        insertedTable.Rows[0].Cells[0].FitText.Should().BeTrue();
        target.Styles["SourceStyle_FreeW1"].BasedOnStyleId.Should().Be("Base_FreeW1");
        target.Styles["SourceStyle_FreeW1"].NextStyleId.Should().Be("Follow_FreeW1");
        target.Styles["Base_FreeW1"].Run.ColorHex.Should().Be("#AA0000");
        target.Styles["TableSource_FreeW1"].TableBorders.Should().BeTrue();
        target.Styles["TableSource_FreeW1"].PreservedTableStyleXml
            .Should().Contain("w:styleId=\"TableSource_FreeW1\"").And.Contain("w:type=\"firstRow\"");
        target.Styles["SourceStyle"].Name.Should().Be("Target paragraph");
        source.Styles["SourceStyle"].BasedOnStyleId.Should().Be("Base");
    }

    [Fact]
    public void Merge_RemapsCollidingBookmarksAndTheirInternalReferences()
    {
        var source = new TextDocument();
        var sourceParagraph = new Paragraph("Source target");
        sourceParagraph.BookmarkNames.Add("Shared");
        sourceParagraph.BookmarkNames.Add("SourceOnly");
        sourceParagraph.BookmarkBoundaries.Add(new BookmarkBoundary("5", BookmarkBoundaryKind.Start, 0, "Shared"));
        sourceParagraph.BookmarkBoundaries.Add(new BookmarkBoundary("5", BookmarkBoundaryKind.End, 2));
        sourceParagraph.Runs.Add(new Run("jump") { HyperlinkAnchor = "Shared" });
        sourceParagraph.Runs.Add(Run.CrossReferenceFieldRun(
            new CrossReferenceField(CrossRefFieldKind.Ref, "Shared", CrossRefInsertAs.Text, Hyperlink: true),
            "Source target"));
        sourceParagraph.Runs.Add(Run.FootnoteReference(1));
        source.Blocks.Add(sourceParagraph);
        var footnote = new Footnote(1, "Source note");
        footnote.Content[0].Runs.Add(new Run("jump from note") { HyperlinkAnchor = "Shared" });
        source.Footnotes[1] = footnote;

        var target = new TextDocument();
        var targetParagraph = new Paragraph("Target one");
        targetParagraph.BookmarkNames.Add("Shared");
        target.Blocks.Add(targetParagraph);
        target.Blocks.Add(new Paragraph("Target two") { BookmarkName = "Shared_FreeW1" });

        var inserted = DocumentMerge.Merge(target, target.Blocks.Count, source);

        var merged = inserted.Single().Should().BeOfType<Paragraph>().Subject;
        merged.BookmarkNames.Should().Equal("Shared_FreeW2", "SourceOnly");
        merged.BookmarkBoundaries.Should().Equal(
            new BookmarkBoundary("5", BookmarkBoundaryKind.Start, 0, "Shared_FreeW2"),
            new BookmarkBoundary("5", BookmarkBoundaryKind.End, 2));
        merged.Runs.Single(run => run.Text == "jump").HyperlinkAnchor.Should().Be("Shared_FreeW2");
        merged.Runs.Single(run => run.CrossReference is not null).CrossReference!.Target.Should().Be("Shared_FreeW2");
        target.Footnotes[1].Content.Single().Runs.Last().HyperlinkAnchor.Should().Be("Shared_FreeW2");
        target.Blocks[0].Should().BeOfType<Paragraph>().Which.BookmarkName.Should().Be("Shared");
        sourceParagraph.BookmarkNames.Should().Equal("Shared", "SourceOnly");
        sourceParagraph.BookmarkBoundaries[0].Name.Should().Be("Shared");
        sourceParagraph.Runs.Single(run => run.Text == "jump").HyperlinkAnchor.Should().Be("Shared");
    }

    [Fact]
    public void Merge_TransfersAndRemapsReferencedNotesAndCommentThreads()
    {
        var source = new TextDocument();
        var sourceParagraph = new Paragraph();
        sourceParagraph.Runs.Add(Run.FootnoteReference(1));
        sourceParagraph.Runs.Add(Run.EndnoteReference(1));
        sourceParagraph.Runs.Add(new Run("Commented") { CommentId = 0 });
        sourceParagraph.Runs.Add(Run.CommentReference(0));
        source.Blocks.Add(sourceParagraph);
        var footnote = new Footnote(1, "Source footnote");
        footnote.Content[0].Runs.Add(Run.EndnoteReference(1));
        source.Footnotes[1] = footnote;
        source.Endnotes[1] = new Endnote(1, "Source endnote");
        var comment = new Comment(0, "Source comment", "Ada", "A") { Resolved = true };
        comment.AddReply(1, "Reply", "Ben", "B");
        source.Comments[0] = comment;

        var target = new TextDocument();
        target.Footnotes[1] = new Footnote(1, "Target footnote");
        target.Endnotes[1] = new Endnote(1, "Target endnote");
        var targetComment = new Comment(0, "Target comment");
        targetComment.AddReply(1, "Target reply");
        target.Comments[0] = targetComment;

        var inserted = DocumentMerge.Merge(target, 0, source);

        var runs = inserted.Single().Should().BeOfType<Paragraph>().Subject.Runs;
        runs[0].FootnoteId.Should().Be(2);
        runs[1].EndnoteId.Should().Be(2);
        runs[2].CommentId.Should().Be(2);
        runs[3].CommentId.Should().Be(2);
        target.Footnotes[2].PlainText.Should().Be("Source footnote1");
        target.Footnotes[2].Content.Single().Runs.Last().EndnoteId.Should().Be(2);
        target.Endnotes[2].PlainText.Should().Be("Source endnote");
        target.Comments[2].PlainText.Should().Be("Source comment");
        target.Comments[2].Replies.Single().Id.Should().Be(3);
        target.Comments[2].Resolved.Should().BeTrue();
        source.Footnotes[1].PlainText.Should().Be("Source footnote1");
        source.Comments[0].Replies.Single().Id.Should().Be(1);
    }

    [Fact]
    public void Merge_TransfersAltChunkPackageGraph_WithCollisionSafeRelationshipRewrite()
    {
        const string altChunkRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/aFChunk";
        var source = new TextDocument();
        source.Preserved.Parts.Add(new PreservedPart("/word/afchunk.docx", [1], RelationshipType: altChunkRel));
        source.Preserved.Parts.Add(new PreservedPart(
            "/word/_rels/afchunk.docx.rels",
            System.Text.Encoding.UTF8.GetBytes("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"image\" Target=\"media/altchunk.png\" /></Relationships>")));
        source.Preserved.Parts.Add(new PreservedPart("/word/media/altchunk.png", [2]));
        source.Preserved.ContentTypeDefaults["png"] = "image/png";
        source.Blocks.Add(new AltChunkBlock("/word/afchunk.docx"));

        var target = new TextDocument();
        target.Preserved.Parts.Add(new PreservedPart("/word/afchunk.docx", [9], RelationshipType: altChunkRel));
        target.Preserved.Parts.Add(new PreservedPart("/word/_rels/afchunk.docx.rels", [8]));
        target.Preserved.Parts.Add(new PreservedPart("/word/media/altchunk.png", [7]));

        var inserted = DocumentMerge.Merge(target, 0, source);

        var copiedAltChunk = inserted.Single().Should().BeOfType<AltChunkBlock>().Subject;
        copiedAltChunk.Should().NotBeSameAs(source.Blocks.Single());
        copiedAltChunk.PreservedPartName.Should().Be("/word/afchunk-freew-import1.docx");
        target.Preserved.Parts.Should().Contain(part => part.PartName == "/word/afchunk-freew-import1.docx");
        target.Preserved.Parts.Should().Contain(part => part.PartName == "/word/_rels/afchunk-freew-import1.docx.rels");
        target.Preserved.Parts.Should().Contain(part => part.PartName == "/word/media/altchunk-freew-import1.png");
        var copiedRels = target.Preserved.Parts.Single(part => part.PartName == "/word/_rels/afchunk-freew-import1.docx.rels");
        System.Text.Encoding.UTF8.GetString(copiedRels.Bytes)
            .Should().Contain("Target=\"media/altchunk-freew-import1.png\"");
        source.Blocks.Single().Should().BeOfType<AltChunkBlock>().Which.PreservedPartName.Should().Be("/word/afchunk.docx");
    }

    [Fact]
    public void Merge_TransfersPreservedDrawingPackageGraph_WithCollisionSafeRelationshipRewrite()
    {
        const string chartRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        var source = new TextDocument();
        source.Preserved.Parts.Add(new PreservedPart("/word/charts/chart1.xml", [1], RelationshipType: chartRel));
        source.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/_rels/chart1.xml.rels",
            System.Text.Encoding.UTF8.GetBytes("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"image\" Target=\"../media/image1.png\" /></Relationships>")));
        source.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", [2]));
        source.Preserved.ContentTypeDefaults["png"] = "image/png";
        var sourceParagraph = new Paragraph();
        sourceParagraph.Runs.Add(Run.FromPreservedDrawing(new PreservedDrawing(
            "<w:drawing />",
            [new PreservedDrawingReference("rId7", "/word/charts/chart1.xml", chartRel)])));
        source.Blocks.Add(sourceParagraph);

        var target = new TextDocument();
        target.Preserved.Parts.Add(new PreservedPart("/word/charts/chart1.xml", [9], RelationshipType: chartRel));
        target.Preserved.Parts.Add(new PreservedPart("/word/charts/_rels/chart1.xml.rels", [8]));
        target.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", [7]));

        var inserted = DocumentMerge.Merge(target, 0, source);

        var copiedDrawing = inserted.Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().PreservedDrawing!;
        copiedDrawing.References.Single().PreservedPartName.Should().Be("/word/charts/chart1-freew-import1.xml");
        target.Preserved.Parts.Should().Contain(part => part.PartName == "/word/charts/chart1-freew-import1.xml");
        target.Preserved.Parts.Should().Contain(part => part.PartName == "/word/charts/_rels/chart1-freew-import1.xml.rels");
        target.Preserved.Parts.Should().Contain(part => part.PartName == "/word/media/image1-freew-import1.png");
        var copiedRels = target.Preserved.Parts.Single(part => part.PartName == "/word/charts/_rels/chart1-freew-import1.xml.rels");
        System.Text.Encoding.UTF8.GetString(copiedRels.Bytes)
            .Should().Contain("Target=\"../media/image1-freew-import1.png\"");
        target.Preserved.ContentTypeDefaults.Should().Contain("png", "image/png");
        source.Preserved.Parts.Should().HaveCount(3);
    }

    [Fact]
    public void CloneBlocks_CopiesTextAndFormatting_AndLeavesSourceUntouched()
    {
        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bold bit", new RunFormatting
        {
            Bold = true,
            DoubleStrikethrough = true,
            NoProof = true,
            Hidden = true,
            WebHidden = true,
            ColorHex = "#FF0000"
        }));
        paragraph.Runs.Add(new Run(" plain"));
        paragraph.StyleId = "Heading1";
        source.Blocks.Add(paragraph);

        var clones = DocumentMerge.CloneBlocks(source);

        var clonedParagraph = clones.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Subject;
        clonedParagraph.PlainText.Should().Be("Bold bit plain");
        clonedParagraph.StyleId.Should().Be("Heading1");
        clonedParagraph.Runs[0].Formatting.Bold.Should().BeTrue();
        clonedParagraph.Runs[0].Formatting.DoubleStrikethrough.Should().BeTrue();
        clonedParagraph.Runs[0].Formatting.NoProof.Should().BeTrue();
        clonedParagraph.Runs[0].Formatting.Hidden.Should().BeTrue();
        clonedParagraph.Runs[0].Formatting.WebHidden.Should().BeTrue();
        clonedParagraph.Runs[0].Formatting.ColorHex.Should().Be("#FF0000");

        // The clone is independent: mutating it must not touch the source.
        clonedParagraph.Runs[0].Text = "Changed";
        source.Blocks.OfType<Paragraph>().Single().Runs[0].Text.Should().Be("Bold bit");
        ReferenceEquals(clonedParagraph.Runs[0], paragraph.Runs[0]).Should().BeFalse();
        ReferenceEquals(clonedParagraph, paragraph).Should().BeFalse();
    }

    [Fact]
    public void CloneBlocks_PreservesSharedBlockContentControlRegion()
    {
        var control = BlockContentControl.BibliographyRegion();
        var source = new TextDocument();
        source.Blocks.Add(new Paragraph("References") { BlockContentControl = control });
        source.Blocks.Add(new Paragraph("Entry") { BlockContentControl = control });

        var clones = DocumentMerge.CloneBlocks(source);

        clones.Should().HaveCount(2);
        clones[0].BlockContentControl.Should().Be(control);
        ReferenceEquals(clones[1].BlockContentControl, clones[0].BlockContentControl).Should().BeTrue();
    }

    [Fact]
    public void CloneBlocks_DeepCopiesTables()
    {
        var source = new TextDocument();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("A1"));
        table.Rows[0].Cells[0].ShadingColorHex = "#00FF00";
        source.Blocks.Add(table);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Table>().Subject;

        clone.Rows[0].Cells[0].PlainText.Should().Be("A1");
        clone.Rows[0].Cells[0].ShadingColorHex.Should().Be("#00FF00");

        // Independence: editing the cloned cell does not change the source table.
        clone.Rows[0].Cells[0].Paragraphs[0].Runs[0].Text = "Z";
        table.Rows[0].Cells[0].PlainText.Should().Be("A1");
        ReferenceEquals(clone.Rows[0].Cells[0], table.Rows[0].Cells[0]).Should().BeFalse();
    }

    [Fact]
    public void CloneBlocks_PreservesRichImageState_WithoutSharingTheImageModel()
    {
        var image = new InlineImage([1, 2, 3], 144, 72)
        {
            AltText = "Cropped floating image",
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 18,
            VerticalOffsetPt = 12,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Page,
            ZOrderIndex = 7,
            RotationAngle = 15,
            FlipH = true,
            CropLeft = 0.1,
            CropBottom = 0.2,
            BorderColorHex = "4472C4",
            BorderWidthPt = 2,
            BrightnessPct = 20,
            ContrastPct = -15,
            SaturationPct = 80,
            TransparencyPct = 10,
            RecolorMode = ImageRecolorMode.Sepia,
            ColorTemperature = 25,
            ShadowPreset = 2,
            GlowSizePt = 4,
            GlowColorHex = "70AD47",
            ReflectionPreset = 1,
            SoftEdgePt = 3,
            ArtisticEffect = ImageArtisticEffect.GlowDiffused,
            PictureStylePreset = 8
        };
        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(string.Empty) { Image = image });
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().Image!;

        clone.Should().NotBeSameAs(image);
        clone.Bytes.Should().BeSameAs(image.Bytes);
        clone.AltText.Should().Be("Cropped floating image");
        clone.Wrapping.Should().Be(ImageWrapping.Square);
        clone.HorizontalOffsetPt.Should().Be(18);
        clone.VerticalOffsetPt.Should().Be(12);
        clone.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        clone.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        clone.ZOrderIndex.Should().Be(7);
        clone.RotationAngle.Should().Be(15);
        clone.FlipH.Should().BeTrue();
        clone.CropLeft.Should().Be(0.1);
        clone.CropBottom.Should().Be(0.2);
        clone.BorderColorHex.Should().Be("4472C4");
        clone.BrightnessPct.Should().Be(20);
        clone.ContrastPct.Should().Be(-15);
        clone.SaturationPct.Should().Be(80);
        clone.TransparencyPct.Should().Be(10);
        clone.RecolorMode.Should().Be(ImageRecolorMode.Sepia);
        clone.ColorTemperature.Should().Be(25);
        clone.ShadowPreset.Should().Be(2);
        clone.GlowSizePt.Should().Be(4);
        clone.ReflectionPreset.Should().Be(1);
        clone.SoftEdgePt.Should().Be(3);
        clone.ArtisticEffect.Should().Be(ImageArtisticEffect.GlowDiffused);
        clone.PictureStylePreset.Should().Be(8);

        clone.WidthPt = 100;
        image.WidthPt.Should().Be(144);
    }

    [Fact]
    public void CloneBlocks_PreservesFloatingWordArt_WithoutSharingItsPlacement()
    {
        var wordArt = new WordArt("Merged WordArt", WordArtStyle.GlowGold, 32)
        {
            FontFamily = "Aptos Display",
            Bold = true,
            WidthPt = 220,
            HeightPt = 48,
            RotationAngle = 12,
            FlipH = true,
            AltText = "Decorative merged heading",
            Warp = WordArtWarp.Wave2,
            TextFitMode = WordArtTextFitMode.NormalAutoFit,
            NormalAutoFitFontScale = 85000,
            NormalAutoFitLineSpacingReduction = 12000,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 24,
                VerticalOffsetPt = 18,
                HorizontalAnchor = HorizontalAnchor.Margin,
                VerticalAnchor = VerticalAnchor.Page,
                ZOrderIndex = 5
            }
        };
        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromWordArt(wordArt));
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().WordArt!;

        clone.Should().NotBeSameAs(wordArt);
        clone.Text.Should().Be("Merged WordArt");
        clone.Style.Should().Be(WordArtStyle.GlowGold);
        clone.FontFamily.Should().Be("Aptos Display");
        clone.Bold.Should().BeTrue();
        clone.WidthPt.Should().Be(220);
        clone.HeightPt.Should().Be(48);
        clone.RotationAngle.Should().Be(12);
        clone.FlipH.Should().BeTrue();
        clone.AltText.Should().Be("Decorative merged heading");
        clone.Warp.Should().Be(WordArtWarp.Wave2);
        clone.TextFitMode.Should().Be(WordArtTextFitMode.NormalAutoFit);
        clone.NormalAutoFitFontScale.Should().Be(85000);
        clone.NormalAutoFitLineSpacingReduction.Should().Be(12000);
        clone.Placement.Should().NotBeSameAs(wordArt.Placement);
        clone.Placement!.Wrapping.Should().Be(ImageWrapping.InFront);
        clone.Placement.HorizontalOffsetPt.Should().Be(24);
        clone.Placement.VerticalOffsetPt.Should().Be(18);
        clone.Placement.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        clone.Placement.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        clone.Placement.ZOrderIndex.Should().Be(5);

        clone.Placement.HorizontalOffsetPt = 0;
        wordArt.Placement!.HorizontalOffsetPt.Should().Be(24);
    }

    [Fact]
    public void CloneBlocks_PreservesSmartArtHierarchy_WithoutSharingNodesOrPlacement()
    {
        var smartArt = new SmartArt
        {
            Kind = SmartArtKind.Hierarchy,
            WidthPt = 360,
            HeightPt = 180,
            LayoutId = "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3",
            ColorSchemeId = "accent1_2",
            StyleId = "simple1",
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 20,
                HorizontalAnchor = HorizontalAnchor.Margin,
                VerticalAnchor = VerticalAnchor.Page,
                ZOrderIndex = 4
            }
        };
        var root = new SmartArtNode("Chief");
        root.AddChild("Operations").AddChild("Field");
        smartArt.Nodes.Add(root);

        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromSmartArt(smartArt));
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().SmartArt!;

        clone.Should().NotBeSameAs(smartArt);
        clone.Kind.Should().Be(SmartArtKind.Hierarchy);
        clone.WidthPt.Should().Be(360);
        clone.HeightPt.Should().Be(180);
        clone.LayoutId.Should().Be("urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3");
        clone.ColorSchemeId.Should().Be("accent1_2");
        clone.StyleId.Should().Be("simple1");
        clone.Placement.Should().NotBeSameAs(smartArt.Placement);
        clone.Placement!.ZOrderIndex.Should().Be(4);
        clone.Nodes.Single().Should().NotBeSameAs(root);
        clone.Nodes.Single().Text.Should().Be("Chief");
        clone.Nodes.Single().Children.Single().Text.Should().Be("Operations");
        clone.Nodes.Single().Children.Single().Children.Single().Text.Should().Be("Field");

        clone.Nodes.Single().Children.Single().Text = "Changed";
        root.Children.Single().Text.Should().Be("Operations");
    }

    [Fact]
    public void CloneBlocks_PreservesFloatingChart_WithoutSharingDataOrPlacement()
    {
        var chart = new Chart
        {
            Kind = ChartKind.Line,
            Title = "Merged chart",
            ShowLegend = true,
            CategoryAxisTitle = "Month",
            ValueAxisTitle = "Revenue",
            WidthPt = 360,
            HeightPt = 216,
            StyleId = 6,
            ColorSchemeId = "mono-blue",
            QuickLayoutId = 4,
            NativeVisualSettings = new ChartNativeVisualSettings(
                ShowGridlines: true,
                HasPlotAreaFill: true,
                ShowDataLabels: false,
                ScatterConnectsPoints: false),
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 20,
                HorizontalAnchor = HorizontalAnchor.Margin,
                VerticalAnchor = VerticalAnchor.Page,
                ZOrderIndex = 4
            }
        };
        chart.Categories.AddRange(["Jan", "Feb"]);
        chart.Series.Add(new ChartSeries("Actual", [10, 20]));
        chart.Series.Add(new ChartSeries("Plan", [12, 24]));

        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().Chart!;

        clone.Should().NotBeSameAs(chart);
        clone.Kind.Should().Be(ChartKind.Line);
        clone.Title.Should().Be("Merged chart");
        clone.ShowLegend.Should().BeTrue();
        clone.CategoryAxisTitle.Should().Be("Month");
        clone.ValueAxisTitle.Should().Be("Revenue");
        clone.WidthPt.Should().Be(360);
        clone.HeightPt.Should().Be(216);
        clone.StyleId.Should().Be(6);
        clone.ColorSchemeId.Should().Be("mono-blue");
        clone.QuickLayoutId.Should().Be(4);
        clone.NativeVisualSettings.Should().Be(chart.NativeVisualSettings);
        clone.Placement.Should().NotBeSameAs(chart.Placement);
        clone.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
        clone.Placement.HorizontalOffsetPt.Should().Be(36);
        clone.Placement.VerticalOffsetPt.Should().Be(20);
        clone.Placement.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        clone.Placement.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        clone.Placement.ZOrderIndex.Should().Be(4);
        clone.Categories.Should().Equal("Jan", "Feb");
        clone.Series.Should().HaveCount(2);
        clone.Series[0].Should().NotBeSameAs(chart.Series[0]);
        clone.Series[0].Name.Should().Be("Actual");
        clone.Series[0].Values.Should().Equal(10, 20);
        clone.Series[1].Name.Should().Be("Plan");
        clone.Series[1].Values.Should().Equal(12, 24);

        clone.Categories[0] = "Changed";
        clone.Series[0].Values[0] = 99;
        clone.Placement.HorizontalOffsetPt = 0;
        chart.Categories[0].Should().Be("Jan");
        chart.Series[0].Values[0].Should().Be(10);
        chart.Placement!.HorizontalOffsetPt.Should().Be(36);
    }

    [Fact]
    public void CloneBlocks_PreservesSemanticInlinePayloads_WithoutSharingMutableState()
    {
        var numerator = new Equation([MathRun.Superscript("x", "2")]);
        var denominator = new Equation([MathRun.Subscript("y", "1")]);
        var equation = new Equation([MathRun.Fraction(numerator, denominator)]);
        var embedded = new EmbeddedObject([1, 2, 3], "Excel.Sheet.12")
        {
            Icon = new InlineImage([4, 5, 6], 48, 36) { AltText = "Workbook" },
            WidthPt = 72,
            HeightPt = 54
        };
        var ruby = new RubyAnnotation
        {
            Alignment = RubyAlignment.DistributeSpace,
            PhoneticSizeHalfPoints = 12,
            RaiseHalfPoints = 9
        };
        ruby.BaseFragments.Add(new RubyTextFragment("漢字", new RunFormatting { Bold = true }));
        ruby.PhoneticFragments.Add(new RubyTextFragment("かんじ", new RunFormatting { FontSizePt = 6 }));
        var references = new List<PreservedDrawingReference>
        {
            new("rId7", "/word/charts/chart42.xml", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart")
        };
        var drawing = new PreservedDrawing("<w:drawing />", references);

        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        paragraph.Runs.Add(Run.FromEmbeddedObject(embedded));
        paragraph.Runs.Add(Run.FromRuby(ruby));
        paragraph.Runs.Add(Run.FromPreservedDrawing(drawing));
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs;
        var clonedEquation = clone[0].Equation!;
        var clonedEmbedded = clone[1].EmbeddedObject!;
        var clonedRuby = clone[2].Ruby!;
        var clonedDrawing = clone[3].PreservedDrawing!;

        clonedEquation.Should().NotBeSameAs(equation);
        clonedEquation.LinearText.Should().Be("x^2/y_1");
        clonedEquation.Runs.Single().NumeratorEquation.Should().NotBeSameAs(numerator);
        clonedEquation.Runs.Single().DenominatorEquation.Should().NotBeSameAs(denominator);
        clonedEmbedded.Should().NotBeSameAs(embedded);
        clonedEmbedded.Payload.Should().Equal([1, 2, 3]);
        clonedEmbedded.ProgId.Should().Be("Excel.Sheet.12");
        clonedEmbedded.Icon.Should().NotBeSameAs(embedded.Icon);
        clonedEmbedded.Icon!.AltText.Should().Be("Workbook");
        clonedEmbedded.WidthPt.Should().Be(72);
        clonedEmbedded.HeightPt.Should().Be(54);
        clonedRuby.Should().NotBeSameAs(ruby);
        clonedRuby.Alignment.Should().Be(RubyAlignment.DistributeSpace);
        clonedRuby.PhoneticSizeHalfPoints.Should().Be(12);
        clonedRuby.RaiseHalfPoints.Should().Be(9);
        clonedRuby.BaseFragments.Should().Equal(ruby.BaseFragments);
        clonedRuby.PhoneticFragments.Should().Equal(ruby.PhoneticFragments);
        clonedDrawing.Should().NotBeSameAs(drawing);
        clonedDrawing.Xml.Should().Be("<w:drawing />");
        clonedDrawing.References.Should().Equal(drawing.References);
        clonedDrawing.References.Should().NotBeSameAs(drawing.References);

        clonedEquation.Runs.Single().NumeratorEquation!.Runs.Add(MathRun.PlainText("+1"));
        clonedEmbedded.Payload[0] = 9;
        clonedRuby.BaseFragments[0] = new RubyTextFragment("変更", new RunFormatting());
        references.Add(new PreservedDrawingReference("rId8", "/word/charts/chart43.xml"));
        numerator.LinearText.Should().Be("x^2");
        embedded.Payload[0].Should().Be(1);
        ruby.BaseText.Should().Be("漢字");
        clonedDrawing.References.Should().ContainSingle();
    }

    [Fact]
    public void Merge_AppendsSourceBlocks_WithTextIntact_AndSourceUnchanged()
    {
        var target = new TextDocument();
        target.Blocks.Add(new Paragraph("Target one"));
        target.Blocks.Add(new Paragraph("Target two"));

        var source = new TextDocument();
        source.Blocks.Add(new Paragraph("Source one"));
        source.Blocks.Add(new Paragraph("Source two"));

        DocumentMerge.Merge(target, target.Blocks.Count, source);

        target.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Target one", "Target two", "Source one", "Source two");

        // Source is untouched (still two blocks, same text, and not aliased into the target).
        source.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Source one", "Source two");
        target.Blocks.Should().NotContain(source.Blocks[0]);
    }

    [Fact]
    public void InsertBlocksAt_PlacesBlocksAtTheGivenIndex()
    {
        var target = new TextDocument();
        target.Blocks.Add(new Paragraph("First"));
        target.Blocks.Add(new Paragraph("Last"));

        var source = new TextDocument();
        source.Blocks.Add(new Paragraph("Inserted A"));
        source.Blocks.Add(new Paragraph("Inserted B"));

        DocumentMerge.Merge(target, 1, source);

        target.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("First", "Inserted A", "Inserted B", "Last");
    }

    [Fact]
    public void InsertBlocksAt_ClampsOutOfRangeIndexToTheBodyEnd()
    {
        var target = new TextDocument();
        target.Blocks.Add(new Paragraph("Only"));

        DocumentMerge.InsertBlocksAt(target, 999, new[] { new Paragraph("Appended") });

        target.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Only", "Appended");
    }

    private static XElement Numbering(XNamespace wordprocessing, int abstractId, int numberId, string label) =>
        new(wordprocessing + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", wordprocessing.NamespaceName),
            new XElement(wordprocessing + "abstractNum",
                new XAttribute(wordprocessing + "abstractNumId", abstractId),
                new XElement(wordprocessing + "multiLevelType", new XAttribute(wordprocessing + "val", label))),
            new XElement(wordprocessing + "num",
                new XAttribute(wordprocessing + "numId", numberId),
                new XElement(wordprocessing + "abstractNumId", new XAttribute(wordprocessing + "val", abstractId))));
}
