using FluentAssertions;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintPreviewDialogPlannerTests
{
    [Theory]
    [InlineData(null, "Book1")]
    [InlineData("", "Book1")]
    [InlineData("  Book1  ", "Book1")]
    public void NormalizeWorkbookName_TrimsAndDefaultsBlankNames(string? workbookName, string expected)
    {
        PrintPreviewDialogPlanner.NormalizeWorkbookName(workbookName).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, false, 0)]
    [InlineData("", false, 0)]
    [InlineData("0", false, 0)]
    [InlineData("2", true, 2)]
    [InlineData("999", true, 999)]
    [InlineData("1000", false, 0)]
    [InlineData("not a number", false, 0)]
    public void TryParseCopyCount_ValidatesExcelCopiesRange(string? text, bool expectedResult, int expectedCopies)
    {
        PrintPreviewDialogPlanner.TryParseCopyCount(text, out var copies).Should().Be(expectedResult);
        copies.Should().Be(expectedCopies);
    }

    [Theory]
    [InlineData(null, 5, false, 0)]
    [InlineData("", 5, false, 0)]
    [InlineData("0", 5, false, 0)]
    [InlineData("1", 5, true, 1)]
    [InlineData("5", 5, true, 5)]
    [InlineData("6", 5, false, 0)]
    [InlineData("2.5", 5, false, 0)]
    [InlineData("not a number", 5, false, 0)]
    public void TryParsePageNumber_ValidatesPreviewPageRange(
        string? text,
        int totalPages,
        bool expectedResult,
        int expectedPage)
    {
        PrintPreviewDialogPlanner.TryParsePageNumber(text, totalPages, out var pageNumber).Should().Be(expectedResult);
        pageNumber.Should().Be(expectedPage);
    }

    [Fact]
    public void CreateNavigationCommandPlans_ProvidesStableToolbarOrder()
    {
        var plans = PrintPreviewDialogPlanner.CreateNavigationCommandPlans();

        plans.Select(plan => plan.Command).Should().Equal(
            PrintPreviewToolbarCommand.FirstPage,
            PrintPreviewToolbarCommand.PreviousPage,
            PrintPreviewToolbarCommand.NextPage,
            PrintPreviewToolbarCommand.LastPage);
        plans.Select(plan => plan.AutomationId).Should().Equal(
            PrintPreviewDialogPlanner.FirstPageButtonAutomationId,
            PrintPreviewDialogPlanner.PreviousPageButtonAutomationId,
            PrintPreviewDialogPlanner.NextPageButtonAutomationId,
            PrintPreviewDialogPlanner.LastPageButtonAutomationId);
    }

    [Fact]
    public void CreateToolbarCommandPlan_ProvidesSharedChromeMetadata()
    {
        var print = PrintPreviewDialogPlanner.CreateToolbarCommandPlan(PrintPreviewToolbarCommand.Print);
        var margins = PrintPreviewDialogPlanner.CreateToolbarCommandPlan(PrintPreviewToolbarCommand.Margins);
        var close = PrintPreviewDialogPlanner.CreateToolbarCommandPlan(PrintPreviewToolbarCommand.Close);

        print.AutomationId.Should().Be(PrintPreviewDialogPlanner.PrintButtonAutomationId);
        print.ContentResourceKey.Should().Be("PrintPreview_PrintButton");
        print.ToolTipResourceKey.Should().Be("PrintPreview_PrintToolTip");
        margins.AutomationNameResourceKey.Should().Be("PrintPreview_MarginsAutomationName");
        close.AutomationId.Should().Be(PrintPreviewDialogPlanner.CloseButtonAutomationId);
        close.HelpTextResourceKey.Should().Be("PrintPreview_CloseHelpText");
    }
}
