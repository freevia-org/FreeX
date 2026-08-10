namespace FreeW.Core.Model.Tests;

public class CaptionsTests
{
    [Fact]
    public void NextCaptionNumber_EmptyDocument_StartsAtOne()
    {
        var doc = new TextDocument();

        Captions.NextCaptionNumber(doc, CaptionLabel.Figure).Should().Be(1);
        Captions.NextCaptionNumber(doc, CaptionLabel.Table).Should().Be(1);
        Captions.NextCaptionNumber(doc, CaptionLabel.Equation).Should().Be(1);
    }

    [Fact]
    public void NextCaptionNumber_IncrementsPerLabelIndependently()
    {
        var doc = new TextDocument();

        // Two figures and one table interleaved with an ordinary paragraph.
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 1, "First diagram"));
        doc.Blocks.Add(new Paragraph("Some body text"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Table, 1, "First table"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 2, "Second diagram"));

        // Figures counted separately from tables.
        Captions.NextCaptionNumber(doc, CaptionLabel.Figure).Should().Be(3);
        Captions.NextCaptionNumber(doc, CaptionLabel.Table).Should().Be(2);
    }

    [Fact]
    public void NextCaptionNumber_IncrementsCustomLabelsIndependently()
    {
        var doc = new TextDocument();

        doc.Blocks.Add(Captions.BuildCaption("Scheme", 1, "Flow"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Equation, 1, "Energy"));
        doc.Blocks.Add(Captions.BuildCaption("Scheme", 2, "State"));

        Captions.NextCaptionNumber(doc, "Scheme").Should().Be(3);
        Captions.NextCaptionNumber(doc, CaptionLabel.Equation).Should().Be(2);
    }

    [Fact]
    public void NextCaptionNumber_CountsCaptionsInsideNestedTables()
    {
        var doc = new TextDocument();
        var outer = Table.Create(1, 1);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] =
            Captions.BuildCaption(CaptionLabel.Figure, 1, "Nested figure");
        outer.Rows[0].Cells[0].NestedTables.Add(nested);
        outer.Rows[0].Cells[0].Paragraphs[0] =
            Captions.BuildCaption(CaptionLabel.Figure, 2, "Outer figure");
        doc.Blocks.Add(outer);

        Captions.NextCaptionNumber(doc, CaptionLabel.Figure).Should().Be(3);
    }

    [Fact]
    public void NextCaptionNumber_IgnoresUnstyledParagraphsThatLookLikeCaptions()
    {
        var doc = new TextDocument();

        // A plain paragraph starting with "Figure 1" but NOT carrying the Caption style is not counted.
        doc.Blocks.Add(new Paragraph("Figure 1: not really a caption"));

        Captions.NextCaptionNumber(doc, CaptionLabel.Figure).Should().Be(1);
    }

    [Fact]
    public void BuildCaption_WithText_ProducesLabelNumberColonText()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "My diagram");

        caption.PlainText.Should().Be("Figure 1: My diagram");
        caption.StyleId.Should().Be(Captions.StyleId);
        caption.Runs.Should().HaveCount(3);
        caption.Runs[1].ComplexField!.Instruction.Should().Be(" SEQ Figure \\* ARABIC ");
        caption.Runs[1].Text.Should().Be("1");
    }

    [Fact]
    public void BuildCaption_CustomLabel_ProducesLabelNumberColonText()
    {
        var caption = Captions.BuildCaption("Scheme", 3, "State machine");

        caption.PlainText.Should().Be("Scheme 3: State machine");
        caption.StyleId.Should().Be(Captions.StyleId);
        Captions.IsCaptionOf(caption, "Scheme").Should().BeTrue();
    }

    [Fact]
    public void BuildCaption_MultiwordCustomLabelQuotesNativeSequenceArgument()
    {
        var caption = Captions.BuildCaption("Flow Diagram", 2, "State");

        caption.PlainText.Should().Be("Flow Diagram 2: State");
        caption.Runs[1].ComplexField!.Instruction
            .Should().Be(" SEQ \"Flow Diagram\" \\* ARABIC ");
    }

    [Fact]
    public void BuildCaption_NoText_OmitsSeparator()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Table, 2, "   ");

        caption.PlainText.Should().Be("Table 2");
        caption.StyleId.Should().Be(Captions.StyleId);
    }

    [Fact]
    public void BuildCaption_IsRecognisedAsCaptionParagraph()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "X");

        Captions.IsCaptionParagraph(caption).Should().BeTrue();
        Captions.IsCaptionParagraph(new Paragraph("X")).Should().BeFalse();
    }

    [Fact]
    public void NextCaptionNumber_CountsCaptionsBuiltByBuildCaption_AfterInsertion()
    {
        var doc = new TextDocument();

        var n1 = Captions.NextCaptionNumber(doc, CaptionLabel.Figure);
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, n1, "A"));
        var n2 = Captions.NextCaptionNumber(doc, CaptionLabel.Figure);
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, n2, "B"));

        n1.Should().Be(1);
        n2.Should().Be(2);
        Captions.NextCaptionNumber(doc, CaptionLabel.Figure).Should().Be(3);
    }

    [Fact]
    public void CreateEmpty_RegistersCaptionStyle()
    {
        var doc = TextDocument.CreateEmpty();

        doc.Styles.Should().ContainKey(Captions.StyleId);
        doc.Styles[Captions.StyleId].Name.Should().Be("Caption");
    }

    [Fact]
    public void EnsureStyles_AddsCaptionStyleIdempotently()
    {
        var doc = new TextDocument();
        doc.Styles.Should().NotContainKey(Captions.StyleId);

        Captions.EnsureStyles(doc);
        Captions.EnsureStyles(doc); // second call is a no-op

        doc.Styles.Should().ContainKey(Captions.StyleId);
    }
}
