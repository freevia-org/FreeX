using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PagePrintTextPlannerTests
{
    [Fact]
    public void ExpandHeaderFooterText_ExpandsExcelHeaderFooterTokens()
    {
        var now = new DateTime(2026, 5, 22, 13, 45, 0);

        PagePrintTextPlanner.ExpandHeaderFooterText(
                "&[Date] &[Time] &[File] &[Path] &[Tab] &[Page]/&[Pages] &D &T &F &Z &A &P/&N &[Picture]",
                pageNumber: 2,
                totalPages: 5,
                workbookName: "Budget.xlsx",
                sheetName: "Summary",
                now)
            .Should()
            .Be($"{now:d} {now:t} Budget.xlsx Budget.xlsx Summary 2/5 {now:d} {now:t} Budget.xlsx Budget.xlsx Summary 2/5 ");
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
}
