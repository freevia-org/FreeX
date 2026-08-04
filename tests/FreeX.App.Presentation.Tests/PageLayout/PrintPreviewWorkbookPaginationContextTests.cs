using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintPreviewWorkbookPaginationContextTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void TryCreate_UsesVisibleSheetsInWorkbookOrder_AndSkipsEmptyAndHiddenSheets()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var empty = workbook.AddSheet("Empty");
        var hidden = workbook.AddSheet("Hidden");
        var second = workbook.AddSheet("Second");

        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("first"));
        second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("second"));
        hidden.SetCell(new CellAddress(hidden.Id, 1, 1), new TextValue("hidden"));
        hidden.IsHidden = true;

        PrintPreviewWorkbookPaginationContext.TryCreate(workbook, Measurer, out var context)
            .Should().BeTrue();

        context.Pages.Select(page => page.SheetName)
            .Should().Equal("First", "Second");
        context.Pages.Should().OnlyContain(page => page.Kind == PrintPreviewWorkbookPageKind.Worksheet);
    }

    [Fact]
    public void TryCreate_CombinesDifferentPageCountsAndCommentsAppendix()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");

        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("first"));
        first.PrintComments = WorksheetPrintComments.AtEnd;
        first.Comments[new CellAddress(first.Id, 1, 1)] = "Printed note";
        first.Comments[new CellAddress(first.Id, 20, 20)] = "Not printed";
        first.PrintArea = new GridRange(
            new CellAddress(first.Id, 1, 1),
            new CellAddress(first.Id, 1, 1));
        first.FirstPageNumber = 3;
        first.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");

        for (uint row = 1; row <= 400; row += 50)
            second.SetCell(new CellAddress(second.Id, row, 10), new NumberValue(row));
        second.SetPrintAreas(
        [
            new GridRange(
                new CellAddress(second.Id, 1, 10),
                new CellAddress(second.Id, 1, 10)),
            new GridRange(
                new CellAddress(second.Id, 1, 10),
                new CellAddress(second.Id, 400, 70)),
        ]);
        second.FirstPageNumber = 10;
        second.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");

        PrintPreviewWorkbookPaginationContext.TryCreate(workbook, Measurer, out var context)
            .Should().BeTrue();

        context.PageCount.Should().BeGreaterThan(3);
        context.Pages[0].Should().Match<PrintPreviewWorkbookPageInfo>(page =>
            page.SheetName == "First" &&
            page.Kind == PrintPreviewWorkbookPageKind.Worksheet &&
            page.PrintedPageNumber == 3);
        context.Pages[1].Should().Match<PrintPreviewWorkbookPageInfo>(page =>
            page.SheetName == "First" &&
            page.Kind == PrintPreviewWorkbookPageKind.CommentSummary);
        context.Pages.Skip(2).Should().OnlyContain(page => page.SheetName == "Second");

        var secondFirstPage = context.Pages[2];
        secondFirstPage.PrintedPageNumber.Should().Be(12);
        context.BuildPage(2)!.FooterRuns.Select(run => run.Text)
            .Should().ContainSingle($"Page 12 of {context.PageCount}");

        context.BuildPage(1).Should().BeNull();
        var appendix = context.BuildPainting(1);
        appendix.Should().NotBeNull();
        appendix!.Instructions.Select(instruction => instruction.Text)
            .Should().Contain("Comments")
            .And.Contain("A1: Printed note")
            .And.NotContain("T20: Not printed");
    }

    [Fact]
    public void TryCreate_IgnorePrintAreaUsesUsedRangeAndRepaginationCanReturnToActiveSheetScope()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("inside used range"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 20), new TextValue("outside print area"));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        PrintPreviewWorkbookPaginationContext.TryCreate(workbook, Measurer, out var workbookContext)
            .Should().BeTrue();
        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var activeContext)
            .Should().BeTrue();
        PrintPreviewPaginationContext.TryCreate(
                workbook,
                sheet,
                Measurer,
                out var ignoredAreaContext,
                ignorePrintArea: true)
            .Should().BeTrue();

        workbookContext.Pages.Should().ContainSingle();
        activeContext.PageCount.Should().Be(1);
        ignoredAreaContext.PageCount.Should().BeGreaterThan(activeContext.PageCount);
    }

    [Fact]
    public void TryCreate_AllEmptySheetsReturnsFalse()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Empty 1");
        workbook.AddSheet("Empty 2");

        PrintPreviewWorkbookPaginationContext.TryCreate(workbook, Measurer, out _)
            .Should().BeFalse();
    }
}
