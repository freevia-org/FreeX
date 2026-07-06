using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-10 COMMENTS-HOST fixes: P6 -- the Avalonia/portable PDF pipeline
/// (<see cref="PortablePdfExportPlanner"/>) must honor <see cref="Sheet.PrintComments"/> the same
/// way the WPF PrintRenderer does, instead of silently omitting every note/threaded comment from
/// the printout.
/// </summary>
public sealed class FreeXReview10CommentsHostTests
{
    [Fact]
    public void CreatePlan_WithPrintCommentsAsDisplayed_AttachesOverlayToOwningPage()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
        var addr = new CellAddress(sheet.Id, 2, 2);
        sheet.Comments[addr] = "Check this total";

        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 20),
            WorkbookExportPrintSurface.MacOs);

        // Pre-fix: PortablePdfExportPlanner.CreatePlan(exportPrintPlan) had no workbook/comment
        // awareness at all, so no page request ever carried comment data -- "As displayed" and "At
        // end of sheet" were indistinguishable (both silently produced nothing).
        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan, workbook);

        plan.IsReady.Should().BeTrue();
        plan.PageRequests.Should().ContainSingle();
        var page = plan.PageRequests[0];
        page.IsCommentSummaryPage.Should().BeFalse();
        page.DisplayedComments.Should().ContainSingle();
        page.DisplayedComments[0].Address.Should().Be(addr);
        page.DisplayedComments[0].Text.Should().Be("Check this total");
    }

    [Fact]
    public void CreatePlan_WithPrintCommentsAtEnd_AppendsCommentSummaryPageAfterGridPages()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        var addr = new CellAddress(sheet.Id, 2, 2);
        sheet.ThreadedComments[addr] = new ThreadedComment("Please verify", "Alice");

        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 20),
            WorkbookExportPrintSurface.MacOs);

        // Pre-fix: total page count was exactly the grid-page count (1 here) regardless of
        // PrintComments, and no page ever carried comment-summary entries -- threaded comments/notes
        // were entirely absent from the portable PDF/print output. Post-fix: an extra "at end of
        // sheet" summary page is appended, carrying the threaded comment's formatted entry.
        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan, workbook);

        plan.IsReady.Should().BeTrue();
        plan.PageRequests.Should().HaveCount(2);

        var gridPage = plan.PageRequests[0];
        gridPage.IsCommentSummaryPage.Should().BeFalse();

        var summaryPage = plan.PageRequests[1];
        summaryPage.IsCommentSummaryPage.Should().BeTrue();
        summaryPage.CommentSummaryEntries.Should().ContainSingle();
        summaryPage.CommentSummaryEntries[0].Address.Should().Be(addr);
        summaryPage.CommentSummaryEntries[0].Text.Should().Contain("Please verify");
        summaryPage.CommentSummaryEntries[0].Text.Should().Contain("Alice");
    }

    [Fact]
    public void CreatePlan_WithPrintCommentsNone_OmitsCommentDataEvenWhenWorkbookSupplied()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintComments = WorksheetPrintComments.None;
        var addr = new CellAddress(sheet.Id, 2, 2);
        sheet.Comments[addr] = "Should not print";
        sheet.ThreadedComments[addr] = new ThreadedComment("Should not print either", "Alice");

        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 20),
            WorkbookExportPrintSurface.MacOs);

        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan, workbook);

        plan.IsReady.Should().BeTrue();
        plan.PageRequests.Should().ContainSingle();
        plan.PageRequests[0].DisplayedComments.Should().BeEmpty();
        plan.PageRequests[0].IsCommentSummaryPage.Should().BeFalse();
    }

    [Fact]
    public void CreatePlan_WithoutWorkbook_PreservesLegacyBehaviorForExistingCallers()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.ThreadedComments[new CellAddress(sheet.Id, 2, 2)] = new ThreadedComment("Note", "Alice");

        var selectedRange = GridRange.Parse("A1:E6", sheet.Id);
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 20),
            WorkbookExportPrintSurface.MacOs);

        // Existing call sites (MainWindow.cs / MainWindow.Print.cs) call CreatePlan(exportPrintPlan)
        // with no workbook argument -- that must keep behaving exactly as before this fix.
        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);

        plan.IsReady.Should().BeTrue();
        plan.PageRequests.Should().ContainSingle();
        plan.PageRequests[0].IsCommentSummaryPage.Should().BeFalse();
    }
}
