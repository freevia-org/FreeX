using System;
using System.Linq;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using Xunit;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Cleanup batch MED14 — round-10 MED/LOW findings.
/// </summary>
public sealed class FreeXCleanupMED14Tests
{
    // P101 (LOW): Excel superscript/subscript header codes &X/&Y were unrecognized by the tokenizer's
    // switch (only '+'/'-' were handled) and fell to the default branch, which printed a literal '&'
    // followed by the letter. Verify &X/&Y are now consumed like &+/&- (no literal "&X"/"&Y" leaks
    // into the rendered text), case-insensitively, matching Excel's documented format codes.
    [Theory]
    [InlineData("Normal&XSuper&Xtext", "NormalSupertext")]
    [InlineData("Normal&YSub&Ytext", "NormalSubtext")]
    [InlineData("Normal&xSuper&xtext", "NormalSupertext")]
    [InlineData("Normal&ySub&ytext", "NormalSubtext")]
    public void TokenizeSectionText_SuperscriptSubscriptCodes_AreConsumedNotPrintedLiterally(
        string sectionText,
        string expectedText)
    {
        var runs = PagePrintTextPlanner.TokenizeSectionText(
            sectionText,
            pageNumber: 1, totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: "",
            sheetName: "Sheet1",
            now: new DateTime(2026, 1, 1));

        var text = string.Concat(runs.Select(r => r.Text));
        text.Should().Be(expectedText);
        text.Should().NotContain("&X", "the &X superscript code must not print as literal text");
        text.Should().NotContain("&Y", "the &Y subscript code must not print as literal text");
    }
}
