using Free.Shared.Pdf;
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
        // "As displayed on sheet" prints a box only for notes that are actually shown (pinned open),
        // matching Excel — so pin this note, otherwise it is correctly excluded from the overlay set.
        sheet.ShownComments.Add(addr);

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BuildPdf_PaintsOnlyShownCommentsWithTheirIndicatorAndPrintColors(
        bool legacy, bool blackAndWhite)
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
        sheet.PrintBlackAndWhite = blackAndWhite;
        var note = new CellAddress(sheet.Id, 2, 2);
        var thread = new CellAddress(sheet.Id, 3, 3);
        var outside = new CellAddress(sheet.Id, 7, 7);
        sheet.Comments[note] = "Check the total";
        sheet.ThreadedComments[thread] = new ThreadedComment("Confirm the balance", "Alice");
        sheet.Comments[new CellAddress(sheet.Id, 4, 4)] = "Unpinned note";
        sheet.Comments[outside] = "Outside selection";
        sheet.ShownComments.Add(note);
        sheet.ShownComments.Add(thread);
        sheet.ShownComments.Add(outside);

        var plan = PlanSelectedRange(workbook, sheet);
        var page = legacy
            ? WorkbookPdfContentBuilder.BuildPage(workbook, plan, plan.PageRequests[0], new PortablePdfDocumentOptions())
            : WorkbookPdfContentBuilder.BuildPageWithPageSetup(workbook, plan, plan.PageRequests[0]);

        var ops = page.Ops;
        var noteColor = blackAndWhite ? PdfColor.Black : new PdfColor(192, 0, 0);
        var threadColor = blackAndWhite ? PdfColor.Black : new PdfColor(124, 55, 158);
        ops.OfType<PdfFilledTriangle>().Select(op => op.Color).Should().Equal(noteColor, threadColor);
        var boxFill = blackAndWhite ? new PdfColor(255, 255, 255) : new PdfColor(255, 255, 225);
        ops.OfType<PdfFillRect>().Where(op => op.Color == boxFill).Should().HaveCount(2);
        var text = string.Join(" ", ops.OfType<PdfText>().Select(op => op.Text));
        text.Should().Contain("Check the total").And.Contain("Confirm the balance").And.Contain("Alice");
        text.Should().NotContain("Unpinned note").And.NotContain("Outside selection");
    }

    [Fact]
    public void DraftQuality_OmitsDisplayedCommentFromRenderingAndPortableTextPreflight()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
        sheet.Comments[address] = "Неподдържан текст";
        sheet.ShownComments.Add(address);

        var displayed = PlanSelectedRange(workbook, sheet);
        var unsupported = PortablePdfTextCapabilityPlanner.CreatePlan(workbook, displayed);
        unsupported.IsReady.Should().BeFalse();
        var diagnostic = unsupported.UnsupportedTextDiagnostics.Should().ContainSingle().Which;
        diagnostic.Source.Should().Be(PortablePdfTextRunSource.DisplayedComment);
        diagnostic.Row.Should().Be(address.Row);
        diagnostic.Column.Should().Be(address.Col);

        sheet.PrintDraftQuality = true;
        var draft = PlanSelectedRange(workbook, sheet);
        draft.PageRequests[0].DisplayedComments.Should().BeEmpty();
        PortablePdfTextCapabilityPlanner.CreatePlan(workbook, draft).IsReady.Should().BeTrue();
        WorkbookPdfContentBuilder.BuildPageWithPageSetup(workbook, draft, draft.PageRequests[0])
            .Ops.OfType<PdfFilledTriangle>().Should().BeEmpty();
    }

    private static PortablePdfExportPlan PlanSelectedRange(Workbook workbook, Sheet sheet)
    {
        var export = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: GridRange.Parse("A1:E6", sheet.Id)),
            WorkbookExportPrintSurface.MacOs);
        return PortablePdfExportPlanner.CreatePlan(export, workbook);
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
