using FreeX.App.Presentation.Calculation;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Formulas tab ▸ Calculation group command handlers (Windows-parity).
///
/// <para>
/// Two ribbon buttons live in the Calculation group (see <c>AvaloniaRibbonHost</c> /
/// <c>MainWindow.cs</c> ribbon definition): <c>formulas.calcOptions</c> ("Calculation
/// Options") and <c>formulas.calcNow</c> ("Calculate Now" / F9).
/// </para>
///
/// <para>
/// Calculation mode (Automatic / Manual) is a workbook-level concept exposed by
/// <see cref="Workbook.CalculationMode"/> and mutated through the undoable Core command
/// <see cref="SetCalculationModeCommand"/>. The Avalonia shell already routes undoable
/// commands through <c>WorkbookSession.ExecuteReviewCommand(IWorkbookCommand)</c>, so the
/// calc-mode toggle needs no <c>WorkbookSession</c> change.
/// </para>
///
/// <para>
/// Forced calculation delegates to <see cref="CalculationCommandPolicy"/> for dirty-workbook,
/// full-workbook, and active-sheet scope. The shell maps that plan onto <c>WorkbookSession</c>
/// recalculation methods and retains native keyboard/ribbon routing.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    // Mirrors the workbook calc mode so we can flip Automatic <-> Manual on the
    // "Calculation Options" parent button (the WPF host shows a menu; the Avalonia
    // ExtraCommands dictionary hands us a parameterless Action with no anchor control,
    // so we toggle and report the resulting mode like the other dropdown-parent buttons).
    private bool CalculationModeIsManual =>
        CalculationCommandPolicy.IsManual(_session.Workbook.CalculationMode);

    /// <summary>
    /// Handler for <c>formulas.calcOptions</c> ("Calculation Options"). Toggles the workbook
    /// calculation mode between Automatic and Manual via the undoable
    /// <see cref="SetCalculationModeCommand"/>, then reports the resulting mode.
    /// </summary>
    private void ToggleCalculationMode()
    {
        SetCalculationMode(CalculationCommandPolicy.ToggleTarget(_session.Workbook.CalculationMode));
    }

    /// <summary>
    /// Handler for the Automatic menu choice. Sets the workbook to Automatic calculation.
    /// </summary>
    private void SetCalculationModeAutomatic() =>
        SetCalculationMode(WorkbookCalculationMode.Automatic);

    /// <summary>
    /// Handler for the Manual menu choice. Sets the workbook to Manual calculation.
    /// </summary>
    private void SetCalculationModeManual() =>
        SetCalculationMode(WorkbookCalculationMode.Manual);

    /// <summary>
    /// Handler for the "Automatic Except Data Tables" menu choice. Sets the workbook to
    /// <see cref="WorkbookCalculationMode.AutomaticExceptDataTables"/> (mirrors the Windows
    /// host's <c>CalcAutoExceptDataTablesMenuItem_Click</c>, which is distinct from the plain
    /// Automatic handler).
    /// </summary>
    private void SetCalculationModeAutomaticExceptDataTables() =>
        SetCalculationMode(WorkbookCalculationMode.AutomaticExceptDataTables);

    private void SetCalculationMode(WorkbookCalculationMode mode)
    {
        ApplyCalculationWorkflowOutcome(CalculationWorkflow.ChangeMode(mode));
    }

    /// <summary>
    /// Handler for <c>formulas.calcNow</c> ("Calculate Now", F9). Recalculates dirty and volatile
    /// formulas through the existing dependency graph.
    /// </summary>
    /// <remarks>
    /// Uses <c>WorkbookSession.RecalculateDirtyCells()</c>, then refreshes the shell.
    /// </remarks>
    private void CalculateNow()
    {
        ExecuteCalculationAction(CalculationCommandAction.CalculateNow);
    }

    private void CalculateFull()
    {
        ExecuteCalculationAction(CalculationCommandAction.CalculateFull);
    }

    /// <summary>
    /// Handler for Shift+F9 ("Calculate Sheet"). Recalculates only the active worksheet's
    /// formulas, mirroring the WPF host's <c>CalcSheetBtn_Click</c>.
    /// </summary>
    private void CalculateActiveSheet()
    {
        ExecuteCalculationAction(CalculationCommandAction.CalculateActiveSheet);
    }

    private CalculationWorkflowSession CalculationWorkflow =>
        new(
            _session.Workbook,
            (command, _) =>
            {
                var result = _session.ExecuteReviewCommand(command);
                return new CalculationCommandExecutionResult(
                    result.Success,
                    result.ErrorMessage,
                    result.IsNoOp);
            },
            new CalculationRecalculationOperations(
                _session.RecalculateDirtyCells,
                _session.RecalculateWorkbook,
                _session.RecalculateActiveSheet));

    private void ExecuteCalculationAction(CalculationCommandAction action)
    {
        ApplyCalculationWorkflowOutcome(CalculationWorkflow.Execute(action));
    }

    private void ApplyCalculationWorkflowOutcome(CalculationWorkflowOutcome outcome)
    {
        if (!outcome.Success)
        {
            ApplyCalculationRefresh(
                CalculationStateRefreshPolicy.CommandSurface,
                outcome.ErrorMessage ?? UiText.Get(
                    outcome.FailureResourceKey ?? CalculationCommandPolicy.FailureResourceKey));
            return;
        }

        ApplyCalculationRefresh(
            outcome.RefreshPolicy,
            ResolveCalculationStatus(outcome.Status));
    }

    private void ApplyCalculationRefresh(CalculationStateRefreshPolicy policy, string status)
    {
        if (policy != CalculationStateRefreshPolicy.None)
            RefreshShell(status);
    }

    private static string ResolveCalculationStatus(CalculationStatusPlan status)
    {
        if (status.ArgumentResourceKey is not { } argumentKey)
            return UiText.Get(status.ResourceKey);

        var argument = UiText.Get(argumentKey).Replace("_", string.Empty, StringComparison.Ordinal);
        return UiText.Format(status.ResourceKey, argument);
    }
}
