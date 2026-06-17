namespace FreeW.Core.Model.Tests;

public class AccessibilityCheckerTests
{
    // A run of plain black-on-white body text (passes contrast, no link, no image).
    private static Run PlainRun(string text) => new(text);

    private static Paragraph BodyParagraph(string text)
    {
        var p = new Paragraph();
        p.Runs.Add(PlainRun(text));
        return p;
    }

    // A clean document: a title style, a Heading 1, body prose, a titled property — yields no issues.
    private static TextDocument CleanDocument()
    {
        var doc = new TextDocument();
        doc.Styles["Normal"] = new DocumentStyle { Id = "Normal", Name = "Normal" };
        doc.Styles["Heading1"] = new DocumentStyle { Id = "Heading1", Name = "Heading 1", BasedOnStyleId = "Normal" };
        doc.Properties.Title = "A Clean Document";

        doc.Blocks.Add(new Paragraph { StyleId = "Heading1", Runs = { PlainRun("Introduction") } });
        doc.Blocks.Add(BodyParagraph("Some readable black-on-white body text."));
        return doc;
    }

    [Fact]
    public void CleanDocument_YieldsNoIssues()
    {
        var report = AccessibilityChecker.Check(CleanDocument());

        report.IsClean.Should().BeTrue();
        report.Issues.Should().BeEmpty();
        report.ErrorCount.Should().Be(0);
        report.WarningCount.Should().Be(0);
        report.TipCount.Should().Be(0);
    }

