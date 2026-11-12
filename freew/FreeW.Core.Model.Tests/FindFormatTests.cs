namespace FreeW.Core.Model.Tests;

public class FindFormatTests
{
    private static readonly RunFormatting Bold = new() { Bold = true };

    private static Paragraph Mixed()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("plain "));
        paragraph.Runs.Add(new Run("bold", Bold));
        paragraph.Runs.Add(new Run(" more", Bold));
        paragraph.Runs.Add(new Run(" tail"));
        return paragraph;
    }

    [Fact]
    public void None_IsEmpty_AndMatchesEverything()
    {
        FindFormat.None.IsEmpty.Should().BeTrue();
        FindFormat.None.Matches(Bold, "Heading1").Should().BeTrue();
        new FindFormat { Bold = false }.IsEmpty.Should().BeFalse();
        new FindFormat { ParagraphStyleId = "Normal" }.HasCharacterFormatting.Should().BeFalse();
    }

    [Fact]
    public void Matches_ChecksEveryCriterionThatIsSet()
    {
        var formatting = new RunFormatting
        {
            Bold = true,
            FontFamily = "Arial",
            FontSizePt = 12,
            ColorHex = "#ff0000",
            HighlightColorHex = "#FFFF00",
        };

        new FindFormat { Bold = true, Italic = false, Underline = false }.Matches(formatting, null).Should().BeTrue();
        new FindFormat { Italic = true }.Matches(formatting, null).Should().BeFalse();
        new FindFormat { FontFamily = "arial " }.Matches(formatting, null).Should().BeTrue();
        new FindFormat { FontFamily = "Calibri" }.Matches(formatting, null).Should().BeFalse();
        new FindFormat { FontSizePt = 12 }.Matches(formatting, null).Should().BeTrue();
        new FindFormat { FontSizePt = 11 }.Matches(formatting, null).Should().BeFalse();
        new FindFormat { ColorHex = "FF0000" }.Matches(formatting, null).Should().BeTrue();
        new FindFormat { ColorHex = "#00FF00" }.Matches(formatting, null).Should().BeFalse();
        new FindFormat { Highlight = true }.Matches(formatting, null).Should().BeTrue();
        new FindFormat { Highlight = false }.Matches(formatting, null).Should().BeFalse();
        new FindFormat { Highlight = true, HighlightColorHex = "#00FF00" }.Matches(formatting, null).Should().BeFalse();
        new FindFormat { Highlight = false }.Matches(RunFormatting.Default, null).Should().BeTrue();
    }

    [Fact]
    public void Matches_TreatsAnUnstyledParagraphAsNormal()
    {
        var normal = new FindFormat { ParagraphStyleId = "Normal" };
        normal.Matches(RunFormatting.Default, null).Should().BeTrue();
        normal.Matches(RunFormatting.Default, "Heading1").Should().BeFalse();
        new FindFormat { ParagraphStyleId = "Heading1" }.Matches(RunFormatting.Default, "heading1").Should().BeTrue();
    }

    [Fact]
    public void ApplyTo_SetsOnlyTheMembersThatAreSet()
    {
        var start = new RunFormatting { Italic = true, FontFamily = "Arial", HighlightColorHex = "#00FF00" };

        var result = new FindFormat { Bold = true, ColorHex = "ff0000", Highlight = false }.ApplyTo(start);

        result.Should().Be(start with { Bold = true, ColorHex = "#FF0000", HighlightColorHex = null });
        new FindFormat { Highlight = true }.ApplyTo(RunFormatting.Default).HighlightColorHex.Should().Be("#FFFF00");
        new FindFormat { Bold = false }.ApplyTo(Bold).Bold.Should().BeFalse();
    }

    [Fact]
    public void NormalizeColor_AcceptsCommonSpellings_AndRejectsNonsense()
    {
        FindFormat.NormalizeColor("ff0000").Should().Be("#FF0000");
        FindFormat.NormalizeColor(" #Ff0000 ").Should().Be("#FF0000");
        FindFormat.NormalizeColor("#80FF0000").Should().Be("#FF0000");
        FindFormat.NormalizeColor("red").Should().BeNull();
        FindFormat.NormalizeColor("").Should().BeNull();
    }

    [Fact]
    public void EffectiveFormatting_InheritsFromCharacterStyleParagraphStyleChainAndDefaults()
    {
        var document = new TextDocument();
        document.DefaultRun = new RunFormatting { FontFamily = "Calibri", FontSizePt = 11 };
        document.Styles["Base"] = new DocumentStyle { Id = "Base", Name = "Base", Run = new RunFormatting { Italic = true, ColorHex = "#112233" } };
        document.Styles["Title2"] = new DocumentStyle
        {
            Id = "Title2",
            Name = "Title 2",
            BasedOnStyleId = "Base",
            Run = new RunFormatting { Bold = true, FontSizePt = 20 },
        };
        document.Styles["Strong"] = new DocumentStyle
        {
            Id = "Strong",
            Name = "Strong",
            Type = StyleType.Character,
            Run = new RunFormatting { Underline = true },
        };
        var paragraph = new Paragraph { StyleId = "Title2" };
        var run = new Run("x", new RunFormatting { FontFamily = "Arial" }) { StyleId = "Strong" };
        paragraph.Runs.Add(run);

        var effective = FindFormat.EffectiveFormatting(document, paragraph, run);

        effective.Bold.Should().BeTrue();
        effective.Italic.Should().BeTrue();
        effective.Underline.Should().BeTrue();
        effective.FontFamily.Should().Be("Arial");
        effective.FontSizePt.Should().Be(20);
        effective.ColorHex.Should().Be("#112233");
        FindFormat.EffectiveFormatting(null, paragraph, run).Should().Be(run.Formatting);
    }

    [Fact]
    public void FormatOnlySearch_ReturnsEachContiguousFormattedStretch()
    {
        var criteria = new FindCriteria(string.Empty, default, new FindFormat { Bold = true });

        FormattedTextSearch.FindAll(null, Mixed(), criteria).Should().Equal((6, 9));
    }

    [Fact]
    public void FormatOnlySearch_StopsAtStructuralSegments()
    {
        FindTextSegment[] segments =
        [
            new("ab", Bold, null),
            new("\r", RunFormatting.Default, null, IsStructural: true),
            new("cd", Bold, null),
            new("ef", RunFormatting.Default, null),
        ];

        FormattedTextSearch.FindAll(segments, new FindCriteria(string.Empty, default, new FindFormat { Bold = true }))
            .Should().Equal((0, 2), (3, 2));
    }

    [Fact]
    public void TextSearchWithFormat_KeepsOnlyMatchesWhollyInsideMatchingFormatting()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("word "));
        paragraph.Runs.Add(new Run("word", Bold));
        paragraph.Runs.Add(new Run(" wo"));
        paragraph.Runs.Add(new Run("rd", Bold));

        FormattedTextSearch.FindAll(null, paragraph, new FindCriteria("word", default, new FindFormat { Bold = true }))
            .Should().Equal((5, 4));
        FormattedTextSearch.FindAll(null, paragraph, new FindCriteria("word", default, new FindFormat { Bold = false }))
            .Should().Equal((0, 4));
        FormattedTextSearch.FindAll(null, paragraph, new FindCriteria("word", default))
            .Should().Equal((0, 4), (5, 4), (10, 4));
    }

    [Fact]
    public void TextSearch_MatchesAcrossRunBoundaries()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("hel"));
        paragraph.Runs.Add(new Run("lo", Bold));

        FormattedTextSearch.FindAll(null, paragraph, new FindCriteria("hello", default)).Should().Equal((0, 5));
    }

    [Fact]
    public void StyleCriterion_MatchesWholeParagraphsOfThatStyle_UsingEffectiveFormatting()
    {
        var document = new TextDocument();
        document.Styles["Heading1"] = new DocumentStyle { Id = "Heading1", Name = "Heading 1", Run = Bold };
        var heading = new Paragraph("Title") { StyleId = "Heading1" };
        var body = new Paragraph("Title");

        var byStyle = new FindCriteria(string.Empty, default, new FindFormat { ParagraphStyleId = "Heading1" });
        FormattedTextSearch.FindAll(document, heading, byStyle).Should().Equal((0, 5));
        FormattedTextSearch.FindAll(document, body, byStyle).Should().BeEmpty();

        var boldText = new FindCriteria("Title", default, new FindFormat { Bold = true });
        FormattedTextSearch.FindAll(document, heading, boldText).Should().Equal((0, 5));
        FormattedTextSearch.FindAll(document, body, boldText).Should().BeEmpty();
    }

    [Fact]
    public void ProjectParagraph_MasksDrawingMirrorText_KeepingPlainTextOffsets()
    {
        var shape = Shape.TextBoxWith("needle", 100, 40);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("a "));
        paragraph.Runs.Add(Run.FromShape(shape));
        paragraph.Runs.Add(new Run(" needle"));

        var hits = FormattedTextSearch.FindAll(null, paragraph, new FindCriteria("needle", default));

        hits.Should().Equal((paragraph.PlainText.LastIndexOf("needle", StringComparison.Ordinal), 6));
    }

    [Fact]
    public void EmptyCriteria_FindNothing()
    {
        FormattedTextSearch.FindAll(null, Mixed(), new FindCriteria(string.Empty, default)).Should().BeEmpty();
        FormattedTextSearch.FindAll(null, Mixed(), new FindCriteria(string.Empty, default, FindFormat.None)).Should().BeEmpty();
    }
}
