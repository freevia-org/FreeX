using System.Reflection;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R125-app-host-requiresfullrecalc-defensive: <c>FreeX.App.Services.WorkbookCellEditService
/// .ApplyHistoryOutcome</c> decides full-vs-targeted recalc by checking
/// <c>CommandOutcome.RequiresFullRecalc</c> FIRST, falling back to a targeted recalc of
/// <c>AffectedCells</c> only when the flag is clear. This WPF host's own choke point
/// (<c>RecalculateAfterCommandOutcome</c>, <c>MainWindow.CommandExecution.cs</c>) used to infer
/// "needs a full recalc" purely from <c>AffectedCells</c> being empty, silently agreeing with the
/// shared service today only because every current <c>IWholeWorkbookRecalcCommand</c>
/// (Add/Rename/Remove/Move/MoveSheets/DuplicateSheet) happens to also report an empty
/// <c>AffectedCells</c> on Undo/Redo (see <c>CommandBus.Undo</c>/<c>Redo</c>). Nothing enforced that
/// agreement: a command that reported BOTH <c>RequiresFullRecalc: true</c> AND a non-empty
/// <c>AffectedCells</c> would have silently fallen through to a targeted recalc on this shell while
/// the shared service correctly forced a full one. This test proves the dispatch directly (via
/// reflection into the private choke point, since no current production command exercises the
/// combination) rather than relying on a future command to exist.
/// </summary>
public sealed class R125_RequiresFullRecalcDefensiveTests
{
    /// <summary>
    /// Fail-before/pass-after: a synthetic outcome with RequiresFullRecalc=true and a non-empty
    /// AffectedCells that does NOT include B1 must still recompute B1. B1's formula is planted via
    /// the harness's raw SetCellFormula (bypassing the normal edit path), so B1-depends-on-A1 is
    /// NOT yet registered in the dependency graph -- only a full RecalculateAllFormulas pass (which
    /// rebuilds the graph from every cell's formula text) discovers and evaluates it; a targeted
    /// Recalculate(workbook, affectedCells) that excludes B1 leaves it at its pre-existing blank
    /// value. Before the fix, RecalculateAfterCommandOutcome only looked at AffectedCells being
    /// non-empty and took the targeted branch, leaving B1 blank. After the fix, the RequiresFullRecalc
    /// flag forces the full-recalc branch and B1 comes out correctly as 6.
    /// </summary>
    [Fact]
    public void RequiresFullRecalc_WithNonEmptyAffectedCells_ForcesFullRecalcNotTargeted()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SetCellNumber(1, 1, 5); // A1 = 5, raw (no recalc/dependency registration)
            harness.SetCellFormula(2, 1, "=A1+1"); // B1 = "=A1+1", raw -- dependency NOT registered
            harness.CellValue(2, 1).Should().Be(BlankValue.Instance, "B1 was set raw and never evaluated");

            var sheetId = harness.ActiveWorkbook.Sheets[0].Id;
            var outcome = new CommandOutcome(
                true,
                AffectedCells: [new CellAddress(sheetId, 1, 1)], // A1 only -- deliberately excludes B1
                RequiresFullRecalc: true);

            var method = typeof(MainWindow).GetMethod(
                "RecalculateAfterCommandOutcome",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            method!.Invoke(harness.Window, [outcome]);

            harness.CellValue(2, 1).Should().Be(new NumberValue(6),
                "RequiresFullRecalc=true must force a full recalc even though AffectedCells is non-empty");
        });
    }

    /// <summary>
    /// No-regression sibling: the ordinary case (RequiresFullRecalc=false, non-empty AffectedCells)
    /// must keep taking the cheap targeted branch -- i.e. must NOT force a full recalc that would
    /// also happen to discover B1's unregistered dependency. This pins the existing "empty vs
    /// non-empty AffectedCells" behavior for the common path so the defensive flag check above only
    /// changes behavior for the RequiresFullRecalc=true case.
    /// </summary>
    [Fact]
    public void RequiresFullRecalcFalse_WithNonEmptyAffectedCells_StaysTargeted()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SetCellNumber(1, 1, 5); // A1 = 5, raw
            harness.SetCellFormula(2, 1, "=A1+1"); // B1 = "=A1+1", raw -- dependency NOT registered
            harness.CellValue(2, 1).Should().Be(BlankValue.Instance);

            var sheetId = harness.ActiveWorkbook.Sheets[0].Id;
            var outcome = new CommandOutcome(
                true,
                AffectedCells: [new CellAddress(sheetId, 1, 1)], // A1 only -- excludes B1
                RequiresFullRecalc: false);

            var method = typeof(MainWindow).GetMethod(
                "RecalculateAfterCommandOutcome",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method!.Invoke(harness.Window, [outcome]);

            harness.CellValue(2, 1).Should().Be(BlankValue.Instance,
                "a targeted recalc of A1 alone must not reach B1's unregistered dependency");
        });
    }

    /// <summary>
    /// No-regression sibling: the empty-AffectedCells case (which already forced a full recalc
    /// before this fix) must keep doing so.
    /// </summary>
    [Fact]
    public void EmptyAffectedCells_StillForcesFullRecalc()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SetCellNumber(1, 1, 5); // A1 = 5, raw
            harness.SetCellFormula(2, 1, "=A1+1"); // B1 = "=A1+1", raw -- dependency NOT registered
            harness.CellValue(2, 1).Should().Be(BlankValue.Instance);

            var outcome = new CommandOutcome(true, AffectedCells: [], RequiresFullRecalc: false);

            var method = typeof(MainWindow).GetMethod(
                "RecalculateAfterCommandOutcome",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method!.Invoke(harness.Window, [outcome]);

            harness.CellValue(2, 1).Should().Be(new NumberValue(6),
                "empty AffectedCells must still fall back to a full recalc that discovers B1 fresh");
        });
    }
}