    [Fact]
    public void Check_NullDocument_Throws()
    {
        var act = () => AccessibilityChecker.Check(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- Missing image alt text (Error) ---

    [Fact]
    public void MissingImageAltText_FlaggedWhenAltEmpty()
    {
        var doc = CleanDocument();
        var run = Run.FromImage(new InlineImage([1, 2, 3], 100, 100)); // no AltText
        doc.Blocks.Add(new Paragraph { Runs = { run } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().ContainSingle(i => i.Rule == AccessibilityRule.MissingImageAltText)
            .Which.Severity.Should().Be(AccessibilitySeverity.Error);
        report.Issues.Single(i => i.Rule == AccessibilityRule.MissingImageAltText).Run.Should().BeSameAs(run);
    }

    [Fact]
    public void MissingImageAltText_NotFlaggedWhenAltPresent()
    {
        var doc = CleanDocument();
        var image = new InlineImage([1, 2, 3], 100, 100) { AltText = "A photo of a cat" };
        doc.Blocks.Add(new Paragraph { Runs = { Run.FromImage(image) } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.MissingImageAltText);
    }

    // --- Uninformative hyperlink text (Warning) ---

    [Theory]
    [InlineData("")]
    [InlineData("click here")]
    [InlineData("Click here.")]
    [InlineData("read more")]
    [InlineData("https://example.com/page")]
    [InlineData("www.example.com")]
    public void UninformativeLinkText_Flagged(string linkText)
    {
        var doc = CleanDocument();
        var run = new Run(linkText) { HyperlinkUrl = "https://example.com/page" };
        doc.Blocks.Add(new Paragraph { Runs = { run } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().Contain(i =>
            i.Rule == AccessibilityRule.UninformativeLinkText &&
            i.Severity == AccessibilitySeverity.Warning);
    }

    [Fact]
    public void UninformativeLinkText_NotFlaggedForDescriptiveText()
    {
        var doc = CleanDocument();
        var run = new Run("the FreeW release notes") { HyperlinkUrl = "https://example.com/page" };
        doc.Blocks.Add(new Paragraph { Runs = { run } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.UninformativeLinkText);
    }

    [Fact]
    public void UninformativeLinkText_BareUrlEqualToHref_Flagged()
    {
        var doc = CleanDocument();
        var run = new Run("https://example.com") { HyperlinkUrl = "https://example.com" };
        doc.Blocks.Add(new Paragraph { Runs = { run } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().Contain(i => i.Rule == AccessibilityRule.UninformativeLinkText);
    }

    // --- Heading order gaps (Warning / Tip) ---

    [Fact]
    public void HeadingOrderGap_Flagged_WhenLevelSkipped()
    {
        var doc = new TextDocument();
        doc.Properties.Title = "T";
        doc.Blocks.Add(new Paragraph { StyleId = "Heading1", Runs = { PlainRun("One") } });
        doc.Blocks.Add(new Paragraph { StyleId = "Heading3", Runs = { PlainRun("Three") } }); // skips Heading2

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().Contain(i =>
            i.Rule == AccessibilityRule.HeadingOrderGap &&
            i.Severity == AccessibilitySeverity.Warning);
    }

    [Fact]
    public void HeadingOrderGap_NotFlagged_WhenLevelsConsecutive()
    {
        var doc = new TextDocument();
        doc.Properties.Title = "T";
        doc.Blocks.Add(new Paragraph { StyleId = "Heading1", Runs = { PlainRun("One") } });
        doc.Blocks.Add(new Paragraph { StyleId = "Heading2", Runs = { PlainRun("Two") } });
        doc.Blocks.Add(new Paragraph { StyleId = "Heading3", Runs = { PlainRun("Three") } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.HeadingOrderGap);
    }

    [Fact]
    public void HeadingOrderGap_Tip_WhenBodyTextButNoHeadings()
    {
        var doc = new TextDocument();
        doc.Properties.Title = "T";
        doc.Blocks.Add(BodyParagraph("Just some prose with no headings at all."));

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().ContainSingle(i => i.Rule == AccessibilityRule.HeadingOrderGap)
            .Which.Severity.Should().Be(AccessibilitySeverity.Tip);
    }

    // --- Tables without a header row (Warning) ---

    [Fact]
    public void TableMissingHeaderRow_Flagged_WhenNoHeaderSignal()
    {
        var doc = CleanDocument();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(PlainRun("a"));
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Add(PlainRun("b"));
        table.Rows[1].Cells[0].Paragraphs[0].Runs.Add(PlainRun("c"));
        table.Rows[1].Cells[1].Paragraphs[0].Runs.Add(PlainRun("d"));
        doc.Blocks.Add(table);

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().Contain(i =>
            i.Rule == AccessibilityRule.TableMissingHeaderRow &&
            i.Severity == AccessibilitySeverity.Warning &&
            i.Table == table);
    }

    [Fact]
    public void TableMissingHeaderRow_NotFlagged_WhenHeaderRowStyled()
    {
        var doc = CleanDocument();
        var table = Table.Create(2, 2);
        foreach (var cell in table.Rows.SelectMany(r => r.Cells))
            cell.Paragraphs[0].Runs.Add(PlainRun("x"));
        table.Formatting = table.Formatting with { HeaderRow = true };
        doc.Blocks.Add(table);

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.TableMissingHeaderRow);
    }

    [Fact]
    public void TableMissingHeaderRow_NotFlagged_WhenFirstRowAllBold()
    {
        var doc = CleanDocument();
        var table = Table.Create(2, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("Header", new RunFormatting { Bold = true }));
        table.Rows[1].Cells[0].Paragraphs[0].Runs.Add(PlainRun("data"));
        doc.Blocks.Add(table);

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.TableMissingHeaderRow);
    }

    // --- Low contrast text (Warning) ---

    [Fact]
    public void LowContrastText_Flagged_LightGreyOnWhite()
    {
        var doc = CleanDocument();
        var run = new Run("hard to read", new RunFormatting { ColorHex = "#BBBBBB" });
        doc.Blocks.Add(new Paragraph { Runs = { run } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().Contain(i =>
            i.Rule == AccessibilityRule.LowContrastText &&
            i.Severity == AccessibilitySeverity.Warning &&
            i.Run == run);
    }

    [Fact]
    public void LowContrastText_NotFlagged_BlackOnWhite()
    {
        var doc = CleanDocument();
        doc.Blocks.Add(new Paragraph { Runs = { new Run("easy to read", new RunFormatting { ColorHex = "#000000" }) } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.LowContrastText);
    }

    [Fact]
    public void LowContrastText_RespectsParagraphShadingBackground()
    {
        // White text on a dark-blue paragraph shading is high contrast and must NOT be flagged.
        var doc = CleanDocument();
        var p = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { ShadingColorHex = "#000080" },
            Runs = { new Run("white on navy", new RunFormatting { ColorHex = "#FFFFFF" }) }
        };
        doc.Blocks.Add(p);

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.LowContrastText);
    }

    [Fact]
    public void LowContrastText_WhiteOnWhite_Flagged()
    {
        var doc = CleanDocument();
        doc.Blocks.Add(new Paragraph { Runs = { new Run("invisible", new RunFormatting { ColorHex = "#FFFFFF" }) } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().Contain(i => i.Rule == AccessibilityRule.LowContrastText);
    }

    // --- Blank table cells (Tip) ---

    [Fact]
    public void BlankTableCell_Flagged_AsTip()
    {
        var doc = CleanDocument();
        var table = Table.Create(1, 2);
        table.Formatting = table.Formatting with { HeaderRow = true }; // isolate the blank-cell rule
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(PlainRun("filled"));
        // second cell left blank
        doc.Blocks.Add(table);

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().ContainSingle(i => i.Rule == AccessibilityRule.BlankTableCell)
            .Which.Severity.Should().Be(AccessibilitySeverity.Tip);
    }

    [Fact]
    public void BlankTableCell_NotFlagged_WhenAllCellsFilled()
    {
        var doc = CleanDocument();
        var table = Table.Create(1, 2);
        table.Formatting = table.Formatting with { HeaderRow = true };
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(PlainRun("a"));
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Add(PlainRun("b"));
        doc.Blocks.Add(table);

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.BlankTableCell);
    }

    // --- Missing document title (Tip) ---

    [Fact]
    public void MissingDocumentTitle_Flagged_AsTip()
    {
        var doc = CleanDocument();
        doc.Properties.Title = null;

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().ContainSingle(i => i.Rule == AccessibilityRule.MissingDocumentTitle)
            .Which.Severity.Should().Be(AccessibilitySeverity.Tip);
    }

    [Fact]
    public void MissingDocumentTitle_NotFlagged_WhenTitleSet()
    {
        var doc = CleanDocument(); // title is set

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotContain(i => i.Rule == AccessibilityRule.MissingDocumentTitle);
    }

    // --- Ordering, counts and severity ---

    [Fact]
    public void Issues_AreOrderedByDocumentPosition_WithDocumentWideLast()
    {
        var doc = new TextDocument();
        // No title → a document-wide Tip that must sort last.
        // Block 0: image with no alt text (Error).
        doc.Blocks.Add(new Paragraph { Runs = { Run.FromImage(new InlineImage([1], 10, 10)) } });
        // Block 1: low-contrast run (Warning).
        doc.Blocks.Add(new Paragraph { Runs = { new Run("grey", new RunFormatting { ColorHex = "#CCCCCC" }) } });

        var report = AccessibilityChecker.Check(doc);

        report.Issues.Should().NotBeEmpty();
        // First issue anchored to block 0, last issue is the document-wide title tip (BlockIndex -1).
        report.Issues[0].BlockIndex.Should().Be(0);
        report.Issues[^1].Rule.Should().Be(AccessibilityRule.MissingDocumentTitle);
        report.Issues[^1].BlockIndex.Should().Be(-1);
    }

    [Fact]
    public void Counts_MatchIssueSeverities()
    {
        var doc = new TextDocument(); // no title (Tip)
        doc.Blocks.Add(new Paragraph { Runs = { Run.FromImage(new InlineImage([1], 10, 10)) } }); // Error
        doc.Blocks.Add(new Paragraph { Runs = { new Run("grey", new RunFormatting { ColorHex = "#CCCCCC" }) } }); // Warning

        var report = AccessibilityChecker.Check(doc);

        report.ErrorCount.Should().Be(report.Issues.Count(i => i.Severity == AccessibilitySeverity.Error));
        report.WarningCount.Should().Be(report.Issues.Count(i => i.Severity == AccessibilitySeverity.Warning));
        report.TipCount.Should().Be(report.Issues.Count(i => i.Severity == AccessibilitySeverity.Tip));
        report.ErrorCount.Should().BeGreaterThan(0);
        report.IsClean.Should().BeFalse();
    }

    // --- Contrast maths via known colour pairs ---

    [Fact]
    public void Contrast_BlackOnWhite_Passes()
    {
        // Black on white is the maximum 21:1 ratio — must never be flagged.
        var doc = new TextDocument { Properties = { Title = "T" } };
        doc.Blocks.Add(new Paragraph { Runs = { new Run("x", new RunFormatting { ColorHex = "#000000" }) } });

        AccessibilityChecker.Check(doc).Issues
            .Should().NotContain(i => i.Rule == AccessibilityRule.LowContrastText);
    }

    [Theory]
    [InlineData("#777777")] // ~4.48:1 on white — just below AA, flagged
    [InlineData("#999999")] // ~2.85:1 — clearly below, flagged
    public void Contrast_MidGreyOnWhite_Fails(string color)
    {
        var doc = new TextDocument { Properties = { Title = "T" } };
        doc.Blocks.Add(new Paragraph { Runs = { new Run("x", new RunFormatting { ColorHex = color }) } });

        AccessibilityChecker.Check(doc).Issues
            .Should().Contain(i => i.Rule == AccessibilityRule.LowContrastText);
    }

    [Fact]
    public void Contrast_DarkGreyOnWhite_Passes()
    {
        // #595959 on white is ~7:1 — comfortably above AA, must not be flagged.
        var doc = new TextDocument { Properties = { Title = "T" } };
        doc.Blocks.Add(new Paragraph { Runs = { new Run("x", new RunFormatting { ColorHex = "#595959" }) } });

        AccessibilityChecker.Check(doc).Issues
            .Should().NotContain(i => i.Rule == AccessibilityRule.LowContrastText);
    }
}
