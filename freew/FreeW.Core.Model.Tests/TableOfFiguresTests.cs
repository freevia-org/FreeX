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
            "Figure 1: First diagram",
            "Figure 2: Second diagram");

        tof.Select(p => p.StyleId).Should().Equal(
            TableOfFigures.HeadingStyleId,
            TableOfFigures.EntryStyleId,
            TableOfFigures.EntryStyleId);
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
            "Table 1: Quarterly results",
            "Table 2: Annual results");

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
            "Equation 1: E = mc2",
            "Equation 2: F = ma");
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
            "Scheme 1: Flow",
            "Scheme 2: State");
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
