using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R120-app-host-manual-mode-fresh-formula-recalc: <c>MainWindow.RecalculateIfAutomatic</c> --
/// the choke point every ordinary cell edit across the WPF shell recalculates through (see
/// <c>MainWindow.WorkbookUiState.cs</c>) -- used to switch on <see cref="WorkbookCalculationMode"/>
/// with <c>_ =&gt; null</c> as the fallback, so Manual mode silently skipped evaluating a
/// brand-new formula the user had just typed: the committed cell kept
/// <see cref="BlankValue"/> (<see cref="Cell.FromFormula"/>'s initial value) until the next F9.
/// Real Excel always computes a newly typed/edited formula once on entry regardless of
/// calculation mode -- only recalculation triggered by a later edit to one of that formula's
/// PRECEDENTS is what Manual mode defers. This mirrors
/// <c>FreeX.App.Services.WorkbookCellEditService</c>'s own
/// <c>RecalculateIfAutomatic(...) ?? RecalculateFreshlyEnteredFormulasOnce(...)</c> fallback
/// (see <c>WorkbookCellEditServiceTests.CommitCellText_ManualCalculationMode_ComputesNewlyEnteredFormulaOnce</c>),
/// which the Avalonia shell already gets via that shared service but the WPF host -- which owns
/// its own raw <c>_recalcEngine</c>/<c>_commandBus</c> instead of routing through that service --
/// did not.
/// </summary>
public sealed class R120_ManualModeFreshFormulaRecalcTests
{
    [Fact]
    public void ManualCalculationMode_CommittingANewFormulaFromTheFormulaBar_ComputesItOnce()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            harness.ActiveWorkbook.CalculationMode = WorkbookCalculationMode.Manual;

            harness.SetCellNumber(1, 1, 5); // A1 = 5

            harness.SelectActiveCell(2, 1); // B1
            harness.SetFormulaEditCell(2, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=A1*2");

            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellFormula(2, 1).Should().Be("A1*2");
            harness.CellValue(2, 1).Should().Be(
                new NumberValue(10),
                "Excel always computes a brand-new formula once on entry, even in Manual " +
                "calculation mode -- only propagation to cells depending on a later-changed " +
                "precedent is deferred until the next F9");
        });
    }

    // No-regression sibling: a precedent-only edit (a plain value committed into a cell some
    // OTHER, already-evaluated formula depends on) must still leave that other formula stale in
    // Manual mode -- the fallback must only ever recalculate the freshly-entered formula cell(s)
    // themselves, never cascade to their dependents, exactly as Manual mode intends.
    [Fact]
    public void ManualCalculationMode_EditingAPrecedentAfterwards_LeavesTheDependentFormulaStaleUntilRecalculated()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            // Enter A1 and B1=A1*2 in Automatic mode first, so B1 starts out correctly computed.
            harness.SetCellNumber(1, 1, 5); // A1 = 5
            harness.SelectActiveCell(2, 1); // B1
            harness.SetFormulaEditCell(2, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=A1*2");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();
            harness.CellValue(2, 1).Should().Be(new NumberValue(10));

            harness.ActiveWorkbook.CalculationMode = WorkbookCalculationMode.Manual;

            // Now edit A1 (B1's precedent) via the same real formula-bar commit path.
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("100");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellValue(1, 1).Should().Be(new NumberValue(100));
            harness.CellValue(2, 1).Should().Be(
                new NumberValue(10),
                "Manual mode must defer recalculating a formula whose PRECEDENT changed until " +
                "the user explicitly recalculates (F9), even though it always computes a " +
                "brand-new formula the instant it is entered");
        });
    }
}
