using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-135 finding: with <see cref="Sheet.PrintAreas"/> holding more than one configured print
/// area and <see cref="Sheet.PrintComments"/> == <see cref="WorksheetPrintComments.AtEnd"/>,
/// <see cref="PortablePdfExportPlanner"/> used to emit the "comments at end of sheet" appendix once
/// PER PRINT AREA, interleaved right after that area's own grid pages, instead of once at the very
/// end of the sheet -- because <c>WorkbookExportPrintPlan.SheetPlans</c> has one entry per print
/// AREA (not per sheet), and the old code fired the AtEnd check inside the per-area loop.
/// <see cref="FreeX.App.Host.PrintRenderer"/> (WPF) gets this right: it builds ONE
/// <c>WorksheetPrintRenderPlan</c> per sheet spanning every configured print area, computes the
/// comment-summary pages once from that combined plan, and appends them once after all of that
/// sheet's grid pages -- so WPF is the side that already matches Excel (a single appendix at the end
/// of the sheet, never duplicated/interleaved per area).
/// </summary>
public sealed class R135_PortablePdfMultiAreaCommentSummaryTests
{
    [Fact]
    public void CreatePlan_WithTwoPrintAreasAndCommentsAtEnd_AppendsSummaryOnceAfterBothAreas()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.ThreadedComments[new CellAddress(sheet.Id, 2, 2)] =
            new ThreadedComment("Please verify", "Alice");

        // Two small, disjoint, non-adjacent print areas so each area paginates to exactly one grid
        // page under the 20x20 page capacity used below.
        sheet.SetPrintAreas(new[]
        {
            GridRange.Parse("A1:B2", sheet.Id),
            GridRange.Parse("D1:E2", sheet.Id)
        });

        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 20),
            WorkbookExportPrintSurface.MacOs);

        // Sanity: the underlying export plan really does carry two separate per-area sheet plans
        // (this is what made the old code's "once per sheetPlan" loop wrong).
        exportPrintPlan.SheetPlans.Should().HaveCount(2);

        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan, workbook);

        plan.IsReady.Should().BeTrue();

        // Pre-fix: this was 4 requests -- [area1 grid][area1 AtEnd summary][area2 grid][area2 AtEnd
        // summary] -- the appendix duplicated and interleaved between the two print areas instead of
        // appearing once at the end. Post-fix: exactly one grid page per area, plus exactly one
        // summary page appended after BOTH areas' grid pages.
        plan.PageRequests.Should().HaveCount(3);

        plan.PageRequests[0].IsCommentSummaryPage.Should().BeFalse("the first print area's grid page");
        plan.PageRequests[1].IsCommentSummaryPage.Should().BeFalse("the second print area's grid page");
        plan.PageRequests[2].IsCommentSummaryPage.Should().BeTrue(
            "the AtEnd comment appendix must come after every print area's grid pages, not between them");

        var summaryPage = plan.PageRequests[2];
        summaryPage.CommentSummaryEntries.Should().ContainSingle(
            "the comment must be listed exactly once, not once per print area");
        summaryPage.CommentSummaryEntries[0].Text.Should().Contain("Please verify");
    }

    /// <summary>
    /// Sibling/no-regression: multiple SHEETS (as opposed to multiple areas on one sheet) each with
    /// their own single print area and PrintComments == AtEnd must each still get their own summary
    /// appendix, appended right after that sheet's own grid pages -- proving the "last area for this
    /// sheet" boundary the fix introduced correctly resets at every sheet change, not just within one
    /// sheet's run of areas, and that a group-size-of-one (no configured PrintAreas at all) still
    /// fires the appendix.
    /// </summary>
    [Fact]
    public void CreatePlan_WithTwoSheetsEachAtEnd_AppendsOneSummaryPerSheetInOrder()
    {
        var workbook = new Workbook("Budget");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.PrintComments = WorksheetPrintComments.AtEnd;
        sheet1.ThreadedComments[new CellAddress(sheet1.Id, 1, 1)] =
            new ThreadedComment("Sheet1 note", "Alice");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("x"));

        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.PrintComments = WorksheetPrintComments.AtEnd;
        sheet2.ThreadedComments[new CellAddress(sheet2.Id, 1, 1)] =
            new ThreadedComment("Sheet2 note", "Bob");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("y"));

        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.VisibleWorkbook,
                WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 20),
            WorkbookExportPrintSurface.MacOs);

        exportPrintPlan.SheetPlans.Should().HaveCount(2);

        var plan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan, workbook);

        plan.IsReady.Should().BeTrue();
        plan.PageRequests.Should().HaveCount(4);

        plan.PageRequests[0].IsCommentSummaryPage.Should().BeFalse();
        plan.PageRequests[1].IsCommentSummaryPage.Should().BeTrue();
        plan.PageRequests[1].CommentSummaryEntries.Should().ContainSingle();
        plan.PageRequests[1].CommentSummaryEntries[0].Text.Should().Contain("Sheet1 note");

        plan.PageRequests[2].IsCommentSummaryPage.Should().BeFalse();
        plan.PageRequests[3].IsCommentSummaryPage.Should().BeTrue();
        plan.PageRequests[3].CommentSummaryEntries.Should().ContainSingle();
        plan.PageRequests[3].CommentSummaryEntries[0].Text.Should().Contain("Sheet2 note");
    }
}
