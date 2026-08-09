using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookExportInteractionPlannerTests
{
    [Fact]
    public void CreateCommandPlan_CombinesSelectionReadinessAndDestinationCapability()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        var selection = GridRange.Parse("A1:B2", sheet.Id);

        var available = WorkbookExportInteractionPlanner.CreateCommandPlan(
            workbook,
            selection,
            WorkbookExportPrintSurface.MacOs);
        var unavailable = WorkbookExportInteractionPlanner.CreateCommandPlan(
            workbook,
            selection,
            WorkbookExportPrintSurface.MacOs,
            canChooseDestination: false);

        available.CanExecute.Should().BeTrue();
        available.HasSelection.Should().BeTrue();
        available.ScopePlan.Scopes.Single(option => option.Scope == WorkbookExportPrintScope.SelectedRange)
            .IsAvailable.Should().BeTrue();
        unavailable.CanExecute.Should().BeFalse();
        unavailable.BlockingStatusKey.Should().Be("MainLoc_PdfExportUnavailable");
    }

    [Fact]
    public void CreateSelectionPlan_RejectsSelectedRangeWithoutSelection()
    {
        var plan = WorkbookExportInteractionPlanner.CreateSelectionPlan(
            WorkbookExportPrintScope.SelectedRange,
            selectedRange: null);

        plan.IsValid.Should().BeFalse();
        plan.ContentScope.Should().Be(ExportContentScope.Selection);
        plan.ErrorStatusKey.Should().Be("Backstage_Export_ScopeSelectionUnavailable");
    }

    [Fact]
    public void CreateRequestPlan_NormalizesDestinationAndOverwriteDecision()
    {
        var plan = WorkbookExportInteractionPlanner.CreateRequestPlan(
            @"C:\temp\report.txt",
            WorkbookExportPrintOutputKind.Pdf,
            ExportOptions.ExcelLikeDefault with { PdfLanguage = "en_us" },
            path => path == @"C:\temp\report.pdf");

        plan.Format.Should().BeSameAs(ExportFormatCatalog.Pdf);
        plan.Request.Path.Should().Be(@"C:\temp\report.pdf");
        plan.Request.Options.PdfLanguage.Should().Be("en-US");
        plan.ShouldConfirmNormalizedOverwrite.Should().BeTrue();
        plan.ShouldPersistPdfLanguage.Should().BeTrue();
        plan.DestinationFileName.Should().Be("report.pdf");
    }

    [Fact]
    public void CreateResultPlan_SequencesOpenAndBackstageCloseAfterSuccess()
    {
        var request = ExportPlanner.PlanExport(
            "report.pdf",
            ExportFormat.Pdf,
            ExportOptions.ExcelLikeDefault with { OpenAfterPublish = true });
        var result = new WorkbookExportExecutionResult(
            WorkbookExportExecutionOutcome.Succeeded,
            request,
            Message: "");

        var plan = WorkbookExportInteractionPlanner.CreateResultPlan(
            result,
            isBackstageVisible: true);

        plan.Succeeded.Should().BeTrue();
        plan.ShouldPresentIssue.Should().BeFalse();
        plan.ShouldOpenDestination.Should().BeTrue();
        plan.ShouldCloseBackstage.Should().BeTrue();
        plan.DestinationPath.Should().EndWith("report.pdf");
    }

    [Fact]
    public void CreateResultPlan_PreservesAdapterFailureMessage()
    {
        var request = ExportPlanner.PlanExport("report.pdf");
        var result = new WorkbookExportExecutionResult(
            WorkbookExportExecutionOutcome.Failed,
            request,
            "Export did not complete.");

        var plan = WorkbookExportInteractionPlanner.CreateResultPlan(
            result,
            isBackstageVisible: false,
            adapterFailureMessage: "No exportable pages.");

        plan.IssueKind.Should().Be(WorkbookExportResultIssueKind.Failure);
        plan.ShouldPresentIssue.Should().BeTrue();
        plan.Message.Should().Be("No exportable pages.");
    }
}
