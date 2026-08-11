using Free.Shared.Localization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Consolidate;

public static class ConsolidateApplicationWorkflow
{
    public static ConsolidateApplicationPlan Plan(
        Workbook workbook,
        ConsolidateDialogResult request,
        bool overwriteConfirmed)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(request);

        var options = new ConsolidateOptions
        {
            Function = request.Function,
            UseTopRowLabels = request.UseTopRowLabels,
            UseLeftColumnLabels = request.UseLeftColumnLabels,
        };

        if (!ConsolidateDialogPlanner.TryPlanApply(
                workbook,
                request.SourceRanges,
                request.DestinationCell,
                options,
                out var applyPlan,
                out var issue))
        {
            return new ConsolidateApplicationPlan(
                ConsolidateApplicationDisposition.Invalid,
                request,
                ApplyPlan: null,
                issue);
        }

        var disposition = applyPlan.OverwriteTargets.Count > 0 && !overwriteConfirmed
            ? ConsolidateApplicationDisposition.ConfirmOverwrite
            : ConsolidateApplicationDisposition.Ready;
        return new ConsolidateApplicationPlan(disposition, request, applyPlan, ConsolidateDialogIssue.None);
    }

    public static ConsolidateApplicationPlan Plan(
        Workbook workbook,
        IReadOnlyList<string> sourceReferences,
        string destinationCellText,
        ConsolidateReferenceParser parseReference,
        ConsolidateOptions options,
        bool createLinksToSourceData,
        bool overwriteConfirmed)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sourceReferences);
        ArgumentNullException.ThrowIfNull(parseReference);
        ArgumentNullException.ThrowIfNull(options);

        if (!ConsolidateDialogPlanner.TryPlanApply(
                workbook,
                sourceReferences,
                destinationCellText,
                parseReference,
                options,
                out var applyPlan,
                out var issue))
        {
            return new ConsolidateApplicationPlan(
                ConsolidateApplicationDisposition.Invalid,
                EmptyRequest(options, createLinksToSourceData),
                ApplyPlan: null,
                issue);
        }

        var request = new ConsolidateDialogResult(
            applyPlan.SourceRanges,
            applyPlan.DestinationCell,
            options.Function,
            options.UseTopRowLabels,
            options.UseLeftColumnLabels,
            createLinksToSourceData);
        var disposition = applyPlan.OverwriteTargets.Count > 0 && !overwriteConfirmed
            ? ConsolidateApplicationDisposition.ConfirmOverwrite
            : ConsolidateApplicationDisposition.Ready;
        return new ConsolidateApplicationPlan(disposition, request, applyPlan, ConsolidateDialogIssue.None);
    }

    public static ConsolidateExecutionOutcome Execute(
        ConsolidateApplicationPlan plan,
        Func<Func<IWorkbookCommand>, ConsolidateCommandAdapterResult> execute)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(execute);

        if (!plan.CanExecute)
        {
            return new ConsolidateExecutionOutcome(
                ConsolidateExecutionStatus.NotReady,
                plan.Request.DestinationCell,
                ErrorMessage: null);
        }

        ConsolidateCommandAdapterResult result;
        try
        {
            result = execute(() => CreateCommand(plan.Request));
        }
        catch (Exception ex)
        {
            result = new ConsolidateCommandAdapterResult(false, ex.Message);
        }

        return new ConsolidateExecutionOutcome(
            result.Success ? ConsolidateExecutionStatus.Applied : ConsolidateExecutionStatus.Failed,
            plan.Request.DestinationCell,
            result.ErrorMessage);
    }

    public static LocalizedTextDescriptor DescribeOverwriteConfirmation(ConsolidateApplicationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return LocalizedTextDescriptor.Resource(
            "TableLoc_ConsolidateOverwriteWarning",
            plan.ApplyPlan?.OverwriteTargets.Count ?? 0);
    }

    public static LocalizedTextDescriptor DescribeFailure(ConsolidateExecutionOutcome outcome) =>
        string.IsNullOrWhiteSpace(outcome.ErrorMessage)
            ? LocalizedTextDescriptor.Resource("TableLoc_ConsolidateFailed")
            : LocalizedTextDescriptor.Literal(outcome.ErrorMessage);

    public static LocalizedTextDescriptor DescribeSuccess(ConsolidateExecutionOutcome outcome) =>
        LocalizedTextDescriptor.Resource("TableLoc_ConsolidatedInto", outcome.DestinationCell.ToA1());

    private static ConsolidateDialogResult EmptyRequest(
        ConsolidateOptions options,
        bool createLinksToSourceData) =>
        new(
            SourceRanges: [],
            DestinationCell: default,
            Function: options.Function,
            UseTopRowLabels: options.UseTopRowLabels,
            UseLeftColumnLabels: options.UseLeftColumnLabels,
            CreateLinksToSourceData: createLinksToSourceData);

    private static ConsolidateCommand CreateCommand(ConsolidateDialogResult request) =>
        new(
            request.SourceRanges,
            request.DestinationCell,
            request.Function,
            request.UseTopRowLabels,
            request.UseLeftColumnLabels,
            request.CreateLinksToSourceData);
}
