using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R127-app-host-manual-f9-catchup: plain F9 ("Calculate Now" / <c>CalcNowBtn_Click</c>) used to
/// be a complete no-op in <see cref="WorkbookCalculationMode.Manual"/> for the single most common
/// reason a user presses it: "I just changed a precedent, bring the sheet up to date."
///
/// <para>
/// R120 correctly taught <c>RecalculateIfAutomatic</c>'s Manual branch to leave a precedent-only
/// edit's dependent formula stale (matching Excel -- see
/// <see cref="R120_ManualModeFreshFormulaRecalcTests"/>). But that means <c>_recalcEngine</c> is
/// never told anything changed: no changed cell, no dirty volatile cell, no spill-blocked anchor.
/// <c>RecalculateDirtyCells</c> (bound to plain F9) then called
/// <c>RecalcEngine.Recalculate(_workbook, [])</c>, which hit <c>RecalcEngine</c>'s
/// empty-changedCells early-exit guard and returned immediately -- so pressing F9 afterward did
/// nothing, and only Ctrl+Alt+F9 ("Calculate Full") actually refreshed the stale formula. Real
/// Excel's F9 is Manual mode's ONE explicit recalculation trigger and always catches up every
/// dirty formula regardless of how its precedent changed.
/// </para>
///
/// <para>
/// Fix: <c>RecalculateDirtyCells</c> now falls back to a full
/// <c>RecalcEngine.RecalculateAllFormulas</c> pass whenever <c>WorkbookCalculationMode.Manual</c>
/// is active, matching <c>RecalculateWorkbook</c> (Ctrl+Alt+F9) and the Avalonia shell's
/// <c>MainWindow.CalculateNow</c> (<c>MainWindow.Calculation.cs</c>), which already always does a
/// full recalc and was never affected by this bug -- see the no-fix-needed note in this round's
/// report. Automatic and AutomaticExceptDataTables are untouched: <c>RecalculateIfAutomatic</c>
/// always calls <c>Recalculate</c> with the FULL changed-cells list for both of those modes, so
/// nothing is ever silently dropped from the graph for F9 to need to catch up, and the cheap
/// dirty-only path remains correct and is still exercised (see the sibling test below).
/// </para>
/// </summary>
public sealed class R127_ManualModeF9CatchUpTests
{
    private static readonly MethodInfo CalcNowClickMethod =
        typeof(MainWindow).GetMethod("CalcNowBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "CalcNowBtn_Click");

    private static readonly MethodInfo CalcFullClickMethod =
        typeof(MainWindow).GetMethod("CalcFullBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "CalcFullBtn_Click");

    private static void ClickCalcNow(MainWindow window) =>
        CalcNowClickMethod.Invoke(window, [null, new RoutedEventArgs()]);

    private static void ClickCalcFull(MainWindow window) =>
        CalcFullClickMethod.Invoke(window, [null, new RoutedEventArgs()]);

    [Fact]
    public void ManualCalculationMode_PlainF9AfterEditingAPrecedent_CatchesUpTheStaleDependentFormula()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            // Enter A1 and B1=A1*2 in Automatic mode first, so B1 starts out correctly computed
            // (mirrors R120_ManualModeFreshFormulaRecalcTests's own repro setup exactly).
            harness.SetCellNumber(1, 1, 5); // A1 = 5
            harness.SelectActiveCell(2, 1); // B1
            harness.SetFormulaEditCell(2, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=A1*2");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();
            harness.CellValue(2, 1).Should().Be(new NumberValue(10));

            harness.ActiveWorkbook.CalculationMode = WorkbookCalculationMode.Manual;

            // Edit A1 (B1's precedent) via the real formula-bar commit path -- ordinary,
            // legitimate Manual-mode UI editing, not a raw model bypass.
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("100");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellValue(1, 1).Should().Be(new NumberValue(100));
            harness.CellValue(2, 1).Should().Be(
                new NumberValue(10),
                "Manual mode must leave B1 stale immediately after the precedent edit (R120's " +
                "own intentional, tested behavior)");

            // The exact user action the defect is about: press plain F9 ("Calculate Now").
            ClickCalcNow(harness.Window);

            harness.CellValue(2, 1).Should().Be(
                new NumberValue(200),
                "plain F9 is Manual calculation mode's one explicit recalculation trigger and " +
                "must catch up every formula left stale by an ordinary precedent edit, exactly " +
                "as real Excel does -- it must not be a silent no-op");
        });
    }

    // No-regression sibling: Automatic mode's plain-F9 scope must stay the cheap dirty-only pass
    // (R79-calc-volatile-recalc-5-1) -- the Manual-mode fallback added by this fix must not widen
    // to Automatic mode and start force-reevaluating formula cells the tracked dependency graph
    // has no reason to consider dirty.
    [Fact]
    public void AutomaticCalculationMode_PlainF9_StillDoesNotReevaluateAFormulaCellTheGraphNeverObserved()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            harness.ActiveWorkbook.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

            harness.SetCellNumber(1, 1, 5); // A1 = 5
            harness.SetCellFormula(2, 1, "A1*2"); // B1 = A1*2, entered as raw model state

            // Seed the dependency graph via one full recalc (Ctrl+Alt+F9), matching R79's own
            // steady-state setup -- plain F9's dirty-only scope has nothing to work from until
            // the graph has been built at least once.
            ClickCalcFull(harness.Window);
            harness.CellValue(2, 1).Should().Be(new NumberValue(10));

            // Mutate A1 directly, bypassing the edit pipeline entirely -- simulates a precedent
            // change the tracked dependency graph never observed (R79's own scenario).
            harness.SetCellNumber(1, 1, 9);

            ClickCalcNow(harness.Window);

            harness.CellValue(2, 1).Should().Be(
                new NumberValue(10),
                "Automatic mode's plain F9 must remain the cheap dirty-only scope -- this fix " +
                "must only add a full-recalc fallback for Manual mode, not widen F9's scope for " +
                "Automatic mode");
        });
    }
}
