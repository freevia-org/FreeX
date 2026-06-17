namespace FreeW.Core.Model.Tests;

public class IndentationTests
{
    [Fact]
    public void IncreaseIndent_AddsOneStepToLeftIndent_LeavingOthers()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("Body") { Formatting = ParagraphFormatting.Default with { IndentRightPt = 7 } };
        doc.Blocks.Add(paragraph);

        var result = Indentation.IncreaseIndent(paragraph.Formatting);

        result.IndentLeftPt.Should().Be(36); // default 0.5in step
        result.IndentRightPt.Should().Be(7);
        result.FirstLineIndentPt.Should().Be(0);
        // Input is not mutated.
        paragraph.Formatting.IndentLeftPt.Should().Be(0);
    }

    [Fact]
    public void IncreaseIndent_HonoursCustomStep_AndAccumulates()
    {
        var f = ParagraphFormatting.Default with { IndentLeftPt = 18 };

        var result = Indentation.IncreaseIndent(f, stepPt: 18);

        result.IndentLeftPt.Should().Be(36);
    }

    [Fact]
    public void DecreaseIndent_SubtractsOneStep()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("Body") { Formatting = ParagraphFormatting.Default with { IndentLeftPt = 72 } };
        doc.Blocks.Add(paragraph);

        var result = Indentation.DecreaseIndent(paragraph.Formatting);

        result.IndentLeftPt.Should().Be(36);
    }

    [Fact]
    public void DecreaseIndent_ClampsAtZero_NeverNegative()
    {
        var f = ParagraphFormatting.Default with { IndentLeftPt = 12 };

        var result = Indentation.DecreaseIndent(f); // step 36 > 12

        result.IndentLeftPt.Should().Be(0);
    }

    [Fact]
    public void SetIndents_SetsLeftRightAndPositiveFirstLine()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("Body");
        doc.Blocks.Add(paragraph);

        var result = Indentation.SetIndents(paragraph.Formatting, leftPt: 24, rightPt: 12, firstLinePt: 18);

        result.IndentLeftPt.Should().Be(24);
        result.IndentRightPt.Should().Be(12);
        result.FirstLineIndentPt.Should().Be(18); // positive = first-line indent
    }

    [Fact]
    public void SetIndents_NegativeFirstLine_IsHangingIndent()
    {
        var f = ParagraphFormatting.Default;

        var result = Indentation.SetIndents(f, leftPt: 36, rightPt: 0, firstLinePt: -18);

        // Negative first-line is preserved verbatim: it models a hanging indent.
        result.FirstLineIndentPt.Should().Be(-18);
        result.IndentLeftPt.Should().Be(36);
    }

    [Fact]
    public void SetIndents_ClampsNegativeLeftAndRightAtZero()
    {
        var f = ParagraphFormatting.Default;

        var result = Indentation.SetIndents(f, leftPt: -10, rightPt: -5, firstLinePt: 0);

        result.IndentLeftPt.Should().Be(0);
        result.IndentRightPt.Should().Be(0);
    }
}
