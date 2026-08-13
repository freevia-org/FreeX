using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookExportCommandPlan(
    bool CanExecute,
    bool HasSelection,
    WorkbookExportScopePlan ScopePlan,
    WorkbookExportReadinessPlan Readiness,
    string? BlockingStatusKey);

public sealed record WorkbookExportSelectionPlan(
    WorkbookExportPrintScope PrintScope,
    ExportContentScope ContentScope,
    GridRange? SelectedRange,
    bool IsValid,
    string? ErrorStatusKey);

public sealed record WorkbookExportRequestPlan(
    ExportFormatDefinition Format,
    ExportRequest Request,
    bool ShouldConfirmNormalizedOverwrite,
    bool ShouldPersistPdfLanguage)
{
    public string DestinationPath => Request.ActualPath;

    public string DestinationFileName => Path.GetFileName(DestinationPath) ?? DestinationPath;
}

public enum WorkbookExportResultIssueKind
{
    None,
    Validation,
    Failure,
    Canceled
}

public sealed record WorkbookExportResultPlan(
    bool Succeeded,
    WorkbookExportResultIssueKind IssueKind,
    string Message,
    bool ShouldPresentIssue,
    bool ShouldOpenDestination,
    bool ShouldCloseBackstage,
    string DestinationPath);

/// <summary>
/// UI-free interaction policy around export engines: command state, scope validation, request and
/// destination normalization, overwrite decisions, and post-execution sequencing.
/// </summary>
public static class WorkbookExportInteractionPlanner
{
    public static WorkbookExportCommandPlan CreateCommandPlan(
        Workbook workbook,
        GridRange? selectedRange,
        WorkbookExportPrintSurface surface,
        bool isBusy = false,
        bool canChooseDestination = true)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(surface);

        var hasSelection = selectedRange is not null;
        var scopePlan = WorkbookExportScopePlanner.Build(workbook, hasSelection, surface);
        var readiness = WorkbookExportReadinessPlanner.Create(workbook, hasSelection);
        var blockingStatusKey = !canChooseDestination
            ? "MainLoc_PdfExportUnavailable"
            : null;

        return new WorkbookExportCommandPlan(
            !isBusy && canChooseDestination && scopePlan.CanExport,
            hasSelection,
            scopePlan,
            readiness,
            blockingStatusKey);
    }

    public static WorkbookExportSelectionPlan CreateSelectionPlan(
        WorkbookExportPrintScope scope,
        GridRange? selectedRange)
    {
        var contentScope = ExportFormatCatalog.ToContentScope(scope);
        var selectionRequired = scope == WorkbookExportPrintScope.SelectedRange;
        var isValid = !selectionRequired || selectedRange is not null;

        return new WorkbookExportSelectionPlan(
            scope,
            contentScope,
            selectionRequired ? selectedRange : null,
            isValid,
            isValid ? null : "Backstage_Export_ScopeSelectionUnavailable");
    }

    public static WorkbookExportRequestPlan CreateRequestPlan(
        string requestedPath,
        WorkbookExportPrintOutputKind outputKind,
        ExportOptions options,
        Func<string, bool> pathExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pathExists);

        var format = ExportFormatCatalog.Get(outputKind);
        var effectiveOptions = ExportPlanner.CreateEffectiveOptionsForFormat(options, format.Format);
        var request = ExportPlanner.PlanExport(requestedPath, format.Format, effectiveOptions);
        return new WorkbookExportRequestPlan(
            format,
            request,
            ExportPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, request, pathExists),
            ShouldPersistPdfLanguage: format.Format == ExportFormat.Pdf);
    }

    public static WorkbookExportRequestPlan CreateRequestPlan(
        string requestedPath,
        ExportFormat format,
        ExportOptions options,
        Func<string, bool> pathExists) =>
        CreateRequestPlan(
            requestedPath,
            ExportFormatCatalog.Get(format).OutputKind,
            options,
            pathExists);

    public static WorkbookExportResultPlan CreateResultPlan(
        WorkbookExportExecutionResult result,
        bool isBackstageVisible,
        bool adapterOwnsFailurePresentation = false,
        string? adapterFailureMessage = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var succeeded = result.Succeeded;
        var issueKind = result.Outcome switch
        {
            WorkbookExportExecutionOutcome.ValidationFailed => WorkbookExportResultIssueKind.Validation,
            WorkbookExportExecutionOutcome.Failed => WorkbookExportResultIssueKind.Failure,
            WorkbookExportExecutionOutcome.Canceled => WorkbookExportResultIssueKind.Canceled,
            _ => WorkbookExportResultIssueKind.None
        };
        var message = string.IsNullOrWhiteSpace(adapterFailureMessage)
            ? result.Message
            : adapterFailureMessage;
        var shouldPresentIssue = issueKind != WorkbookExportResultIssueKind.None &&
            !(adapterOwnsFailurePresentation && issueKind == WorkbookExportResultIssueKind.Failure);

        return new WorkbookExportResultPlan(
            succeeded,
            issueKind,
            message,
            shouldPresentIssue,
            succeeded && result.Request.Options.OpenAfterPublish,
            succeeded && isBackstageVisible,
            result.Request.ActualPath);
    }
}
