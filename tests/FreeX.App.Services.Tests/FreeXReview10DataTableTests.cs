using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression test for round-10 review finding P19: Goal Seek in Manual calculation mode must
/// still refresh the set cell (and the rest of the dependency chain from the changing cell)
/// once its result is applied, instead of leaving the grid showing the pre-seek value until the
/// user presses F9. Excel treats Goal Seek's recalculation as a deliberate one-time action that
/// is not subject to the "only recalc on demand" rule that otherwise governs Manual mode.
/// </summary>
public sealed class FreeXReview10DataTableTests
{
    [Fact]
    public void ExecuteGoalSeek_ManualCalculationMode_RefreshesSetCellAfterApplyingResult()
    {
        var (workbook, sheet, service, _) = CreateEditService();
        var changingCell = new CellAddress(sheet.Id, 1, 1); // A1
        var setCell = new CellAddress(sheet.Id, 1, 2);      // B1 = A1*10

        sheet.SetCell(changingCell, new NumberValue(2));
        sheet.SetFormula(setCell, "A1*10");
        // Establish the initial cached value (B1 = 20) exactly as a prior automatic recalculation
        // would have, before the workbook is switched to Manual.
        service.RecalculateAll(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.Manual;

        var result = service.ExecuteGoalSeek(workbook, new GoalSeekRequest(setCell, 100, changingCell));

        result.Success.Should().BeTrue();
        result.EditResult.Should().NotBeNull();
        result.EditResult!.Success.Should().BeTrue();

        // The changing cell must hold the found value...
        sheet.GetValue(changingCell).Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(10, 1e-4);

        // ...and critically, the SET cell must already reflect that new value (100), not the
        // stale pre-seek value (20), even though calculation mode is Manual. Before the fix,
        // ApplyHistoryOutcome's RecalculateIfAutomatic was a no-op in Manual mode and Goal Seek's
        // own finally-block recalculation had already restored B1 to the ORIGINAL value's result
        // (20) as part of its convergence search cleanup.
        sheet.GetValue(setCell).Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(100, 1e-4);
    }

    [Fact]
    public void ExecuteGoalSeek_AutomaticCalculationMode_StillRefreshesSetCell()
    {
        // Regression guard: the Automatic-mode path (already working) must be unaffected by the
        // Manual-mode fix — RecalculateIfAutomatic already covers it, and the fix must not
        // recalculate twice in a way that changes the outcome.
        var (workbook, sheet, service, _) = CreateEditService();
        var changingCell = new CellAddress(sheet.Id, 1, 1);
        var setCell = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(changingCell, new NumberValue(2));
        sheet.SetFormula(setCell, "A1*10");
        service.RecalculateAll(workbook);

        var result = service.ExecuteGoalSeek(workbook, new GoalSeekRequest(setCell, 100, changingCell));

        result.Success.Should().BeTrue();
        sheet.GetValue(setCell).Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(100, 1e-4);
    }

    [Fact]
    public void ApplyGoalSeekProposal_ManualCalculationMode_RefreshesSetCellAfterConfirmation()
    {
        var (workbook, sheet, service, _) = CreateEditService();
        var changingCell = new CellAddress(sheet.Id, 1, 1);
        var setCell = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(changingCell, new NumberValue(2));
        sheet.SetFormula(setCell, "A1*10");
        service.RecalculateAll(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        var request = new GoalSeekRequest(setCell, 100, changingCell);
        var proposal = service.FindGoalSeekProposal(workbook, request);

        var result = service.ApplyGoalSeekProposal(workbook, proposal);

        result.Success.Should().BeTrue();
        sheet.GetValue(changingCell).Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(10, 1e-4);
        sheet.GetValue(setCell).Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(100, 1e-4);
    }

    private static (Workbook Workbook, Sheet Sheet, WorkbookCellEditService Service, RecalcEngine RecalcEngine)
        CreateEditService()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);
        return (workbook, sheet, service, recalcEngine);
    }
}
