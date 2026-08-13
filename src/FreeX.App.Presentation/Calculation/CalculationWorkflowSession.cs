using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Calculation;

public sealed record CalculationCommandExecutionResult(
    bool Success,
    string? ErrorMessage = null,
    bool IsNoOp = false);

public sealed record CalculationWorkflowOutcome(
    bool Success,
    bool ExecutedCommand,
    bool IsNoOp,
    CalculationRecalculationScope RecalculationScope,
    CalculationStateRefreshPolicy RefreshPolicy,
    CalculationStatusPlan Status,
    string? FailureResourceKey = null,
    string? ErrorMessage = null);

public sealed record IterativeCalculationWorkflowOutcome(
    bool Success,
    bool ExecutedCommand,
    bool IsNoOp,
    CalculationRecalculationScope RecalculationScope,
    string? FailureResourceKey = null,
    string? ErrorMessage = null);

public sealed record FormulaErrorRuleWorkflowOutcome(
    bool Success,
    int PlannedCommandCount,
    int ExecutedCommandCount,
    string? ErrorMessage = null)
{
    public bool IsNoOp => PlannedCommandCount == 0;
}

public sealed record CalculationRecalculationOperations(
    Action RecalculateDirtyWorkbook,
    Action RecalculateFullWorkbook,
    Action RecalculateActiveSheet);

public delegate CalculationCommandExecutionResult CalculationCommandExecutor(
    IWorkbookCommand command,
    string commandLabel);

/// <summary>
/// Owns calculation command execution and recalculation sequencing shared by the desktop shells.
/// Renderers retain native command surfaces, resource rendering, errors, and repainting.
/// </summary>
public sealed class CalculationWorkflowSession
{
    private readonly Workbook _workbook;
    private readonly CalculationCommandExecutor _executeCommand;
    private readonly CalculationRecalculationOperations _recalculation;

    public CalculationWorkflowSession(
        Workbook workbook,
        CalculationCommandExecutor executeCommand,
        CalculationRecalculationOperations recalculation)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
        _recalculation = recalculation ?? throw new ArgumentNullException(nameof(recalculation));

        ArgumentNullException.ThrowIfNull(_recalculation.RecalculateDirtyWorkbook);
        ArgumentNullException.ThrowIfNull(_recalculation.RecalculateFullWorkbook);
        ArgumentNullException.ThrowIfNull(_recalculation.RecalculateActiveSheet);
    }

    public CalculationWorkflowOutcome ChangeMode(WorkbookCalculationMode requestedMode)
    {
        var plan = CalculationCommandPolicy.PlanModeChange(
            _workbook.CalculationMode,
            requestedMode);
        if (plan.IsNoOp)
        {
            return new CalculationWorkflowOutcome(
                Success: true,
                ExecutedCommand: false,
                IsNoOp: true,
                plan.RecalculationScope,
                plan.RefreshPolicy,
                plan.Status);
        }

        var execution = _executeCommand(plan.Command!, CalculationCommandPolicy.CommandLabel);
        if (!execution.Success)
        {
            return new CalculationWorkflowOutcome(
                Success: false,
                ExecutedCommand: true,
                execution.IsNoOp,
                CalculationRecalculationScope.None,
                CalculationStateRefreshPolicy.None,
                plan.Status,
                plan.FailureResourceKey,
                execution.ErrorMessage);
        }

        ApplyRecalculation(plan.RecalculationScope);
        return new CalculationWorkflowOutcome(
            Success: true,
            ExecutedCommand: true,
            execution.IsNoOp,
            plan.RecalculationScope,
            plan.RefreshPolicy,
            plan.Status);
    }

    public CalculationWorkflowOutcome Execute(CalculationCommandAction action)
    {
        var plan = CalculationCommandPolicy.PlanAction(action);
        ApplyRecalculation(plan.RecalculationScope);
        return new CalculationWorkflowOutcome(
            Success: true,
            ExecutedCommand: false,
            IsNoOp: false,
            plan.RecalculationScope,
            plan.RefreshPolicy,
            plan.Status);
    }

    public IterativeCalculationWorkflowOutcome ChangeIterativeCalculation(
        bool enabled,
        int? maxIterations,
        double? maxChange)
    {
        var plan = CalculationCommandPolicy.PlanIterativeCalculationChange(
            _workbook.IterativeCalculation,
            _workbook.MaxCalculationIterations,
            _workbook.MaxCalculationChange,
            enabled,
            maxIterations,
            maxChange);
        if (plan.IsNoOp)
        {
            return new IterativeCalculationWorkflowOutcome(
                Success: true,
                ExecutedCommand: false,
                IsNoOp: true,
                plan.RecalculationScope);
        }

        var execution = _executeCommand(plan.Command!, CalculationCommandPolicy.CommandLabel);
        if (!execution.Success)
        {
            return new IterativeCalculationWorkflowOutcome(
                Success: false,
                ExecutedCommand: true,
                execution.IsNoOp,
                CalculationRecalculationScope.None,
                plan.FailureResourceKey,
                execution.ErrorMessage);
        }

        ApplyRecalculation(plan.RecalculationScope);
        return new IterativeCalculationWorkflowOutcome(
            Success: true,
            ExecutedCommand: true,
            execution.IsNoOp,
            plan.RecalculationScope);
    }

    public FormulaErrorRuleWorkflowOutcome ChangeFormulaErrorRules(
        IEnumerable<string> requestedDisabledErrorCodes)
    {
        ArgumentNullException.ThrowIfNull(requestedDisabledErrorCodes);

        var commands = CalculationCommandPolicy.PlanFormulaErrorRuleChanges(
            _workbook.DisabledFormulaErrorCodes,
            requestedDisabledErrorCodes);
        var executedCommandCount = 0;
        foreach (var command in commands)
        {
            var execution = _executeCommand(
                command,
                CalculationCommandPolicy.FormulaErrorRulesCommandLabel);
            executedCommandCount++;
            if (!execution.Success)
            {
                return new FormulaErrorRuleWorkflowOutcome(
                    Success: false,
                    commands.Count,
                    executedCommandCount,
                    execution.ErrorMessage);
            }
        }

        return new FormulaErrorRuleWorkflowOutcome(
            Success: true,
            commands.Count,
            executedCommandCount);
    }

    private void ApplyRecalculation(CalculationRecalculationScope scope)
    {
        switch (scope)
        {
            case CalculationRecalculationScope.None:
                return;
            case CalculationRecalculationScope.DirtyWorkbook:
                _recalculation.RecalculateDirtyWorkbook();
                return;
            case CalculationRecalculationScope.FullWorkbook:
                _recalculation.RecalculateFullWorkbook();
                return;
            case CalculationRecalculationScope.ActiveSheet:
                _recalculation.RecalculateActiveSheet();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
        }
    }
}
