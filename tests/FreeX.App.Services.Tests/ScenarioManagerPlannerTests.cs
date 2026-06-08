using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ScenarioManagerPlannerTests
{
    [Fact]
    public void CreateDialogPlan_ListsScenariosAndSelectsRequestedScenario()
    {
        var workbook = CreateWorkbook(out var sheet);
        var baselineCell = CellAddress.Parse("A1", sheet.Id);
        var forecastCell = CellAddress.Parse("B2", sheet.Id);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Baseline",
            [new ScenarioCellValue(baselineCell, new NumberValue(10))],
            "Original forecast"));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Forecast",
            [new ScenarioCellValue(forecastCell, new NumberValue(25))],
            "Stretch target",
            Hidden: true,
            Locked: true));

        var plan = ScenarioManagerPlanner.CreateDialogPlan(workbook, "forecast");

        plan.IsReady.Should().BeTrue();
        plan.Operation.Should().Be(ScenarioManagerOperation.OpenManager);
        plan.StatusText.Should().Be("Ready to manage 2 scenarios; 'Forecast' is selected.");
        plan.SelectedScenario.Should().NotBeNull();
        plan.SelectedScenario!.Name.Should().Be("Forecast");
        plan.Scenarios.Should().Equal(
            new ScenarioManagerScenarioChoice("Baseline", 1, "Original forecast", false, false, false),
            new ScenarioManagerScenarioChoice("Forecast", 1, "Stretch target", true, true, true));
    }

    [Fact]
    public void CreateShowPlan_ReturnsAffectedCellsForExistingScenario()
    {
        var workbook = CreateWorkbook(out var sheet);
        var firstCell = CellAddress.Parse("A1", sheet.Id);
        var secondCell = CellAddress.Parse("C3", sheet.Id);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Quarter End",
            [
                new ScenarioCellValue(firstCell, new NumberValue(1)),
                new ScenarioCellValue(secondCell, new TextValue("done"))
            ]));

        var plan = ScenarioManagerPlanner.CreateShowPlan(workbook, "quarter end");

        plan.IsReady.Should().BeTrue();
        plan.Operation.Should().Be(ScenarioManagerOperation.Show);
        plan.StatusText.Should().Be("Ready to show scenario 'Quarter End' affecting 2 cells.");
        plan.SelectedScenario.Should().NotBeNull();
        plan.SelectedScenario!.Name.Should().Be("Quarter End");
        plan.AffectedCells.Should().Equal(firstCell, secondCell);
    }

    [Fact]
    public void CreateSavePlan_RejectsDuplicateNamesExceptReplacementTarget()
    {
        var workbook = CreateWorkbook(out var sheet);
        var cell = CellAddress.Parse("A1", sheet.Id);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Baseline",
            [new ScenarioCellValue(cell, new NumberValue(10))]));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Forecast",
            [new ScenarioCellValue(CellAddress.Parse("B1", sheet.Id), new NumberValue(20))]));

        var duplicatePlan = ScenarioManagerPlanner.CreateSavePlan(
            workbook,
            new ScenarioManagerSaveRequest(
                "baseline",
                [new ScenarioCellValue(cell, new NumberValue(11))],
                ReplaceScenarioName: "Forecast"));
        var replacePlan = ScenarioManagerPlanner.CreateSavePlan(
            workbook,
            new ScenarioManagerSaveRequest(
                "baseline",
                [new ScenarioCellValue(cell, new NumberValue(11))],
                ReplaceScenarioName: "Baseline"));

        duplicatePlan.IsReady.Should().BeFalse();
        duplicatePlan.Status.Should().Be(ScenarioManagerPlanStatus.ScenarioNameDuplicate);
        duplicatePlan.StatusText.Should().Be("A scenario named 'Baseline' already exists.");
        replacePlan.IsReady.Should().BeTrue();
        replacePlan.StatusText.Should().Be("Ready to save scenario 'baseline' with 1 changing cell.");
        replacePlan.AffectedCells.Should().Equal(cell);
    }

    [Fact]
    public void CreateSavePlan_RejectsChangingCellsOutsideWorkbook()
    {
        var workbook = CreateWorkbook(out _);
        var externalCell = new CellAddress(SheetId.New(), 1, 1);

        var plan = ScenarioManagerPlanner.CreateSavePlan(
            workbook,
            new ScenarioManagerSaveRequest(
                "External",
                [new ScenarioCellValue(externalCell, new NumberValue(1))]));

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(ScenarioManagerPlanStatus.ChangingCellsOutsideWorkbook);
        plan.StatusText.Should().Be("Scenario changing cells must belong to this workbook.");
    }

    [Fact]
    public void CreateSavePlan_RejectsProtectedChangingCellsWithoutScenarioPermission()
    {
        var workbook = CreateWorkbook(out var sheet);
        sheet.IsProtected = true;
        var cell = CellAddress.Parse("A1", sheet.Id);

        var blockedPlan = ScenarioManagerPlanner.CreateSavePlan(
            workbook,
            new ScenarioManagerSaveRequest(
                "Protected",
                [new ScenarioCellValue(cell, new NumberValue(1))]));
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);
        var allowedPlan = ScenarioManagerPlanner.CreateSavePlan(
            workbook,
            new ScenarioManagerSaveRequest(
                "Protected",
                [new ScenarioCellValue(cell, new NumberValue(1))]));

        blockedPlan.IsReady.Should().BeFalse();
        blockedPlan.Status.Should().Be(ScenarioManagerPlanStatus.ProtectedChangingCells);
        blockedPlan.StatusText.Should().Be("Scenario changing cells are protected on at least one worksheet.");
        allowedPlan.IsReady.Should().BeTrue();
    }

    [Fact]
    public void CreateSummaryReportPlan_RequiresScenariosAndWorkbookResultCells()
    {
        var workbook = CreateWorkbook(out var sheet);
        var resultCell = CellAddress.Parse("D4", sheet.Id);
        var emptyPlan = ScenarioManagerPlanner.CreateSummaryReportPlan(workbook, [resultCell]);
        var externalResultPlan = ScenarioManagerPlanner.CreateSummaryReportPlan(
            workbook,
            [new CellAddress(SheetId.New(), 1, 1)]);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Baseline",
            [new ScenarioCellValue(CellAddress.Parse("A1", sheet.Id), new NumberValue(10))]));
        var readyPlan = ScenarioManagerPlanner.CreateSummaryReportPlan(workbook, [resultCell, resultCell]);

        emptyPlan.IsReady.Should().BeFalse();
        emptyPlan.Status.Should().Be(ScenarioManagerPlanStatus.NoScenarios);
        externalResultPlan.Status.Should().Be(ScenarioManagerPlanStatus.NoScenarios);
        readyPlan.IsReady.Should().BeTrue();
        readyPlan.StatusText.Should().Be("Ready to create a scenario summary for 1 scenario.");
        readyPlan.ResultCells.Should().Equal(resultCell);
        readyPlan.AffectedCells.Should().Equal(CellAddress.Parse("A1", sheet.Id));
    }

    [Fact]
    public void PlansReportUnavailableWorkbookAndVisibleWorksheetStates()
    {
        var noWorkbookPlan = ScenarioManagerPlanner.CreateDialogPlan(null);
        var workbook = CreateWorkbook(out var sheet);
        sheet.IsHidden = true;

        var noVisibleSheetPlan = ScenarioManagerPlanner.CreateDialogPlan(workbook);

        noWorkbookPlan.Status.Should().Be(ScenarioManagerPlanStatus.NoWorkbook);
        noWorkbookPlan.StatusText.Should().Be("Open a workbook before using Scenario Manager.");
        noVisibleSheetPlan.Status.Should().Be(ScenarioManagerPlanStatus.NoVisibleWorksheet);
        noVisibleSheetPlan.StatusText.Should().Be("Scenario Manager requires at least one visible worksheet.");
    }

    private static Workbook CreateWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Budget");
        sheet = workbook.AddSheet("Sheet1");
        return workbook;
    }
}
