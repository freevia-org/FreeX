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
/// Forcing a recalculation (the real behaviour of "Calculate Now") needs
/// <c>RecalcEngine.RecalculateAllFormulas</c>, which lives in <c>FreeX.Core.Calc</c> and is
/// only reachable by the Windows host because it holds the <c>RecalcEngine</c> directly.
/// <c>WorkbookSession</c> does NOT currently expose the engine or a force-recalc method
/// (its <c>ExecuteReviewCommand</c> / <c>ExecuteEditCommand</c> path does not recalc, and
/// <c>WorkbookCellEditService.RecalculateIfAutomatic</c> is private to that service). Until a
/// recalc method is added to <c>WorkbookSession</c>, "Calculate Now" reports that a recalc
/// is unavailable in this build rather than silently doing nothing. See the handler note for
/// the exact method that needs adding.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    // Mirrors the workbook calc mode so we can flip Automatic <-> Manual on the
    // "Calculation Options" parent button (the WPF host shows a menu; the Avalonia
    // ExtraCommands dictionary hands us a parameterless Action with no anchor control,
    // so we toggle and report the resulting mode like the other dropdown-parent buttons).
    private bool CalculationModeIsManual =>
        _session.Workbook.CalculationMode == WorkbookCalculationMode.Manual;

    /// <summary>
    /// Handler for <c>formulas.calcOptions</c> ("Calculation Options"). Toggles the workbook
    /// calculation mode between Automatic and Manual via the undoable
    /// <see cref="SetCalculationModeCommand"/>, then reports the resulting mode.
    /// </summary>
    private void ToggleCalculationMode()
    {
        var nextMode = CalculationModeIsManual
            ? WorkbookCalculationMode.Automatic
            : WorkbookCalculationMode.Manual;

        SetCalculationMode(nextMode);
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

    private void SetCalculationMode(WorkbookCalculationMode mode)
    {
        if (_session.Workbook.CalculationMode == mode)
        {
            RefreshShell($"Calculation already set to {DescribeCalculationMode(mode)}.");
            return;
        }

        var result = _session.ExecuteReviewCommand(new SetCalculationModeCommand(mode));
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? "Could not change calculation mode.");
            return;
        }

        // Switching to Automatic recalculates any values left stale while in Manual mode
        // (mirrors the Windows host, which recalcs on the Automatic transition).
        if (mode == WorkbookCalculationMode.Automatic)
            _session.RecalculateWorkbook();

        RefreshShell($"Calculation set to {DescribeCalculationMode(mode)}.");
    }

    /// <summary>
    /// Handler for <c>formulas.calcNow</c> ("Calculate Now", F9). Recalculates every formula
    /// in the workbook.
    /// </summary>
    /// <remarks>
    /// Forces a full recalc via <c>WorkbookSession.RecalculateWorkbook()</c> (which drives
    /// <c>RecalcEngine.RecalculateAllFormulas</c>), then refreshes the shell.
    /// </remarks>
    private void CalculateNow()
    {
        _session.RecalculateWorkbook();
        RefreshShell("Recalculated all formulas.");
    }

    private static string DescribeCalculationMode(WorkbookCalculationMode mode) =>
        mode == WorkbookCalculationMode.Manual ? "Manual" : "Automatic";
}
