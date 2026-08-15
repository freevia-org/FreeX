using FluentAssertions;
using FreeX.App.Presentation.ScenarioManager;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Covers the Scenario Manager "Merge..." planning support (Excel: Data > What-If Analysis >
/// Scenario Manager > Merge), which combines scenarios sourced from elsewhere into the active
/// workbook's scenario list. See R24-what-if-datatable-3.
/// </summary>
public sealed class ScenarioManagerMergePlanTests
{
    [Fact]
    public void TryParseAction_MapsMergeAlias()
    {
        ScenarioManagerPlanner.TryParseAction("merge", out var action).Should().BeTrue();
        action.Should().Be(ScenarioManagerAction.Merge);
    }

    [Fact]
    public void CreateMergePlan_MergesScenariosFromAnotherSourceIntoActiveWorkbook()
    {
        var workbook = CreateWorkbook(out var sheet);
        var firstCell = CellAddress.Parse("A1", sheet.Id);
        var secondCell = CellAddress.Parse("B2", sheet.Id);
        var sourceScenarios = new[]
        {
            new WorkbookScenario(
                "Merged Base Case",
                [new ScenarioCellValue(firstCell, new NumberValue(5))],
                "From another worksheet"),
            new WorkbookScenario(
                "Merged Upside",
                [new ScenarioCellValue(secondCell, new NumberValue(9))])
        };

        var plan = ScenarioManagerPlanner.CreateMergePlan(workbook, sourceScenarios);

        plan.IsReady.Should().BeTrue();
        plan.Operation.Should().Be(ScenarioManagerOperation.Merge);
        plan.StatusText.Should().Be("Ready to merge 2 scenarios into this workbook.");
        plan.AffectedCells.Should().Equal(firstCell, secondCell);
    }

    [Fact]
    public void CreateMergePlan_ReportsNoScenariosWhenSourceIsEmpty()
    {
        var workbook = CreateWorkbook(out _);

        var plan = ScenarioManagerPlanner.CreateMergePlan(workbook, []);

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(ScenarioManagerPlanStatus.NoScenarios);
        plan.StatusText.Should().Be("The source sheet or workbook has no scenarios to merge.");
    }

    [Fact]
    public void CreateMergePlan_RejectsSourceScenariosWithCellsOutsideTargetWorkbook()
    {
        var workbook = CreateWorkbook(out _);
        var externalCell = new CellAddress(SheetId.New(), 1, 1);
        var sourceScenarios = new[]
        {
            new WorkbookScenario("External", [new ScenarioCellValue(externalCell, new NumberValue(1))])
        };

        var plan = ScenarioManagerPlanner.CreateMergePlan(workbook, sourceScenarios);

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(ScenarioManagerPlanStatus.ChangingCellsOutsideWorkbook);
        plan.StatusText.Should().Be("Scenario changing cells must belong to this workbook.");
    }

    [Fact]
    public void CreateMergePlan_RejectsProtectedChangingCellsWithoutScenarioPermission()
    {
        var workbook = CreateWorkbook(out var sheet);
        sheet.IsProtected = true;
        var cell = CellAddress.Parse("A1", sheet.Id);
        var sourceScenarios = new[]
        {
            new WorkbookScenario("Protected", [new ScenarioCellValue(cell, new NumberValue(1))])
        };

        var blockedPlan = ScenarioManagerPlanner.CreateMergePlan(workbook, sourceScenarios);
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);
        var allowedPlan = ScenarioManagerPlanner.CreateMergePlan(workbook, sourceScenarios);

        blockedPlan.IsReady.Should().BeFalse();
        blockedPlan.Status.Should().Be(ScenarioManagerPlanStatus.ProtectedChangingCells);
        allowedPlan.IsReady.Should().BeTrue();
    }

    [Fact]
    public void CreateMergePlan_ReportsWorkbookUnavailable()
    {
        var plan = ScenarioManagerPlanner.CreateMergePlan(null, [
            new WorkbookScenario("Base", [new ScenarioCellValue(new CellAddress(SheetId.New(), 1, 1), new NumberValue(1))])
        ]);

        plan.IsReady.Should().BeFalse();
        plan.Operation.Should().Be(ScenarioManagerOperation.Merge);
        plan.Status.Should().Be(ScenarioManagerPlanStatus.NoWorkbook);
    }

    [Fact]
    public void RemapScenariosBySheetName_MapsWorkbookLocalIdsPreservesMetadataAndDropsUnresolvedScenarios()
    {
        var source = new Workbook("Source");
        var sourceBudget = source.AddSheet("Budget");
        var sourceMissing = source.AddSheet("Missing");
        var target = new Workbook("Target");
        var targetBudget = target.AddSheet("Budget");
        source.Scenarios.Add(new WorkbookScenario(
            "Upside",
            [new ScenarioCellValue(new CellAddress(sourceBudget.Id, 3, 2), new NumberValue(42))],
            "Imported assumptions",
            Hidden: true,
            Locked: true));
        source.Scenarios.Add(new WorkbookScenario(
            "Unresolved",
            [new ScenarioCellValue(new CellAddress(sourceMissing.Id, 1, 1), new NumberValue(1))]));

        var remapped = ScenarioManagerPlanner.RemapScenariosBySheetName(source, target);

        remapped.Should().ContainSingle();
        var scenario = remapped[0];
        scenario.Name.Should().Be("Upside");
        scenario.Comment.Should().Be("Imported assumptions");
        scenario.Hidden.Should().BeTrue();
        scenario.Locked.Should().BeTrue();
        scenario.ChangingCells.Should().ContainSingle();
        scenario.ChangingCells[0].Address.Should().Be(new CellAddress(targetBudget.Id, 3, 2));
        scenario.ChangingCells[0].Value.Should().Be(new NumberValue(42));
    }

    private static Workbook CreateWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Budget");
        sheet = workbook.AddSheet("Sheet1");
        return workbook;
    }
}
