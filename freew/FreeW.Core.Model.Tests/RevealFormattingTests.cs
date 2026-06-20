namespace FreeW.Core.Model.Tests;

public class RevealFormattingTests
{
    private static IReadOnlyList<RevealFormattingItem> Section(
        IReadOnlyList<RevealFormattingSection> sections, string heading) =>
        sections.Single(s => s.Heading == heading).Items;

    private static string Value(IReadOnlyList<RevealFormattingItem> items, string label) =>
        items.Single(i => i.Label == label).Value;

    [Fact]
    public void Describe_ProducesFontParagraphSectionGroups()
    {
        var sections = RevealFormatting.Describe(
            RunFormatting.Default, ParagraphFormatting.Default, new PageSettings());

        sections.Select(s => s.Heading).Should().ContainInOrder("FONT", "PARAGRAPH", "SECTION");
    }

    [Fact]
    public void Describe_Font_ShowsFamilySizeColorAndEffects()
    {
        var run = new RunFormatting
        {
            FontFamily = "Calibri",
            FontSizePt = 11,
            ColorHex = "#FF0000",
            Bold = true,
            Italic = true
        };

        var font = Section(RevealFormatting.Describe(run, ParagraphFormatting.Default, new PageSettings()), "FONT");

        Value(font, "Font").Should().Be("Calibri");
        Value(font, "Size").Should().Be("11 pt");
        Value(font, "Color").Should().Be("#FF0000");
        Value(font, "Effects").Should().Be("Bold, Italic");
    }

    [Fact]
    public void Describe_Font_NoEffectsAndNoColor_ReadAsNoneAndAutomatic()
    {
        var font = Section(
            RevealFormatting.Describe(RunFormatting.Default, ParagraphFormatting.Default, new PageSettings()),
            "FONT");

        Value(font, "Effects").Should().Be("(none)");
        Value(font, "Color").Should().Be("Automatic");
    }

    [Fact]
    public void Describe_Font_SuperscriptAndCapsListedAsEffects()
    {
        var run = new RunFormatting { Underline = true, AllCaps = true, VerticalAlign = VerticalAlign.Superscript };
        var font = Section(RevealFormatting.Describe(run, ParagraphFormatting.Default, new PageSettings()), "FONT");

        Value(font, "Effects").Should().Be("Underline, All caps, Superscript");
    }

    [Fact]
    public void Describe_Paragraph_ShowsAlignmentSpacingAndLineSpacing()
    {
        var p = new ParagraphFormatting
        {
            Alignment = TextAlignment.Center,
            SpaceBeforePt = 6,
            SpaceAfterPt = 12,
            LineSpacing = 2.0
        };

        var para = Section(RevealFormatting.Describe(RunFormatting.Default, p, new PageSettings()), "PARAGRAPH");

        Value(para, "Alignment").Should().Be("Centered");
        Value(para, "Spacing").Should().Be("Before 6 pt, After 12 pt");
        Value(para, "Line spacing").Should().Be("Double");
    }

    [Fact]
    public void Describe_Paragraph_HangingIndentReadsAsHanging()
    {
        var p = new ParagraphFormatting { IndentLeftPt = 36, FirstLineIndentPt = -18 };
        var para = Section(RevealFormatting.Describe(RunFormatting.Default, p, new PageSettings()), "PARAGRAPH");

        Value(para, "Indentation").Should().Be("Left 0.5\", Right 0\", Hanging 0.25\"");
    }

    [Fact]
    public void Describe_Paragraph_ExactLineSpacingShowsPoints()
    {
        var p = new ParagraphFormatting { LineRule = LineSpacingRule.Exact, LineHeightPt = 14 };
        var para = Section(RevealFormatting.Describe(RunFormatting.Default, p, new PageSettings()), "PARAGRAPH");

        Value(para, "Line spacing").Should().Be("Exactly 14 pt");
    }

    [Fact]
    public void Describe_Paragraph_ListShownOnlyWhenPresent_WithOneBasedLevel()
    {
        var none = Section(
            RevealFormatting.Describe(RunFormatting.Default, ParagraphFormatting.Default, new PageSettings()),
            "PARAGRAPH");
        none.Should().NotContain(i => i.Label == "List");

        var p = new ParagraphFormatting { ListKind = ListKind.Bullet, ListLevel = 1 };
        var listed = Section(RevealFormatting.Describe(RunFormatting.Default, p, new PageSettings()), "PARAGRAPH");
        Value(listed, "List").Should().Be("Bulleted (level 2)");
    }

    [Fact]
    public void Describe_Section_ShowsMarginsPaperOrientationAndColumns()
    {
        var page = new PageSettings { ColumnCount = 2, ColumnSpacingPt = 36 };
        var sec = Section(RevealFormatting.Describe(RunFormatting.Default, ParagraphFormatting.Default, page), "SECTION");

        Value(sec, "Margins").Should().Be("Top 1\", Bottom 1\", Left 1\", Right 1\"");
        Value(sec, "Paper").Should().Be("8.5\" × 11\" (Portrait)");
        Value(sec, "Columns").Should().Be("2 columns (spacing 0.5\")");
    }

    [Fact]
    public void Describe_Section_LandscapePaperDetectedFromDimensions()
    {
        var page = new PageSettings { WidthPt = 792, HeightPt = 612 };
        var sec = Section(RevealFormatting.Describe(RunFormatting.Default, ParagraphFormatting.Default, page), "SECTION");

        Value(sec, "Paper").Should().Be("11\" × 8.5\" (Landscape)");
        Value(sec, "Columns").Should().Be("One");
    }
}
