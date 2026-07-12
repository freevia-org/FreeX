using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R35-deferred-scenario-merge-1: Excel's Scenario Manager "Merge..." command combines scenarios
/// saved on another worksheet/workbook into the active workbook's scenario set.
/// ScenarioManagerPlanner.CreateMergePlan already modeled the validation (no scenarios / changing
/// cells outside the workbook / protected changing cells) but had no command that actually
/// performed the merge, so the feature was entirely unreachable in shipped code. These tests cover
/// MergeScenarioCommand: merging scenarios "from another sheet" into Workbook.Scenarios, rejecting
/// merges that reference cells outside the workbook or on a protected sheet, de-duplicating a
/// merged scenario's name against one that already exists locally, and undo/redo.
/// </summary>
public sealed class R35_MergeScenarioCommandTests
{
    [Fact]
    public void Apply_MergesScenariosFromAnotherSheetIntoTargetWorkbookScenarios()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var ctx = new TestCommandContext(workbook);

        // Simulate a scenario "saved on another sheet" (Sheet2) that has not yet been merged into
        // the workbook's scenario set.
        var sourceScenario = new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(new CellAddress(sheet2.Id, 1, 1), new NumberValue(100))]);

        workbook.Scenarios.Should().BeEmpty();

        var outcome = new MergeScenarioCommand([sourceScenario]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Best Case");
        workbook.Scenarios[0].ChangingCells[0].Address.Sheet.Should().Be(sheet2.Id);
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(100));

        // Sibling no-regression: Sheet1 is untouched and unrelated to the merge.
        sheet1.GetValue(1, 1).Should().Be(new BlankValue());
    }

    [Fact]
    public void Apply_UniquifiesMergedScenarioNameThatAlreadyExistsLocally()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(1))]));

        var sourceScenario = new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(new CellAddress(sheet.Id, 2, 1), new NumberValue(2))]);

        var outcome = new MergeScenarioCommand([sourceScenario]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().HaveCount(2);
        workbook.Scenarios[0].Name.Should().Be("Best Case");
        workbook.Scenarios[1].Name.Should().Be("Best Case (2)");
    }

    [Fact]
    public void Apply_RejectsMergeWhenSourceScenarioReferencesCellOutsideTargetWorkbook()
    {
        var workbook = new Workbook("test");
        workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var foreignSheetId = new SheetId(Guid.NewGuid());
        var sourceScenario = new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(new CellAddress(foreignSheetId, 1, 1), new NumberValue(1))]);

        var outcome = new MergeScenarioCommand([sourceScenario]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        workbook.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void Apply_RejectsMergeWhenChangingCellsAreProtectedWithoutEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(workbook);

        var sourceScenario = new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(1))]);

        var outcome = new MergeScenarioCommand([sourceScenario]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        workbook.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void Apply_RejectsMergeWhenThereAreNoSourceScenarios()
    {
        var workbook = new Workbook("test");
        workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var outcome = new MergeScenarioCommand([]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        workbook.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void Revert_RemovesExactlyTheScenariosThisCommandMerged()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Existing",
            [new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(1))]));

        var command = new MergeScenarioCommand([
            new WorkbookScenario(
                "Merged One",
                [new ScenarioCellValue(new CellAddress(sheet.Id, 2, 1), new NumberValue(2))]),
            new WorkbookScenario(
                "Merged Two",
                [new ScenarioCellValue(new CellAddress(sheet.Id, 3, 1), new NumberValue(3))])
        ]);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().HaveCount(3);

        command.Revert(ctx);

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Existing");
    }
}
