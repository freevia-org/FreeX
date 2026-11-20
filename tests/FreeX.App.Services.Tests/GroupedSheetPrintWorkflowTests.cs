using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class GroupedSheetPrintWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatePlan_IncludesOnlyVisibleGroupMembersInWorkbookOrder(bool hasPrinter)
    {
        var workbook = new Workbook("Grouped worksheets");
        var first = workbook.AddSheet("First");
        var excluded = workbook.AddSheet("Excluded");
        var blank = workbook.AddSheet("Blank");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var fullyHiddenArea = workbook.AddSheet("Fully hidden area");
        fullyHiddenArea.PrintArea = GridRange.Parse("A1:A1", fullyHiddenArea.Id);
        fullyHiddenArea.HiddenRows.Add(1);
        var last = workbook.AddSheet("Last");
        var veryHidden = workbook.AddSheet("Very hidden");
        veryHidden.IsVeryHidden = true;
        foreach (var sheet in new[] { first, excluded, hidden, fullyHiddenArea, last, veryHidden })
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(sheet.Name));

        var request = new PrintJobRequest(WorkbookExportPrintScope.GroupedSheets)
        {
            GroupedSheetIds = new HashSet<SheetId> { last.Id, hidden.Id, fullyHiddenArea.Id, blank.Id, first.Id, veryHidden.Id }
        };
        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook,
            hasSelection: false,
            request,
            PrintExportHostCapabilities.AvaloniaPortable(
                canSubmitToPlatformPrinter: hasPrinter,
                hasPrinterDestination: hasPrinter));

        plan.IsReady.Should().BeTrue();
        var job = plan.Readiness.NativePrintPlan.JobPlan;
        job.FirstPage.Should().Be(1);
        job.LastPage.Should().Be(3);
        plan.PortablePdfPlan!.PageRequests.Select(page => page.SheetName).Should().Equal("First", "Blank", "Last");

        // Use the group's own last page for range validation even when its worksheet was selected last.
        var lastPagePlan = WorkbookPrintWorkflow.CreatePlan(
            workbook, false,
            request with { PageRangeKind = PrintJobPageRangeKind.PageRange, FromPage = 3, ToPage = 3 },
            PrintExportHostCapabilities.AvaloniaPortable());
        lastPagePlan.IsReady.Should().BeTrue();
        lastPagePlan.PortablePdfPlan!.PageRequests.Should().ContainSingle().Which.SheetName.Should().Be("Last");
    }

    [Fact]
    public void CreatePlan_MissingGroupDoesNotPrintAnotherScope()
    {
        var workbook = new Workbook("Grouped worksheets");
        var sheet = workbook.AddSheet("Worksheet");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Data"));

        var plan = WorkbookPrintWorkflow.CreatePlan(
            workbook, false, new PrintJobRequest(WorkbookExportPrintScope.GroupedSheets),
            PrintExportHostCapabilities.AvaloniaPortable());

        plan.IsReady.Should().BeFalse();
        plan.Readiness.PdfExportPlan.ValidationStatus.Should().Be(WorkbookExportPrintValidationStatus.NoPrintableRanges);
        plan.PortablePdfPlan.Should().BeNull();
    }
}
