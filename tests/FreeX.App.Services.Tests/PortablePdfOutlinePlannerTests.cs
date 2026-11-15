using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PortablePdfOutlinePlannerTests
{
    [Fact]
    public void Build_UsesTheGroupedExportPagesAndFormatsConfiguredPrintTitles()
    {
        var (workbook, plan) = CreateGroupedExport();

        PortablePdfOutlinePlanner.Build(workbook, plan.PageRequests, PdfBookmarkMode.None)
            .Should().BeEmpty();
        PortablePdfOutlinePlanner.Build(workbook, plan.PageRequests, PdfBookmarkMode.SheetNames)
            .Should().Equal(new PdfOutlineEntry("Café", 0), new PdfOutlineEntry("Annexe", 3));
        PortablePdfOutlinePlanner.Build(workbook, plan.PageRequests, PdfBookmarkMode.PrintTitles)
            .Should().Equal(new PdfOutlineEntry("Café (Rows 1-2, Columns 1)", 0), new PdfOutlineEntry("Annexe", 3));
        PortablePdfOutlinePlanner.Build(workbook, plan.PageRequests, PdfBookmarkMode.PageNumbers)
            .Should().Equal(
                new PdfOutlineEntry("Page 1", 0),
                new PdfOutlineEntry("Page 2", 1),
                new PdfOutlineEntry("Page 3", 2),
                new PdfOutlineEntry("Page 4", 3));
    }

    [Theory]
    [InlineData(PdfBookmarkMode.SheetNames)]
    [InlineData(PdfBookmarkMode.PrintTitles)]
    public void Build_DropsExcludedWorksheetDestinationsAndRebasesRetainedDestinations(PdfBookmarkMode mode)
    {
        var (workbook, plan) = CreateGroupedExport();
        var range = PortablePdfExportPlanner.ApplyPageRange(plan, new ExportPageRange(2, 4));

        PortablePdfOutlinePlanner.Build(workbook, range.PageRequests, mode)
            .Should().Equal(new PdfOutlineEntry("Annexe", 2));

        var laterRange = PortablePdfExportPlanner.ApplyPageRange(range, new ExportPageRange(2, 3));
        PortablePdfOutlinePlanner.Build(workbook, laterRange.PageRequests, mode)
            .Should().Equal(new PdfOutlineEntry("Annexe", 1));

        var onlyCommentPage = PortablePdfExportPlanner.ApplyPageRange(laterRange, new ExportPageRange(1, 1));
        PortablePdfOutlinePlanner.Build(workbook, onlyCommentPage.PageRequests, mode)
            .Should().BeEmpty();
    }

    [Fact]
    public void Build_PreservesOriginalPageLabelsWhenFilteringAgain()
    {
        var (workbook, plan) = CreateGroupedExport();
        var range = PortablePdfExportPlanner.ApplyPageRange(plan, new ExportPageRange(2, 4));
        var laterRange = PortablePdfExportPlanner.ApplyPageRange(range, new ExportPageRange(2, 3));

        PortablePdfOutlinePlanner.Build(workbook, range.PageRequests, PdfBookmarkMode.PageNumbers)
            .Should().Equal(
                new PdfOutlineEntry("Page 2", 0),
                new PdfOutlineEntry("Page 3", 1),
                new PdfOutlineEntry("Page 4", 2));
        PortablePdfOutlinePlanner.Build(workbook, laterRange.PageRequests, PdfBookmarkMode.PageNumbers)
            .Should().Equal(new PdfOutlineEntry("Page 3", 0), new PdfOutlineEntry("Page 4", 1));
    }

    private static (Workbook Workbook, PortablePdfExportPlan Plan) CreateGroupedExport()
    {
        var workbook = new Workbook("Report");
        var details = workbook.AddSheet("Café");
        var firstArea = GridRange.Parse("B4:C4", details.Id);
        var secondArea = GridRange.Parse("F8:G8", details.Id);
        details.SetPrintAreas([firstArea, secondArea]);
        details.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        details.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        details.PrintComments = WorksheetPrintComments.AtEnd;
        details.Comments[new CellAddress(details.Id, 4, 2)] = "Review this forecast.";
        workbook.AddSheet("Outside group");
        var blank = workbook.AddSheet("Annexe");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var intent = new WorkbookExportPrintIntent(WorkbookExportPrintScope.GroupedSheets, WorkbookExportPrintOutputKind.Pdf)
        {
            GroupedSheetIds = new HashSet<SheetId> { blank.Id, hidden.Id, details.Id }
        };
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        var plan = PortablePdfExportPlanner.CreatePlan(printPlan, workbook);
        plan.PageRequests.Select(page => page.PrintRange.Start.Sheet)
            .Should().Equal(details.Id, details.Id, details.Id, blank.Id);
        plan.PageRequests.Take(2).Select(page => page.PrintRange).Should().Equal(firstArea, secondArea);
        plan.PageRequests[2].IsCommentSummaryPage.Should().BeTrue();
        return (workbook, plan);
    }
}
