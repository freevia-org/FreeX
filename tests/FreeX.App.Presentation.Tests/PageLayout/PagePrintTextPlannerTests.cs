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
    /// R111 regression: Excel's &amp;-code font-size token accepts 1-3 digit sizes (up to 409pt).
    /// &amp;100Report must be parsed as a 100pt size with no leftover digit leaking into the text,
    /// not size 10 followed by literal "0Report".
    /// </summary>
    [Fact]
    public void R111_TokenizeSectionText_ThreeDigitFontSizeCode_ConsumesAllDigits()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&100Report",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        runs.Should().ContainSingle("the whole string is one run after the size code is fully consumed");
        var run = runs[0];
        run.Text.Should().Be("Report", "the leftover '0' must not leak into the visible text");
        run.FontSize.Should().Be(100);
    }

    /// <summary>
    /// R111 no-regression sibling: the existing 2-digit font-size code (&amp;14) must still parse
    /// correctly and must not itself start consuming a following digit that belongs to plain text.
    /// </summary>
    [Fact]
    public void R111_TokenizeSectionText_TwoDigitFontSizeCode_FollowedByDigitText_StillParsesCorrectly()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&14 2027",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        runs.Should().ContainSingle();
        var run = runs[0];
        run.Text.Should().Be(" 2027", "text following the size code (separated by a space) is untouched");
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

    // -----------------------------------------------------------------------
    // R111-app-host-multiline-header-footer-1: multi-line section support
    // -----------------------------------------------------------------------

    /// <summary>
    /// A section string with an embedded literal line break (Alt+Enter in Excel's Header/Footer
    /// editor round-trips as a raw '\n' -- see XlsxWorksheetPageSetupMapper/XlsxFileAdapter) must
    /// still tokenize as a SINGLE run whose text contains the '\n' verbatim: TokenizeSectionText
    /// itself is not responsible for splitting lines, only for parsing '&amp;' format codes.
    /// </summary>
    [Fact]
    public void R111_TokenizeSectionText_EmbeddedNewline_PreservedVerbatimInRunText()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "Confidential\nDo Not Distribute",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        runs.Should().ContainSingle();
        runs[0].Text.Should().Be("Confidential\nDo Not Distribute");
    }

    /// <summary>
    /// R111 core fix: SplitRunsIntoLines must turn a single run whose text contains an embedded '\n'
    /// into two separate lines, each carrying the original run's formatting, with neither line
    /// silently dropped (the defect this fix addresses: WPF's FormattedText.MaxLineCount = 1 and the
    /// portable PDF tier's single fixed baseline both used to show only the first line).
    /// </summary>
    [Fact]
    public void R111_SplitRunsIntoLines_TwoLineSection_ProducesTwoNonEmptyLines()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "Confidential\nDo Not Distribute",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        var lines = PagePrintTextPlanner.SplitRunsIntoLines(runs);

        lines.Should().HaveCount(2, "the embedded '\\n' must produce two printed lines, not one");
        lines[0].Should().ContainSingle(r => r.Text == "Confidential");
        lines[1].Should().ContainSingle(r => r.Text == "Do Not Distribute");
    }

    /// <summary>
    /// No-regression sibling: a plain single-line section (no embedded newline) must still split
    /// into exactly one line, unaffected by the new multi-line logic.
    /// </summary>
    [Fact]
    public void R111_SplitRunsIntoLines_SingleLineSection_ProducesOneLine()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "Confidential",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        var lines = PagePrintTextPlanner.SplitRunsIntoLines(runs);

        lines.Should().ContainSingle();
        lines[0].Should().ContainSingle(r => r.Text == "Confidential");
    }

    /// <summary>
    /// A run's bold/italic/font/color formatting must carry over unchanged to every line it produces
    /// when split -- e.g. "&amp;B" toggled bold before the embedded newline must still apply to both
    /// halves.
    /// </summary>
    [Fact]
    public void R111_SplitRunsIntoLines_FormattingCarriesOverToEveryLine()
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            "&BBold line one\nBold line two",
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        var lines = PagePrintTextPlanner.SplitRunsIntoLines(runs);

        lines.Should().HaveCount(2);
        lines[0].Single().Bold.Should().BeTrue();
        lines[1].Single().Bold.Should().BeTrue("bold was toggled on before the newline and never toggled off");
    }

    /// <summary>
    /// CountSectionLines must count embedded line breaks directly on the raw section string (used to
    /// size the header/footer band before any per-run tokenization), returning at least 1 for an
    /// empty/plain section and growing by one per embedded '\n'.
    /// </summary>
    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("Plain", 1)]
    [InlineData("Line one\nLine two", 2)]
    [InlineData("Line one\nLine two\nLine three", 3)]
    [InlineData("Line one\r\nLine two", 2)]
    public void R111_CountSectionLines_ReturnsExpectedLineCount(string? text, int expected)
    {
        PagePrintTextPlanner.CountSectionLines(text).Should().Be(expected);
    }
}
