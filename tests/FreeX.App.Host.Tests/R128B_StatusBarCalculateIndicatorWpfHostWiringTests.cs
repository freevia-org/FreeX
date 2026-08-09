using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R128B-app-host-status-bar-calculate-indicator (ScopeAudit follow-up to R128): the r128 fix wired
/// Excel's Manual-mode "Calculate" status-bar indicator end to end for the Avalonia shell -- via
/// <c>FreeX.App.Services.WorkbookCellEditService</c> setting/clearing
/// <see cref="Workbook.HasPendingManualRecalculation"/> -- but the WPF host (<see cref="MainWindow"/>)
/// never routes ordinary cell edits or recalculation through that shared service. It has its own
/// parallel <c>_recalcEngine</c>/<c>_commandBus</c> pipeline in <c>MainWindow.WorkbookUiState.cs</c>
/// and <c>MainWindow.FormulaCommands.cs</c>, so the flag could never become true on Windows: the
/// read side (<c>MainWindow.GridStatus.cs</c>'s <c>RefreshStatusBar</c>) was wired, but nothing ever
/// wrote to the flag, so the "Calculate" indicator would always evaluate to "Ready" for a real
/// Windows user, reproducing the exact defect the r128 fix was meant to close.
/// </summary>
/// <remarks>
/// Fix: mirrors <c>FreeX.App.Services.WorkbookCellEditService</c>'s R128 pattern independently in
/// each of the WPF host's own choke points/explicit-recalc handlers:
/// <list type="bullet">
/// <item><c>RecalculateIfAutomatic</c> (the single choke point every ordinary cell edit across the
/// shell reaches -- see its own R120 comment) now sets the flag whenever the workbook is in Manual
/// mode and the edit affected at least one cell, mirroring
/// <c>WorkbookCellEditService.ApplyHistoryOutcome</c>'s tail.</item>
/// <item><c>RecalculateWorkbook</c> (Ctrl+Alt+F9), <c>RecalculateDirtyCells</c> (F9),
/// <c>RebuildDependenciesAndCalculate</c> (Ctrl+Alt+Shift+F9), and <c>CalcSheetBtn_Click</c>
/// (Shift+F9) now clear the flag, mirroring <c>WorkbookCellEditService.RecalculateAll</c>/
/// <c>RecalculateSheet</c>.</item>
/// </list>
/// </remarks>
public sealed class R128B_StatusBarCalculateIndicatorWpfHostWiringTests
{
    private static readonly MethodInfo CalcNowClickMethod =
        typeof(MainWindow).GetMethod("CalcNowBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "CalcNowBtn_Click");

    private static readonly MethodInfo CalcFullClickMethod =
        typeof(MainWindow).GetMethod("CalcFullBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "CalcFullBtn_Click");

    private static readonly MethodInfo CalcSheetClickMethod =
        typeof(MainWindow).GetMethod("CalcSheetBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "CalcSheetBtn_Click");

    private static readonly MethodInfo RebuildDependenciesAndCalculateMethod =
        typeof(MainWindow).GetMethod("RebuildDependenciesAndCalculate", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "RebuildDependenciesAndCalculate");

    private static void ClickCalcNow(MainWindow window) =>
        CalcNowClickMethod.Invoke(window, [null, new RoutedEventArgs()]);

    private static void ClickCalcFull(MainWindow window) =>
        CalcFullClickMethod.Invoke(window, [null, new RoutedEventArgs()]);

    private static void ClickCalcSheet(MainWindow window) =>
        CalcSheetClickMethod.Invoke(window, [null, new RoutedEventArgs()]);

    private static void InvokeRebuildDependenciesAndCalculate(MainWindow window) =>
        RebuildDependenciesAndCalculateMethod.Invoke(window, null);

    /// <summary>
    /// The core repro: real Windows-user action (edit a precedent cell via the formula bar while in
    /// Manual mode) must set <see cref="Workbook.HasPendingManualRecalculation"/> so the status bar
    /// shows "Calculate" instead of "Ready" -- this is the assertion that FAILS before the fix
    /// (RecalculateIfAutomatic never touched the flag) and PASSES after.
    /// </summary>
    [Fact]
    public void ManualMode_PrecedentEditViaFormulaBar_SetsHasPendingManualRecalculation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            // A1 = 5, B1 = A1*2 in Automatic mode first (mirrors R127's own repro setup).
            harness.SetCellNumber(1, 1, 5);
            harness.SelectActiveCell(2, 1);
            harness.SetFormulaEditCell(2, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=A1*2");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();
            harness.CellValue(2, 1).Should().Be(new NumberValue(10));

            harness.ActiveWorkbook.CalculationMode = WorkbookCalculationMode.Manual;
            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeFalse(
                "nothing has been edited yet since entering Manual mode");

            // Edit A1 (B1's precedent) via the real formula-bar commit path -- the exact user
            // action Excel's status-bar "Calculate" indicator exists to warn about.
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("100");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeTrue(
                "a Manual-mode precedent edit leaves B1 stale until the next explicit " +
                "recalculation, which is exactly what Excel's status-bar 'Calculate' indicator " +
                "must warn the real Windows user about");
        });
    }

    /// <summary>
    /// A freshly entered formula (not just a precedent edit) must also flag pending recalculation
    /// in Manual mode, mirroring the shared service's
    /// <c>CommitCellText_ManualMode_FreshFormulaEntry_AlsoSetsHasPendingManualRecalculation</c> --
    /// this exercises the switch's non-null <c>report</c> path (<c>RecalculateFreshlyEnteredFormulasOnce</c>),
    /// distinct from the precedent-only edit above which leaves <c>report</c> null.
    /// </summary>
    [Fact]
    public void ManualMode_FreshFormulaEntry_AlsoSetsHasPendingManualRecalculation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            harness.ActiveWorkbook.CalculationMode = WorkbookCalculationMode.Manual;

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=1+1");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeTrue(
                "even though the freshly entered formula itself gets evaluated immediately, some " +
                "other formula could depend on this cell and stay stale until the next explicit " +
                "recalculation");
        });
    }

    [Fact]
    public void ManualMode_PlainF9AfterPrecedentEdit_ClearsHasPendingManualRecalculation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            SeedManualModePrecedentEdit(harness);
            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeTrue("precondition");

            ClickCalcNow(harness.Window);

            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeFalse(
                "plain F9 is Manual mode's explicit recalculation trigger and must clear the " +
                "pending-recalculation flag once it has run, the same way " +
                "WorkbookCellEditService.RecalculateAll does on the Avalonia shell");
        });
    }

    [Fact]
    public void ManualMode_CalculateFull_ClearsHasPendingManualRecalculation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            SeedManualModePrecedentEdit(harness);
            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeTrue("precondition");

            ClickCalcFull(harness.Window);

            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeFalse(
                "Ctrl+Alt+F9 ('Calculate Full') must clear the pending-recalculation flag");
        });
    }

    [Fact]
    public void ManualMode_CalculateSheet_ClearsHasPendingManualRecalculation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            SeedManualModePrecedentEdit(harness);
            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeTrue("precondition");

            ClickCalcSheet(harness.Window);

            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeFalse(
                "Shift+F9 ('Calculate Sheet') must clear the pending-recalculation flag -- it is " +
                "workbook-scoped (matching Excel's own workbook-level 'Calculate' indicator), so " +
                "Shift+F9 clears it the same as a full recalc rather than tracking per-sheet " +
                "staleness, mirroring WorkbookCellEditService.RecalculateSheet");
        });
    }

    [Fact]
    public void ManualMode_RebuildDependenciesAndCalculate_ClearsHasPendingManualRecalculation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            SeedManualModePrecedentEdit(harness);
            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeTrue("precondition");

            InvokeRebuildDependenciesAndCalculate(harness.Window);

            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeFalse(
                "Ctrl+Alt+Shift+F9 must clear the pending-recalculation flag");
        });
    }

    // No-regression sibling: Automatic mode must never set the flag in the first place -- this
    // fix must only ever set HasPendingManualRecalculation for Manual mode, matching
    // WorkbookCellEditService's own Automatic-mode no-op behavior (see
    // R128_StatusBarCalculateIndicatorWiringTests.CommitCellText_AutomaticModes_NeverSetHasPendingManualRecalculation
    // in FreeX.App.Services.Tests).
    [Fact]
    public void AutomaticMode_EditingCells_NeverSetsHasPendingManualRecalculation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            harness.ActiveWorkbook.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

            harness.SetCellNumber(1, 1, 5);
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

            harness.ActiveWorkbook.HasPendingManualRecalculation.Should().BeFalse(
                "Automatic mode always keeps every dependent current as edits happen, so the " +
                "'Calculate' indicator must never appear -- this fix targets Manual mode only");
        });
    }

    private static void SeedManualModePrecedentEdit(MainWindowFormulaBarSyncTests.MainWindowHarness harness)
    {
        harness.SetCellNumber(1, 1, 5); // A1 = 5
        harness.SelectActiveCell(2, 1); // B1
        harness.SetFormulaEditCell(2, 1);
        harness.FocusFormulaBar();
        harness.SetFormulaBarText("=A1*2");
        harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();
        harness.CellValue(2, 1).Should().Be(new NumberValue(10));

        harness.ActiveWorkbook.CalculationMode = WorkbookCalculationMode.Manual;

        harness.SelectActiveCell(1, 1);
        harness.SetFormulaEditCell(1, 1);
        harness.FocusFormulaBar();
        harness.SetFormulaBarText("100");
        harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();
    }
}
