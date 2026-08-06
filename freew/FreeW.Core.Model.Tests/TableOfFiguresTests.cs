namespace FreeW.Core.Model.Tests;

public class TableOfFiguresTests
{
    // A Caption-styled paragraph reading "Figure {n}: {text}" / "Table {n}: {text}", as produced by Captions.
    private static Paragraph Caption(CaptionLabel label, int number, string text) =>
        Captions.BuildCaption(label, number, text);

    [Fact]
    public void Build_EmptyDocument_YieldsOnlyTheHeadingParagraph()
    {
        var doc = new TextDocument();

        var tof = TableOfFigures.Build(doc);

        tof.Should().ContainSingle();
        tof[0].PlainText.Should().Be("Table of Figures");
        tof[0].StyleId.Should().Be(TableOfFigures.HeadingStyleId);
    }

    [Fact]
    public void Build_FigureCaptions_YieldsHeadingThenEntriesInDocumentOrder()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 1, "First diagram"));
        doc.Blocks.Add(Caption(CaptionLabel.Table, 1, "A table that must be skipped"));
        doc.Blocks.Add(new Paragraph("More body") { StyleId = "Heading1" });
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 2, "Second diagram"));

        var tof = TableOfFigures.Build(doc);

        tof.Select(p => p.PlainText).Should().Equal(
            "Table of Figures",
            "Figure 1: First diagram\t1",
            "Figure 2: Second diagram\t1");

        tof.Select(p => p.StyleId).Should().Equal(
            TableOfFigures.HeadingStyleId,
            TableOfFigures.EntryStyleId,
            TableOfFigures.EntryStyleId);

        tof[0].SpanningFieldOwner.Should().BeNull();
        tof.Skip(1).All(paragraph => paragraph.SpanningFieldOwner?.Instruction == " TOC \\c \"Figure\" ")
            .Should().BeTrue();
        tof[1].SpanningFieldStart!.Instruction.Should().Be(" TOC \\c \"Figure\" ");
        tof[^1].EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public void Build_TableLabel_YieldsTableOfTablesWithOnlyTableCaptions()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 1, "A figure"));
        doc.Blocks.Add(Caption(CaptionLabel.Table, 1, "Quarterly results"));
        doc.Blocks.Add(Caption(CaptionLabel.Table, 2, "Annual results"));

        var tof = TableOfFigures.Build(doc, CaptionLabel.Table);

        tof.Select(p => p.PlainText).Should().Equal(
            "Table of Tables",
            "Table 1: Quarterly results\t1",
            "Table 2: Annual results\t1");

        tof[0].StyleId.Should().Be(TableOfFigures.HeadingStyleId);
        tof.Skip(1).Should().OnlyContain(p => p.StyleId == TableOfFigures.EntryStyleId);
    }

    [Fact]
    public void Build_EquationLabel_YieldsTableOfEquationsWithOnlyEquationCaptions()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 1, "A figure"));
        doc.Blocks.Add(Caption(CaptionLabel.Equation, 1, "E = mc2"));
        doc.Blocks.Add(Caption(CaptionLabel.Equation, 2, "F = ma"));

        var tof = TableOfFigures.Build(doc, CaptionLabel.Equation);

        tof.Select(p => p.PlainText).Should().Equal(
            "Table of Equations",
            "Equation 1: E = mc2\t1",
            "Equation 2: F = ma\t1");
    }

    [Fact]
    public void Build_CustomLabel_YieldsCustomHeadingAndEntries()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(Captions.BuildCaption("Scheme", 1, "Flow"));
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 1, "A figure"));
        doc.Blocks.Add(Captions.BuildCaption("Scheme", 2, "State"));

        var tof = TableOfFigures.Build(doc, "Scheme");

        tof.Select(p => p.PlainText).Should().Equal(
            "Table of Schemes",
            "Scheme 1: Flow\t1",
            "Scheme 2: State\t1");
    }

    [Fact]
    public void Build_EntriesUseDottedRightTabAndLogicalPageResolverWithDecimalFallback()
    {
        var doc = new TextDocument();
        doc.Page.WidthPt = 700;
        doc.Page.MarginLeftPt = 80;
        doc.Page.MarginRightPt = 120;
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 1, "Front matter"));
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 2, "Main matter"));

        var entries = TableOfFigures.Build(
            doc,
            CaptionLabel.Figure,
            blockIndex => blockIndex == 0 ? "iv" : null);

        entries.Skip(1).Select(paragraph => paragraph.PlainText)
            .Should().Equal("Figure 1: Front matter\tiv", "Figure 2: Main matter\t1");
        entries[1].Formatting.TabStops.Should().Equal(
            new TabStop(500, TabStopAlignment.Right, TabLeader.Dots));
    }

    [Fact]
    public void Build_ExplicitPageBreakAdvancesCaptionFallbackPage()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 1, "First"));
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 2, "Second"));

        TableOfFigures.Build(doc).Skip(1).Select(paragraph => paragraph.PlainText)
            .Should().Equal("Figure 1: First\t1", "Figure 2: Second\t2");
    }

    [Fact]
    public void ExistingLabelText_InfersBuiltInAndCustomGeneratedHeadings()
    {
        var doc = new TextDocument();

        doc.Blocks.Add(new Paragraph("Table of Equations") { StyleId = TableOfFigures.HeadingStyleId });
        TableOfFigures.ExistingLabelText(doc).Should().Be(Captions.EquationLabelText);

        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Table of Schemes") { StyleId = TableOfFigures.HeadingStyleId });
        TableOfFigures.ExistingLabelText(doc).Should().Be("Scheme");

        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Scheme 1\t1")
        {
            StyleId = "Normal",
            SpanningFieldOwner = new ComplexField(" TOC \\c \"Scheme\" ")
        });
        TableOfFigures.ExistingLabelText(doc).Should().Be("Scheme");

        doc.Blocks.Insert(0, new Paragraph("Table of Figures")
        {
            StyleId = TableOfFigures.HeadingStyleId
        });
        TableOfFigures.ExistingLabelText(doc).Should().Be("Scheme");
    }

    [Fact]
    public void Build_NoMatchingCaptions_YieldsOnlyTheHeading()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(Caption(CaptionLabel.Table, 1, "Only a table"));

        var tof = TableOfFigures.Build(doc, CaptionLabel.Figure);

        tof.Should().ContainSingle()
            .Which.StyleId.Should().Be(TableOfFigures.HeadingStyleId);
    }

    [Fact]
    public void Build_DoesNotMutateTheDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(Caption(CaptionLabel.Figure, 1, "A figure"));

        var before = doc.Blocks.Count;
        TableOfFigures.Build(doc);

        doc.Blocks.Should().HaveCount(before);
    }

    [Fact]
    public void IsTableOfFiguresStyleId_RecognisesGeneratedStyles()
    {
        TableOfFigures.IsTableOfFiguresStyleId(TableOfFigures.HeadingStyleId).Should().BeTrue();
        TableOfFigures.IsTableOfFiguresStyleId(TableOfFigures.EntryStyleId).Should().BeTrue();

        TableOfFigures.IsTableOfFiguresStyleId(null).Should().BeFalse();
        TableOfFigures.IsTableOfFiguresStyleId("").Should().BeFalse();
        TableOfFigures.IsTableOfFiguresStyleId("Normal").Should().BeFalse();
        TableOfFigures.IsTableOfFiguresStyleId("Caption").Should().BeFalse();
    }

    [Fact]
    public void IsTableOfFiguresParagraph_TrueOnlyForGeneratedStyledParagraphs()
    {
        TableOfFigures.IsTableOfFiguresParagraph(
            new Paragraph("x") { StyleId = TableOfFigures.EntryStyleId }).Should().BeTrue();
        TableOfFigures.IsTableOfFiguresParagraph(
            new Paragraph("x") { StyleId = TableOfFigures.HeadingStyleId }).Should().BeTrue();
        TableOfFigures.IsTableOfFiguresParagraph(
            new Paragraph("x") { StyleId = "Caption" }).Should().BeFalse();
        TableOfFigures.IsTableOfFiguresParagraph(new Paragraph("x")
        {
            SpanningFieldOwner = new ComplexField(" TOC \\c \"Figure\" ")
        }).Should().BeTrue();
        TableOfFigures.IsTableOfFiguresParagraph(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" TOC \\a \"Table\" ", "Table 1\t1") }
        }).Should().BeTrue();
        TableOfFigures.IsTableOfFiguresParagraph(new Paragraph("x")
        {
            SpanningFieldOwner = new ComplexField(" TOC \\o \"1-3\" ")
        }).Should().BeFalse();
        TableOfFigures.IsTableOfFiguresParagraph(new Paragraph("x")
        {
            StyleId = TableOfFigures.EntryStyleId,
            SpanningFieldOwner = new ComplexField(" TOC \\o \"1-3\" ")
        }).Should().BeFalse();
        TableOfFigures.IsTableOfFiguresParagraph(Table.Create(1, 1)).Should().BeFalse();
    }

    [Fact]
    public void EnsureStyles_RegistersStylesIdempotently()
    {
        var doc = TextDocument.CreateEmpty();

        TableOfFigures.EnsureStyles(doc);
        TableOfFigures.EnsureStyles(doc); // second call must not throw or duplicate

        doc.Styles.Should().ContainKey(TableOfFigures.HeadingStyleId);
        doc.Styles.Should().ContainKey(TableOfFigures.EntryStyleId);
    }

    [Fact]
    public void EnsureStyles_DoesNotOverwriteAnExistingStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles[TableOfFigures.HeadingStyleId] = new DocumentStyle
        {
            Id = TableOfFigures.HeadingStyleId,
            Name = "Custom"
        };

        TableOfFigures.EnsureStyles(doc);

        doc.Styles[TableOfFigures.HeadingStyleId].Name.Should().Be("Custom");
    }
}
