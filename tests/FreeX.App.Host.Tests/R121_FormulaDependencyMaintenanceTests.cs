using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R121-app-host-formula-dependency-maintenance:
/// <c>FreeX.App.Services.WorkbookCellEditService</c> maintains the dependency graph for EVERY
/// affected cell (<c>UpdateFormulaDependencies</c>, run unconditionally before the recalc branch in
/// <c>ApplyHistoryOutcome</c>) on every edit, regardless of calculation mode. The WPF host used to
/// hand-roll an equivalent of this in exactly ONE place -- <c>CommitPreparedEdits</c>, the
/// formula-bar/cell-editor commit path -- re-parsing the raw, un-normalized formula-bar text instead
/// of reading each cell's own already-committed <see cref="Cell.FormulaText"/>. Every OTHER edit path
/// (Paste, Fill, Sort, Undo/Redo, Find &amp; Replace, Goal Seek, Delete key/Clear Contents, ...) had NO
/// dependency-graph maintenance of its own at all. This is now centralized in
/// <c>RecalculateIfAutomatic</c> (the single choke point ~40 call sites across the shell already
/// funnel through), mirroring the shared service's <c>UpdateFormulaDependencies</c> call exactly,
/// and read from each cell's own committed FormulaText.
///
/// NOTE ON TEST COVERAGE: in Automatic/AutomaticExceptDataTables mode, <c>RecalcEngine.Recalculate</c>
/// already performs equivalent register/clear bookkeeping internally for whatever changedCells list
/// it is given (<c>EnsureChangedFormulaDependenciesRegistered</c>/<c>ClearVacatedFormulaDependencies</c>),
/// so this fix is mostly a no-op re-derivation there. The one case that reaches this method with NO
/// internal RecalcEngine bookkeeping at all is Manual mode clearing a formula cell via a
/// non-formula-bar path (its address is excluded from <c>RecalculateFreshlyEnteredFormulasOnce</c>'s
/// filtered list, so <c>Recalculate</c> is never invoked for it) -- but that leaves only a dangling
/// dependency-GRAPH edge with no reproducible value-level symptom under this engine's current
/// "skip non-formula cells during evaluation" behavior, so it could not be proven with a failing
/// CellValue-based test the way the Goal Seek fix could. It is included as a defensive,
/// line-for-line parity fix with the shared service (harmless, and closes a documented
/// service-vs-host code-path gap), not as an independently-proven bug fix. See the R121 report for
/// the honest accounting.
/// </summary>
public sealed class R121_FormulaDependencyMaintenanceTests
{
    /// <summary>
    /// No-regression check: an ordinary formula-bar commit of a formula that depends on another
    /// cell still recalculates correctly through <c>RecalculateIfAutomatic</c> now that it also runs
    /// the new unconditional dependency-maintenance step ahead of the mode switch.
    /// </summary>
    [Fact]
    public void AutomaticMode_EditingAPrecedentAfterFormulaEntry_StillRecalculatesTheDependent()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SetCellNumber(1, 1, 5); // A1 = 5
            harness.SelectActiveCell(2, 1);
            harness.SetFormulaEditCell(2, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=A1*2");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();
            harness.CellValue(2, 1).Should().Be(new NumberValue(10));

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("100");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellValue(2, 1).Should().Be(new NumberValue(200));
        });
    }

    /// <summary>
    /// No-regression check: clearing a formula cell via the Delete-key/Clear-Contents path (not the
    /// formula bar) in Manual mode still leaves it blank, and a later edit to its former precedent
    /// (after switching back to Automatic) does not resurrect a stale value or throw.
    /// </summary>
    [Fact]
    public void ManualMode_ClearingAFormulaCellViaDeleteKey_StaysBlankAfterLaterPrecedentEdit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SetCellNumber(1, 1, 5); // A1 = 5
            harness.SelectActiveCell(2, 1);
            harness.SetFormulaEditCell(2, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=A1+5");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();
            harness.CellValue(2, 1).Should().Be(new NumberValue(10));

            harness.ActiveWorkbook.CalculationMode = WorkbookCalculationMode.Manual;

            harness.SelectActiveCell(2, 1);
            harness.ClearSelectedContents();
            harness.CellFormula(2, 1).Should().BeNull();
            harness.CellValue(2, 1).Should().Be(BlankValue.Instance);

            harness.ActiveWorkbook.CalculationMode = WorkbookCalculationMode.Automatic;
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("100");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellValue(1, 1).Should().Be(new NumberValue(100));
            harness.CellValue(2, 1).Should().Be(BlankValue.Instance);
        });
    }
}
