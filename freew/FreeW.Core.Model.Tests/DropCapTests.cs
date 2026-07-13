namespace FreeW.Core.Model.Tests;

public class DropCapTests
{
    [Fact]
    public void ApplyDropCap_SplitsFirstCharacterIntoEnlargedBoldRun_RestUnchanged()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("Hello world");
        doc.Blocks.Add(paragraph);

        DropCap.ApplyDropCap(paragraph);

        paragraph.Runs.Should().HaveCount(2);

        var cap = paragraph.Runs[0];
        cap.Text.Should().Be("H");
        cap.Formatting.Bold.Should().BeTrue();
        cap.Formatting.FontSizePt.Should().Be(DropCap.DefaultSizePt);
        paragraph.DropCap.Should().Be(new DropCapLayoutIntent(
            DropCapPosition.Dropped,
            DropCap.DefaultLineSpan,
            DropCap.DefaultSizePt,
            DropCap.DefaultDistanceFromTextPt));

        var rest = paragraph.Runs[1];
        rest.Text.Should().Be("ello world");
        // The remainder keeps the original (default) formatting — neither bold nor enlarged.
        rest.Formatting.Should().Be(RunFormatting.Default);

        // The paragraph's text is preserved end to end.
        paragraph.PlainText.Should().Be("Hello world");
    }

    [Fact]
    public void ApplyDropCap_PreservesRemainderRunFormatting()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        var original = new RunFormatting { Italic = true, FontFamily = "Georgia", FontSizePt = 11 };
        paragraph.Runs.Add(new Run("Once", original));
        doc.Blocks.Add(paragraph);

        DropCap.ApplyDropCap(paragraph, sizePt: 48);

        paragraph.Runs[0].Text.Should().Be("O");
        paragraph.Runs[0].Formatting.Bold.Should().BeTrue();
        paragraph.Runs[0].Formatting.FontSizePt.Should().Be(48);
        // The cap inherits the rest of the source formatting (italic / family) and adds bold + size.
        paragraph.Runs[0].Formatting.Italic.Should().BeTrue();
        paragraph.Runs[0].Formatting.FontFamily.Should().Be("Georgia");

        paragraph.Runs[1].Text.Should().Be("nce");
        paragraph.Runs[1].Formatting.Should().Be(original);
    }

    [Fact]
    public void ApplyDropCap_InMarginRetainsDistinctLayoutIntent()
    {
        var paragraph = new Paragraph("Margin");

        DropCap.ApplyDropCap(
            paragraph,
            DropCapPosition.InMargin,
            sizePt: 48,
            lineSpan: 4,
            distanceFromTextPt: 9);

        paragraph.Runs[0].Text.Should().Be("M");
        paragraph.Runs[0].Formatting.FontSizePt.Should().Be(48);
        paragraph.DropCap.Should().Be(new DropCapLayoutIntent(
            DropCapPosition.InMargin,
            4,
            48,
            9));
    }

    [Fact]
    public void ApplyDropCap_SingleCharacterRun_EnlargesInPlace()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("A");
        doc.Blocks.Add(paragraph);

        DropCap.ApplyDropCap(paragraph);

        paragraph.Runs.Should().HaveCount(1);
        paragraph.Runs[0].Text.Should().Be("A");
        paragraph.Runs[0].Formatting.Bold.Should().BeTrue();
        paragraph.Runs[0].Formatting.FontSizePt.Should().Be(DropCap.DefaultSizePt);
        paragraph.DropCap.Should().NotBeNull();
    }

    [Fact]
    public void ApplyDropCap_EmptyParagraph_IsNoOp()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        doc.Blocks.Add(paragraph);

        DropCap.ApplyDropCap(paragraph);

        paragraph.Runs.Should().BeEmpty();
        paragraph.DropCap.Should().BeNull();
    }

    [Fact]
    public void ClearFormatting_ResetsEveryRunToDefault_PreservingText()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bold ", new RunFormatting { Bold = true, FontSizePt = 18 }));
        paragraph.Runs.Add(new Run("and italic", new RunFormatting { Italic = true, ColorHex = "#FF0000" }));
        paragraph.DropCap = new DropCapLayoutIntent(DropCapPosition.Dropped, 3, 42, 6);
        doc.Blocks.Add(paragraph);

        DropCap.ClearFormatting(paragraph);

        paragraph.PlainText.Should().Be("Bold and italic");
        paragraph.Runs.Should().OnlyContain(r => r.Formatting == RunFormatting.Default);
        paragraph.DropCap.Should().BeNull();
    }

    [Fact]
    public void ClearFormatting_KeepsRunCountAndText()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("one", new RunFormatting { Underline = true }));
        paragraph.Runs.Add(new Run("two", new RunFormatting { Strikethrough = true }));
        paragraph.Runs.Add(new Run("three", new RunFormatting { AllCaps = true }));
        doc.Blocks.Add(paragraph);

        DropCap.ClearFormatting(paragraph);

        paragraph.Runs.Should().HaveCount(3);
        paragraph.Runs.Select(r => r.Text).Should().Equal("one", "two", "three");
        paragraph.Runs.Should().OnlyContain(r => r.Formatting == RunFormatting.Default);
    }
}
