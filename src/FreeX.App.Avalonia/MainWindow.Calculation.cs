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
/// Forcing a recalculation ("Calculate Now" / F9, and "Calculate Sheet" / Shift+F9) is exposed
/// by <c>WorkbookSession.RecalculateWorkbook()</c> and <c>WorkbookSession.RecalculateActiveSheet()</c>,
/// which drive <c>RecalcEngine.RecalculateAllFormulas</c> / <c>RecalcEngine.RecalculateSheetFormulas</c>
/// (via <c>WorkbookCellEditService</c>) the same way the WPF host's <c>CalcNowBtn_Click</c> /
/// <c>CalcSheetBtn_Click</c> do. Both keyboard shortcuts are wired in <c>MainWindow.cs</c>'s
/// <c>MainWindow_KeyDownAsync</c>.
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
        if (_session.Workbook.CalculationMode == mode)
        {
            RefreshShell(UiText.Format("ShellLoc_CalculationAlreadySet", DescribeCalculationMode(mode)));
            return;
        }

        var result = _session.ExecuteReviewCommand(new SetCalculationModeCommand(mode));
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotChangeCalcMode"));
            return;
        }

        // Switching to either Automatic variant recalculates any values left stale while in
        // Manual mode (mirrors the Windows host, which recalcs on both the Automatic and
        // Automatic-Except-Data-Tables transitions).
        if (mode is WorkbookCalculationMode.Automatic or WorkbookCalculationMode.AutomaticExceptDataTables)
            _session.RecalculateWorkbook();

        RefreshShell(UiText.Format("ShellLoc_CalculationSet", DescribeCalculationMode(mode)));
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
        RefreshShell(UiText.Get("ShellLoc_RecalculatedAllFormulas"));
    }

    /// <summary>
    /// Handler for Shift+F9 ("Calculate Sheet"). Recalculates only the active worksheet's
    /// formulas, mirroring the WPF host's <c>CalcSheetBtn_Click</c>.
    /// </summary>
    private void CalculateActiveSheet()
    {
        _session.RecalculateActiveSheet();
        RefreshShell(UiText.Get("ShellLoc_RecalculatedAllFormulas"));
    }

    private static string DescribeCalculationMode(WorkbookCalculationMode mode) =>
        mode == WorkbookCalculationMode.Manual
            ? UiText.Get("ShellLoc_CalcModeManual")
            : UiText.Get("ShellLoc_CalcModeAutomatic");
}
