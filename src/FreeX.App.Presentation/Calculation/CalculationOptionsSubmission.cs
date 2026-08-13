using FreeX.Core.Model;

namespace FreeX.App.Presentation.Calculation;

/// <summary>
/// Calculation state captured when an Options dialog opens. The two-state dialog projection keeps
/// the exact workbook mode so AutomaticExceptDataTables is not collapsed when the user leaves the
/// Automatic/Manual choice unchanged.
/// </summary>
public sealed record CalculationOptionsDialogState(
    bool AutoCalculate,
    bool IterativeCalculation,
    int? MaxCalculationIterations,
    double? MaxCalculationChange,
    WorkbookCalculationMode CalculationMode)
{
    public CalculationOptionsDialogState(
        bool AutoCalculate,
        bool IterativeCalculation,
        int? MaxCalculationIterations,
        double? MaxCalculationChange)
        : this(
            AutoCalculate,
            IterativeCalculation,
            MaxCalculationIterations,
            MaxCalculationChange,
            AutoCalculate ? WorkbookCalculationMode.Automatic : WorkbookCalculationMode.Manual)
    {
    }

    public static CalculationOptionsDialogState FromWorkbook(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return new CalculationOptionsDialogState(
            workbook.CalculationMode != WorkbookCalculationMode.Manual,
            workbook.IterativeCalculation,
            workbook.MaxCalculationIterations,
            workbook.MaxCalculationChange,
            workbook.CalculationMode);
    }

    public static CalculationOptionsDialogState FromAppDefault(bool autoCalculate) => new(
        autoCalculate,
        IterativeCalculation: false,
        MaxCalculationIterations: null,
        MaxCalculationChange: null,
        autoCalculate ? WorkbookCalculationMode.Automatic : WorkbookCalculationMode.Manual);
}

public sealed record IterativeCalculationSubmission(
    bool Enabled,
    int MaxIterations,
    double MaxChange);

public sealed record CalculationOptionsSubmission(
    WorkbookCalculationMode? RequestedMode,
    IterativeCalculationSubmission? IterativeCalculation);

public static class CalculationOptionsSubmissionPlanner
{
    public static CalculationOptionsSubmission? Plan(
        CalculationOptionsDialogState initial,
        bool autoCalculate,
        bool iterativeCalculation,
        int maxCalculationIterations,
        double maxCalculationChange)
    {
        ArgumentNullException.ThrowIfNull(initial);

        WorkbookCalculationMode? requestedMode = initial.AutoCalculate == autoCalculate
            ? null
            : autoCalculate
                ? WorkbookCalculationMode.Automatic
                : WorkbookCalculationMode.Manual;

        var iterativeChanged =
            initial.IterativeCalculation != iterativeCalculation ||
            (initial.MaxCalculationIterations ?? CalculationCommandPolicy.DefaultMaxCalculationIterations) !=
            maxCalculationIterations ||
            (initial.MaxCalculationChange ?? CalculationCommandPolicy.DefaultMaxCalculationChange) !=
            maxCalculationChange;

        var iterativeSubmission = iterativeChanged
            ? new IterativeCalculationSubmission(
                iterativeCalculation,
                maxCalculationIterations,
                maxCalculationChange)
            : null;

        return requestedMode is null && iterativeSubmission is null
            ? null
            : new CalculationOptionsSubmission(requestedMode, iterativeSubmission);
    }
}

public sealed record CalculationOptionsSubmissionOutcome(
    CalculationWorkflowOutcome? ModeOutcome,
    IterativeCalculationWorkflowOutcome? IterativeOutcome)
{
    public bool Success =>
        ModeOutcome?.Success != false &&
        IterativeOutcome?.Success != false;
}

/// <summary>
/// Executes an Options-dialog calculation submission through the shared calculation workflow.
/// Renderers retain responsibility for presenting status/errors and refreshing native controls.
/// </summary>
public static class CalculationOptionsSubmissionCoordinator
{
    public static CalculationOptionsSubmissionOutcome Apply(
        CalculationWorkflowSession workflow,
        CalculationOptionsSubmission? submission)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (submission is null)
            return new CalculationOptionsSubmissionOutcome(null, null);

        CalculationWorkflowOutcome? modeOutcome = null;
        if (submission.RequestedMode is { } requestedMode)
        {
            modeOutcome = workflow.ChangeMode(requestedMode);
            if (!modeOutcome.Success)
                return new CalculationOptionsSubmissionOutcome(modeOutcome, null);
        }

        IterativeCalculationWorkflowOutcome? iterativeOutcome = null;
        if (submission.IterativeCalculation is { } iterative)
        {
            iterativeOutcome = workflow.ChangeIterativeCalculation(
                iterative.Enabled,
                iterative.MaxIterations,
                iterative.MaxChange);
        }

        return new CalculationOptionsSubmissionOutcome(modeOutcome, iterativeOutcome);
    }
}
