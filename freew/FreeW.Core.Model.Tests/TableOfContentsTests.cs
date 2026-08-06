namespace FreeW.Core.Model.Tests;

public class TableOfContentsTests
{
    [Fact]
    public void Build_EmptyDocument_YieldsOnlyTheHeadingParagraph()
    {
        var doc = new TextDocument();

        var toc = TableOfContents.Build(doc);

        toc.Should().ContainSingle();
        toc[0].PlainText.Should().Be(TableOfContents.HeadingText);
        toc[0].StyleId.Should().Be(TableOfContents.HeadingStyleId);
    }

    [Fact]
    public void Build_NoHeadings_YieldsOnlyTheHeadingParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Blocks.Add(new Paragraph("More body") { StyleId = "Normal" });

        var toc = TableOfContents.Build(doc);

        toc.Should().ContainSingle()
            .Which.StyleId.Should().Be(TableOfContents.HeadingStyleId);
    }

    [Fact]
    public void Build_TitleAndHeadings_YieldsHeadingThenEntriesInOrderWithLevelIndent()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("My Title") { StyleId = "Title" });        // level 0
        doc.Blocks.Add(new Paragraph("Intro body"));                            // excluded
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });  // level 1
        doc.Blocks.Add(new Paragraph("Section A") { StyleId = "Heading2" });    // level 2
        doc.Blocks.Add(new Paragraph("Detail") { StyleId = "Heading3" });       // level 3
        doc.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });  // level 1

        var toc = TableOfContents.Build(doc);

        // Heading + one paragraph per outline entry, in document order.
        toc.Select(p => p.PlainText).Should().Equal(
            TableOfContents.HeadingText,
            "My Title\t1",
            "Chapter One\t1",
            "Section A\t1",
            "Detail\t1",
            "Chapter Two\t1");

        // The heading uses the TOC heading style; entries use TOC{level} (clamped at MaxStyledLevel).
        toc.Select(p => p.StyleId).Should().Equal(
            TableOfContents.HeadingStyleId,
            "TOC1",
            "TOC1",
            "TOC2",
            "TOC3",
            "TOC1");

        // Left indent is level * IndentPerLevelPt for each entry (heading has none).
        toc.Select(p => p.Formatting.IndentLeftPt).Should().Equal(
            0,                                       // heading
            0 * TableOfContents.IndentPerLevelPt,    // Title (level 0)
            1 * TableOfContents.IndentPerLevelPt,    // Heading1
            2 * TableOfContents.IndentPerLevelPt,    // Heading2
            3 * TableOfContents.IndentPerLevelPt,    // Heading3
            1 * TableOfContents.IndentPerLevelPt);   // Heading1
    }

    [Fact]
    public void Build_DefaultEntryParagraphsContainHeadingTabPageNumberAndDottedRightTabStop()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });

        var toc = TableOfContents.Build(doc);

        var entry = toc[1];
        entry.Runs.Select(run => run.Text).Should().Equal("Chapter One", "\t", "1");
        entry.PlainText.Should().Be("Chapter One\t1");
        entry.Formatting.TabStops.Should().Equal(
            new TabStop(
                TableOfContents.DefaultEntryRightTabStopPt,
                TabStopAlignment.Right,
                TabLeader.Dots));
    }

    [Fact]
    public void Build_EntriesCarryOneNativeWordTocFieldAndHeadingRemainsOutsideIt()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Document Title") { StyleId = "Title" });
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Deep") { StyleId = "Heading6" });

        var toc = TableOfContents.Build(doc);
        var entries = toc.Skip(1).ToArray();

        toc[0].SpanningFieldStart.Should().BeNull();
        toc[0].SpanningFieldOwner.Should().BeNull();
        toc[0].EndsSpanningField.Should().BeFalse();
        entries.Should().OnlyContain(paragraph =>
            paragraph.SpanningFieldOwner != null
            && paragraph.SpanningFieldOwner.Instruction == TableOfContents.NativeFieldInstruction);
        entries[0].SpanningFieldStart!.Instruction.Should().Be(TableOfContents.NativeFieldInstruction);
        entries.Skip(1).Should().OnlyContain(paragraph => paragraph.SpanningFieldStart == null);
        entries.Take(entries.Length - 1).Should().OnlyContain(paragraph => !paragraph.EndsSpanningField);
        entries[^1].EndsSpanningField.Should().BeTrue();
        entries.Select(paragraph => paragraph.StyleId).Should().Equal("TOC1", "TOC1", "TOC3");
    }

    [Fact]
    public void NativeFieldInstructionFor_UsesImportedStyleNamesAndClampsDeepLevels()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles["Heading6"] = new DocumentStyle
        {
            Id = "Heading6",
            Name = "Deep chapter",
            OutlineLevel = 5
        };

        TableOfContents.NativeFieldInstructionFor(doc)
            .Should().Contain("Heading 3,3")
            .And.Contain("Deep chapter,3")
            .And.NotContain("Heading 6,3");
    }

    [Fact]
    public void EnsureStyles_SeedsUsedDeepHeadingForNativeWordUpdate()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Deep") { StyleId = "Heading6" });

        TableOfContents.EnsureStyles(doc);

        doc.Styles["Heading6"].Name.Should().Be("Heading 6");
        doc.Styles["Heading6"].OutlineLevel.Should().Be(5);
        doc.Styles["Heading6"].BasedOnStyleId.Should().Be("Normal");
    }

    [Fact]
    public void Build_EntryRightTabStopUsesWritablePageWidth()
    {
        var doc = new TextDocument();
        doc.Page.WidthPt = 700;
        doc.Page.MarginLeftPt = 80;
        doc.Page.MarginRightPt = 120;
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });

        var entry = TableOfContents.Build(doc)[1];

        entry.Formatting.TabStops.Should().ContainSingle()
            .Which.Should().Be(new TabStop(500, TabStopAlignment.Right, TabLeader.Dots));
    }

    [Fact]
    public void Build_ExplicitPageBreaksBeforeOrInsidePrecedingContentAdvanceLaterHeadingPageNumbers()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("First") { StyleId = "Heading1" });

        var paragraphWithInlineBreak = new Paragraph("Body before break");
        paragraphWithInlineBreak.Runs.Add(Run.PageBreak());
        paragraphWithInlineBreak.Runs.Add(new Run("Body after break"));
        doc.Blocks.Add(paragraphWithInlineBreak);
        doc.Blocks.Add(new Paragraph("After inline break") { StyleId = "Heading1" });

        doc.Blocks.Add(new Paragraph("Paged body")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });
        doc.Blocks.Add(new Paragraph("After page-break-before content") { StyleId = "Heading1" });

        var entries = TableOfContents.Build(doc).Skip(1).Select(p => p.PlainText);

        entries.Should().Equal(
            "First\t1",
            "After inline break\t2",
            "After page-break-before content\t3");
    }

    [Fact]
    public void Build_PageBreakBeforeHeadingAdvancesThatHeadingPageNumber()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("First") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Second")
        {
            StyleId = "Heading1",
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var entries = TableOfContents.Build(doc).Skip(1).Select(p => p.PlainText);

        entries.Should().Equal("First\t1", "Second\t2");
    }

    [Fact]
    public void Build_PageTextResolverOverridesPhysicalDecimalAndKeepsFallback()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Front matter") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Main matter") { StyleId = "Heading1" });

        var entries = TableOfContents.Build(
                doc,
                blockIndex => blockIndex == 0 ? "iv" : null)
            .Skip(1)
            .Select(paragraph => paragraph.PlainText);

        entries.Should().Equal("Front matter\tiv", "Main matter\t1");
    }

    [Fact]
    public void Build_SectionBreaksAdvanceLaterHeadingPageNumbers()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("First") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Odd page section")
        {
            SectionBreak = new Section(new PageSettings(), SectionBreakKind.OddPage)
        });
        doc.Blocks.Add(new Paragraph("After odd break") { StyleId = "Heading1" });

        var entries = TableOfContents.Build(doc).Skip(1).Select(p => p.PlainText);

        entries.Should().Equal("First\t1", "After odd break\t3");
    }

    [Fact]
    public void Build_DeepHeading_IndentsByTrueLevelButClampsStyleId()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Deep") { StyleId = "Heading6" });

        var toc = TableOfContents.Build(doc);

        var entry = toc[1];
        entry.PlainText.Should().Be("Deep\t1");
        entry.Formatting.IndentLeftPt.Should().Be(6 * TableOfContents.IndentPerLevelPt);
        // The style id is clamped to the deepest registered level so it still resolves to a TOC style.
        entry.StyleId.Should().Be("TOC" + TableOfContents.MaxStyledLevel);
    }

    [Fact]
    public void Build_DoesNotMutateTheDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });

        var before = doc.Blocks.Count;
        TableOfContents.Build(doc);

        doc.Blocks.Should().HaveCount(before);
    }

    [Fact]
    public void IsTocStyleId_RecognisesGeneratedStyles()
    {
        TableOfContents.IsTocStyleId(TableOfContents.HeadingStyleId).Should().BeTrue();
        TableOfContents.IsTocStyleId("TOC0").Should().BeTrue();
        TableOfContents.IsTocStyleId("TOC1").Should().BeTrue();
        TableOfContents.IsTocStyleId("TOC3").Should().BeTrue();

        TableOfContents.IsTocStyleId(null).Should().BeFalse();
        TableOfContents.IsTocStyleId("").Should().BeFalse();
        TableOfContents.IsTocStyleId("Normal").Should().BeFalse();
        TableOfContents.IsTocStyleId("Heading1").Should().BeFalse();
        TableOfContents.IsTocStyleId("TOC").Should().BeFalse();      // no level number
    }

    [Fact]
    public void IsTocParagraph_TrueOnlyForTocStyledParagraphs()
    {
        TableOfContents.IsTocParagraph(new Paragraph("x") { StyleId = "TOC1" }).Should().BeTrue();
        TableOfContents.IsTocParagraph(new Paragraph("x")
        {
            SpanningFieldOwner = new ComplexField(" TOC \\o \"1-3\" ")
        }).Should().BeTrue();
        TableOfContents.IsTocParagraph(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" TOC \\o \"1-3\" ", "Chapter One\t1") }
        }).Should().BeTrue();
        TableOfContents.IsTocParagraph(new Paragraph("x") { StyleId = "Heading1" }).Should().BeFalse();
        TableOfContents.IsTocParagraph(new Paragraph("x")
        {
            SpanningFieldOwner = new ComplexField(" INDEX ")
        }).Should().BeFalse();
        TableOfContents.IsTocParagraph(new Paragraph("x")
        {
            SpanningFieldOwner = new ComplexField(" TOC \\c \"Figure\" ")
        }).Should().BeFalse();
        TableOfContents.IsTocParagraph(new Paragraph("x")
        {
            StyleId = "TOC1",
            SpanningFieldOwner = new ComplexField(" TOC \\c \"Figure\" ")
        }).Should().BeFalse();
        TableOfContents.IsTocParagraph(Table.Create(1, 1)).Should().BeFalse();
    }

    [Fact]
    public void EnsureStyles_RegistersTocStylesIdempotently()
    {
        var doc = TextDocument.CreateEmpty();

        TableOfContents.EnsureStyles(doc);
        TableOfContents.EnsureStyles(doc); // second call must not throw or duplicate

        doc.Styles.Should().ContainKey(TableOfContents.HeadingStyleId);
        doc.Styles.Should().ContainKey("TOC1");
        doc.Styles.Should().ContainKey("TOC2");
        doc.Styles.Should().ContainKey("TOC3");
    }

    [Fact]
    public void EnsureStyles_DoesNotOverwriteAnExistingStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles[TableOfContents.HeadingStyleId] = new DocumentStyle
        {
            Id = TableOfContents.HeadingStyleId,
            Name = "Custom"
        };

        TableOfContents.EnsureStyles(doc);

        doc.Styles[TableOfContents.HeadingStyleId].Name.Should().Be("Custom");
    }
}
