using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PagePrintTextPlannerTests
{
    // -----------------------------------------------------------------------
    // Legacy ExpandHeaderFooterText (flat string) — regression tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ExpandHeaderFooterText_ExpandsExcelHeaderFooterTokens()
    {
        var now = new DateTime(2026, 5, 22, 13, 45, 0);

        // &[Path]/&Z returns the workbook DIRECTORY; with no directory supplied it is empty.
        // &[File]/&F returns the workbook filename.
        PagePrintTextPlanner.ExpandHeaderFooterText(
                "&[Date] &[Time] &[File] &[Path] &[Tab] &[Page]/&[Pages] &D &T &F &Z &A &P/&N &[Picture]",
                pageNumber: 2,
                totalPages: 5,
                workbookName: "Budget.xlsx",
                workbookDirectory: @"C:\Docs\",
                sheetName: "Summary",
                now)
            .Should()
            .Be($"{now:d} {now:t} Budget.xlsx C:\\Docs\\ Summary 2/5 {now:d} {now:t} Budget.xlsx C:\\Docs\\ Summary 2/5 ");
    }

    [Fact]
    public void ExpandHeaderFooterText_LegacyOverload_PathReturnsEmpty()
    {
        // The 6-parameter overload (no workbookDirectory) keeps &[Path]/&Z as empty string,
        // preserving backward compatibility for callers that don't know the path.
        var result = PagePrintTextPlanner.ExpandHeaderFooterText(
            "&[File]-&[Path]",
            pageNumber: 1,
            totalPages: 1,
            workbookName: "Book.xlsx",
            sheetName: "Sheet1",
            now: new DateTime(2026, 5, 22));

        result.Should().Be("Book.xlsx-");
    }

    [Fact]
    public void ExpandHeaderFooterText_TreatsNullAsEmptyAndRemovesPictureTokens()
    {
        PagePrintTextPlanner.ExpandHeaderFooterText(
                null,
                pageNumber: 1,
                totalPages: 1,
                workbookName: "Book.xlsx",
                sheetName: "Sheet1",
                new DateTime(2026, 5, 22))
            .Should()
            .BeEmpty();

        PagePrintTextPlanner.ExpandHeaderFooterText(
                "Logo &[Picture] &G",
                pageNumber: 1,
                totalPages: 1,
                workbookName: "Book.xlsx",
                sheetName: "Sheet1",
                new DateTime(2026, 5, 22))
            .Should()
            .Be("Logo  ");
    }

    [Theory]
    [InlineData("#DIV/0!", WorksheetPrintErrorValue.Displayed, "#DIV/0!")]
    [InlineData("#VALUE!", WorksheetPrintErrorValue.Blank, "")]
    [InlineData("#REF!", WorksheetPrintErrorValue.Dash, "--")]
    [InlineData("#NAME?", WorksheetPrintErrorValue.NotAvailable, "#N/A")]
    [InlineData("plain", WorksheetPrintErrorValue.Dash, "plain")]
    public void FormatPrintedCellText_AppliesWorksheetErrorPolicy(
        string displayText,
        WorksheetPrintErrorValue printErrorValue,
        string expected)
    {
        PagePrintTextPlanner.FormatPrintedCellText(displayText, printErrorValue).Should().Be(expected);
    }

    // -----------------------------------------------------------------------
    // PR3: &Z / &[Path] returns workbook DIRECTORY; &F / &[File] returns filename
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenizeSectionText_ZReturnsDirectory_FReturnsFilename()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&Z&F",
            pageNumber: 1, totalPages: 1,
            workbookName: "Budget.xlsx",
            workbookDirectory: @"C:\Users\quinn\Documents\",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        // Flat text: directory + filename
        var flat = string.Concat(runs.Select(r => r.Text));
        flat.Should().Be(@"C:\Users\quinn\Documents\Budget.xlsx");
    }

    [Fact]
    public void TokenizeSectionText_BracketedPathReturnsDirectory_BracketedFileReturnsFilename()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&[Path]&[File]",
            pageNumber: 1, totalPages: 1,
            workbookName: "Report.xlsx",
            workbookDirectory: @"C:\Reports\",
            sheetName: "Data",
            now: new DateTime(2026, 1, 1));

        var flat = string.Concat(runs.Select(r => r.Text));
        flat.Should().Be(@"C:\Reports\Report.xlsx");
    }

    [Fact]
    public void TokenizeSectionText_UnsavedWorkbook_ZReturnsEmpty()
    {
        // When workbookDirectory is empty (unsaved workbook) &Z should produce empty string.
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&Z&F",
            pageNumber: 1, totalPages: 1,
            workbookName: "Unsaved.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        var flat = string.Concat(runs.Select(r => r.Text));
        flat.Should().Be("Unsaved.xlsx");
    }

    // -----------------------------------------------------------------------
    // PR2: Font / style codes — tokenizer produces correct runs
    // -----------------------------------------------------------------------

    /// <summary>
    /// &amp;BPage &amp;P&amp;B of &amp;N → a single bold run "Page &lt;n&gt; of &lt;m&gt;".
    /// The &amp;B codes toggle bold on/off and the value tokens are expanded.
    /// </summary>
    [Fact]
    public void TokenizeSectionText_BoldToggleAndPageTokens()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&BPage &P&B of &N",
            pageNumber: 3, totalPages: 10,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        // Expect: runs while bold=true contain "Page 3", then bold toggles off → " of 10".
        // Exact run count may vary but the bold run must contain "Page 3".
        var boldRuns = runs.Where(r => r.Bold).ToList();
        boldRuns.Should().NotBeEmpty("&B should produce at least one bold run");
        var boldText = string.Concat(boldRuns.Select(r => r.Text));
        boldText.Should().Be("Page 3", "the text between the two &B codes should be bold");

        var nonBoldText = string.Concat(runs.Where(r => !r.Bold).Select(r => r.Text));
        nonBoldText.Should().Be(" of 10", "text after the second &B should not be bold");

        // No run should have the literal text "&B"
        runs.Should().NotContain(r => r.Text.Contains("&B"), "format codes must not appear as literal text");
    }

    /// <summary>
    /// &amp;"Arial,Bold Italic"&amp;14Title → one run: fontName Arial, bold, italic, size 14.
    /// </summary>
    [Fact]
    public void TokenizeSectionText_FontNameAndSizeCode()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&\"Arial,Bold Italic\"&14Title",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        runs.Should().ContainSingle("a single text run 'Title' is expected");
        var run = runs[0];
        run.Text.Should().Be("Title");
        run.FontName.Should().Be("Arial");
        run.Bold.Should().BeTrue("'Bold Italic' style sets bold");
        run.Italic.Should().BeTrue("'Bold Italic' style sets italic");
        run.FontSize.Should().Be(14);
    }

    /// <summary>
    /// &amp;Kff0000Red&amp;K000000Black → two runs with different colors.
    /// </summary>
    [Fact]
    public void TokenizeSectionText_ColorCodes_ProduceColoredRuns()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&Kff0000Red&K000000Black",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        runs.Should().HaveCount(2);
        var redRun = runs[0];
        redRun.Text.Should().Be("Red");
        redRun.Color.Should().NotBeNull();
        redRun.Color!.Value.R.Should().Be(0xFF);
        redRun.Color!.Value.G.Should().Be(0x00);
        redRun.Color!.Value.B.Should().Be(0x00);

        var blackRun = runs[1];
        blackRun.Text.Should().Be("Black");
        blackRun.Color!.Value.R.Should().Be(0x00);
        blackRun.Color!.Value.G.Should().Be(0x00);
        blackRun.Color!.Value.B.Should().Be(0x00);
    }

    /// <summary>
    /// &amp;&amp; → literal '&amp;'.
    /// </summary>
    [Fact]
    public void TokenizeSectionText_DoubleAmpersand_ProducesLiteralAmpersand()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&&",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        runs.Should().ContainSingle();
        runs[0].Text.Should().Be("&");
    }

    /// <summary>
    /// &amp;I&amp;Uitalic-underline&amp;U&amp;I → the middle text should be italic+underline,
    /// then both toggled off; no format codes should appear as literal text.
    /// </summary>
    [Fact]
    public void TokenizeSectionText_ItalicUnderlineToggle()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&I&Uitalic-underline&U&I",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        // The middle run should be italic + underline
        var formattedRun = runs.Single(r => r.Text == "italic-underline");
        formattedRun.Italic.Should().BeTrue();
        formattedRun.Underline.Should().BeTrue();

        // No literal format-code text
        runs.Should().NotContain(r => r.Text.Contains('&'), "format codes must not appear as literal text");
    }

    /// <summary>
    /// Plain section with only value placeholders — still expands correctly as one run.
    /// </summary>
    [Fact]
    public void TokenizeSectionText_PlainValuePlaceholders_SinglePlainRun()
    {
        var now = new DateTime(2026, 5, 22, 13, 45, 0);
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&F - Page &P of &N",
            pageNumber: 2, totalPages: 5,
            workbookName: "Budget.xlsx",
            workbookDirectory: "",
            sheetName: "Summary",
            now);

        runs.Should().ContainSingle("no format codes → single plain run");
        var run = runs[0];
        run.Text.Should().Be("Budget.xlsx - Page 2 of 5");
        run.Bold.Should().BeFalse();
        run.Italic.Should().BeFalse();
        run.FontName.Should().BeNull();
        run.FontSize.Should().BeNull();
        run.Color.Should().BeNull();
    }

    /// <summary>
    /// Regression: no codes at all, value placeholders still expand.
    /// </summary>
    [Fact]
    public void ExpandHeaderFooterText_PlainPlaceholders_StillExpand()
    {
        var now = new DateTime(2026, 5, 22, 13, 45, 0);
        var result = PagePrintTextPlanner.ExpandHeaderFooterText(
            "&F - &D",
            pageNumber: 1, totalPages: 1,
            workbookName: "Budget.xlsx",
            sheetName: "Summary",
            now);

        result.Should().Be($"Budget.xlsx - {now:d}");
    }
}
