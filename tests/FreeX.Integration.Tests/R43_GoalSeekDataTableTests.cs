using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// Round-43 Goal Seek / Data Table findings.
///
/// R43-commands-goalseek-datatable-3-1: the WPF host's Goal Seek command
/// (MainWindow.DataCommands.cs's GoalSeekBtn_Click) applies its result via GoalSeekCommand and
/// then only calls RecalculateIfAutomatic — a no-op outside Automatic/AutomaticExceptDataTables
/// calculation mode — so in Manual mode the set cell (and the rest of the dependency chain from
/// the changing cell) kept showing its stale pre-seek value until the user pressed F9. Excel
/// treats Goal Seek's recalculation as a deliberate one-time action that always refreshes the set
/// cell, even in Manual mode. These tests exercise the exact underlying command-logic pattern the
/// fixed GoalSeekBtn_Click now follows: apply GoalSeekCommand, then force a recalculation of the
/// changing cell whenever the workbook's calculation mode is not Automatic/
/// AutomaticExceptDataTables (mirroring FreeX.App.Services.WorkbookCellEditService.ExecuteGoalSeek,
/// which the WPF host's own Goal Seek command does not route through).
/// </summary>
public sealed class R43_GoalSeekDataTableTests
{
    private static (Workbook workbook, Sheet sheet, RecalcEngine engine, CellAddress changingCell, CellAddress setCell) CreateGoalSeekScenario(
        WorkbookCalculationMode calcMode)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);

        var changingCell = new CellAddress(sheet.Id, 1, 1); // A1
        var setCell = new CellAddress(sheet.Id, 1, 2);      // B1 = A1*10

        sheet.SetCell(changingCell, new NumberValue(2));
        sheet.SetFormula(setCell, "A1*10");
        engine.RegisterFormulaDependencies(
            setCell, FormulaEvaluator.ParseFormula("A1*10"), sheet.Id, workbook);

        // Establish the initial cached value (B1 = 20) exactly as a prior automatic recalculation
        // would have, before the workbook is switched to the mode under test.
        engine.Recalculate(workbook, [setCell]);
        sheet.GetValue(setCell).Should().Be(new NumberValue(20));

        workbook.CalculationMode = calcMode;
        return (workbook, sheet, engine, changingCell, setCell);
    }

    /// <summary>Reproduces the fixed GoalSeekBtn_Click sequence: apply GoalSeekCommand, run the
    /// "RecalculateIfAutomatic"-equivalent no-op, then force a recalculation of the changing cell
    /// whenever the mode is not Automatic/AutomaticExceptDataTables.</summary>
    private static void ApplyGoalSeekAndRecalculateLikeWpfHost(
        Workbook workbook, RecalcEngine engine, ICommandContext ctx, CellAddress changingCell, double foundValue)
    {
        var cmd = new GoalSeekCommand(changingCell, foundValue);
        var outcome = cmd.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // RecalculateIfAutomatic: a no-op outside Automatic/AutomaticExceptDataTables.
        if (workbook.CalculationMode is WorkbookCalculationMode.Automatic or WorkbookCalculationMode.AutomaticExceptDataTables)
            engine.Recalculate(workbook, [changingCell]);

        // The round-43 fix: force the recalculation when it was skipped above, exactly as
        // GoalSeekBtn_Click now does.
        if (workbook.CalculationMode is not (WorkbookCalculationMode.Automatic or WorkbookCalculationMode.AutomaticExceptDataTables))
            engine.Recalculate(workbook, [changingCell]);
    }

    [Fact]
    public void GoalSeekApply_ManualCalculationMode_RefreshesSetCellAfterApplyingResult()
    {
        var (workbook, sheet, engine, changingCell, setCell) =
            CreateGoalSeekScenario(WorkbookCalculationMode.Manual);
        var ctx = new TestCommandContext(workbook);

        ApplyGoalSeekAndRecalculateLikeWpfHost(workbook, engine, ctx, changingCell, foundValue: 10);

        sheet.GetValue(changingCell).Should().Be(new NumberValue(10));
        // Before the fix this stayed at 20 (the stale pre-seek value) until a manual F9/Shift+F9.
        sheet.GetValue(setCell).Should().Be(new NumberValue(100));
    }

    [Fact]
    public void GoalSeekApply_AutomaticCalculationMode_StillRefreshesSetCellAfterApplyingResult()
    {
        // Sibling no-regression case: Automatic mode already refreshed correctly via
        // RecalculateIfAutomatic before this fix, and must keep doing so (the fix's extra
        // "not Automatic" branch must stay a no-op here, not double-recalculate incorrectly).
        var (workbook, sheet, engine, changingCell, setCell) =
            CreateGoalSeekScenario(WorkbookCalculationMode.Automatic);
        var ctx = new TestCommandContext(workbook);

        ApplyGoalSeekAndRecalculateLikeWpfHost(workbook, engine, ctx, changingCell, foundValue: 10);

        sheet.GetValue(changingCell).Should().Be(new NumberValue(10));
        sheet.GetValue(setCell).Should().Be(new NumberValue(100));
    }
}
