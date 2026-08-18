using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public sealed class ScenarioManagerCommandTests
{
    [Fact]
    public void SaveScenarioCommand_AddsScenarioAndUndoRemovesIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = new SaveScenarioCommand(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))],
            "Optimistic assumptions");

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Best Case");
        workbook.Scenarios[0].Comment.Should().Be("Optimistic assumptions");
        workbook.Scenarios[0].ChangingCells.Should().ContainSingle()
            .Which.Should().Be(new ScenarioCellValue(address, new NumberValue(42)));

        command.Revert(ctx);

        workbook.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void SaveScenarioCommand_PreservesHiddenAndLockedFlags()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = new SaveScenarioCommand(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))],
            "Optimistic assumptions",
            hidden: true,
            locked: true);

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Hidden.Should().BeTrue();
        workbook.Scenarios[0].Locked.Should().BeTrue();
    }

    [Fact]
    public void SaveScenarioCommand_ReplacesExistingScenarioAndUndoRestoresIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(10))]));

        var command = new SaveScenarioCommand(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(99))]);

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(99));

        command.Revert(ctx);

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void SaveScenarioCommand_RenamesExistingScenarioAndUndoRestoresIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(10))],
            "Original",
            Hidden: true,
            Locked: true));

        var command = new SaveScenarioCommand(
            "Upside Case",
            [new ScenarioCellValue(address, new NumberValue(99))],
            "Updated",
            hidden: false,
            locked: false,
            replaceScenarioName: "Best Case");

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Upside Case");
        workbook.Scenarios[0].Comment.Should().Be("Updated");
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(99));
        workbook.Scenarios[0].Hidden.Should().BeFalse();
        workbook.Scenarios[0].Locked.Should().BeFalse();

        command.Revert(ctx);

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Best Case");
        workbook.Scenarios[0].Comment.Should().Be("Original");
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(10));
        workbook.Scenarios[0].Hidden.Should().BeTrue();
        workbook.Scenarios[0].Locked.Should().BeTrue();
    }

    [Fact]
    public void SaveScenarioCommand_RejectsRenameToAnotherScenarioName()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", [new ScenarioCellValue(address, new NumberValue(10))]));
        workbook.Scenarios.Add(new WorkbookScenario("Worst Case", [new ScenarioCellValue(address, new NumberValue(1))]));

        var outcome = new SaveScenarioCommand(
            "Worst Case",
            [new ScenarioCellValue(address, new NumberValue(99))],
            replaceScenarioName: "Best Case").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("already exists");
        workbook.Scenarios.Select(scenario => scenario.Name).Should().Equal("Best Case", "Worst Case");
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(10));
        workbook.Scenarios[1].ChangingCells[0].Value.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void SaveScenarioCommand_RejectsProtectedSheetWithoutEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);

        var outcome = new SaveScenarioCommand(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        workbook.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void SaveScenarioCommand_AllowsProtectedSheetWithEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);

        var outcome = new SaveScenarioCommand(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().ContainSingle();
    }

    [Fact]
    public void ApplyScenarioCommand_AppliesChangingValuesAndUndoRestoresCells()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(first, new NumberValue(10));
        sheet.SetFormula(second, "A1*2");
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(first, new NumberValue(42)),
                new ScenarioCellValue(second, new TextValue("manual"))
            ]));

        var command = new ApplyScenarioCommand("Best Case");

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(42));
        sheet.GetCell(2, 1)!.FormulaText.Should().BeNull();
        sheet.GetValue(2, 1).Should().Be(new TextValue("manual"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
        sheet.GetCell(2, 1)!.FormulaText.Should().Be("A1*2");
    }

    [Fact]
    public void ApplyScenarioCommand_RejectsProtectedSheetWithoutEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(10));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))]));
        sheet.IsProtected = true;

        var outcome = new ApplyScenarioCommand("Best Case").Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void ApplyScenarioCommand_AllowsProtectedSheetWithEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(10));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))]));

        var outcome = new ApplyScenarioCommand("Best Case").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void ApplyScenarioCommand_LaterChangingCellSheetMissing_DoesNotPartiallyMutateEarlierCellsAndAllowsUndo()
    {
        // R142/GOALSEEK-WHATIF-1: a scenario's changing cells can span multiple sheets (FreeX
        // does not restrict a scenario to a single sheet the way Excel's own UI does -- see
        // SaveScenarioCommand, which has no same-sheet check). If a LATER changing cell's sheet
        // no longer exists in the workbook (e.g. it was deleted out from under a stale scenario),
        // Apply must reject the whole operation WITHOUT having already written the earlier,
        // still-valid changing cells -- otherwise the user is left with a silently mutated cell,
        // an error message claiming the operation failed outright, and (since CommandBus only
        // pushes to the undo stack on CommandOutcome.Success) no undo entry to recover it.
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var first = new CellAddress(sheet1.Id, 1, 1);
        sheet1.SetCell(first, new NumberValue(10));

        // Simulate the second changing cell's sheet having been removed from the workbook: a
        // SheetId that resolves to no live Sheet, exactly what ctx.Workbook.GetSheet(...) sees
        // for a changing cell left dangling by a sheet deletion.
        var missingSheet = new SheetId(Guid.NewGuid());
        var second = new CellAddress(missingSheet, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(first, new NumberValue(100)),
                new ScenarioCellValue(second, new NumberValue(200))
            ]));

        var command = new ApplyScenarioCommand("Best Case");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet1.GetValue(1, 1).Should().Be(new NumberValue(10), "the first changing cell must not be mutated when a later one fails validation");

        // Revert must also be a safe no-op: since Apply failed, CommandBus never pushes this
        // command to the undo stack, so nothing should change if Revert is called anyway.
        command.Revert(ctx);
        sheet1.GetValue(1, 1).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void DeleteScenarioCommand_RemovesScenarioAndUndoRestoresIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario("Base", [new ScenarioCellValue(address, new NumberValue(1))]));
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", [new ScenarioCellValue(address, new NumberValue(42))]));

        var command = new DeleteScenarioCommand("Best Case");

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.Scenarios.Select(scenario => scenario.Name).Should().Equal("Base");

        command.Revert(ctx);

        workbook.Scenarios.Select(scenario => scenario.Name).Should().Equal("Base", "Best Case");
    }

    [Fact]
    public void DeleteScenarioCommand_RejectsProtectedSheetWithoutEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", [new ScenarioCellValue(address, new NumberValue(42))]));
        sheet.IsProtected = true;

        var outcome = new DeleteScenarioCommand("Best Case").Apply(ctx);

        outcome.Success.Should().BeFalse();
        workbook.Scenarios.Should().ContainSingle();
    }

    [Fact]
    public void DeleteScenarioCommand_AllowsProtectedSheetWithEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", [new ScenarioCellValue(address, new NumberValue(42))]));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);

        var outcome = new DeleteScenarioCommand("Best Case").Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void ScenarioSummaryReportCommand_CreatesReportSheetAndUndoRemovesIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var price = new CellAddress(sheet.Id, 1, 1);
        var volume = new CellAddress(sheet.Id, 2, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(price, new NumberValue(12)),
                new ScenarioCellValue(volume, new NumberValue(100))
            ]));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Worst Case",
            [
                new ScenarioCellValue(price, new NumberValue(8)),
                new ScenarioCellValue(volume, new NumberValue(50))
            ]));

        var command = new ScenarioSummaryReportCommand();

        command.Apply(ctx).Success.Should().BeTrue();

        var report = workbook.Sheets.Should().Contain(s => s.Name == "Scenario Summary").Which;
        report.GetValue(1, 1).Should().Be(new TextValue("Scenario Summary"));
        report.GetValue(3, 1).Should().Be(new TextValue("Changing Cells"));
        report.GetValue(3, 2).Should().Be(new TextValue("Best Case"));
        report.GetValue(3, 3).Should().Be(new TextValue("Worst Case"));
        report.GetValue(4, 1).Should().Be(new TextValue("Sheet1!A1"));
        report.GetValue(4, 2).Should().Be(new NumberValue(12));
        report.GetValue(4, 3).Should().Be(new NumberValue(8));
        report.GetValue(5, 1).Should().Be(new TextValue("Sheet1!A2"));
        report.GetValue(5, 2).Should().Be(new NumberValue(100));
        report.GetValue(5, 3).Should().Be(new NumberValue(50));

        command.Revert(ctx);

        workbook.Sheets.Should().NotContain(s => s.Name == "Scenario Summary");
    }

    [Fact]
    public void ScenarioSummaryReportCommand_WithResultCells_ReportsValuesAfterEachScenarioAndRestoresWorkbook()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var price = new CellAddress(sheet.Id, 1, 1);
        var profit = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(price, new NumberValue(10));
        sheet.SetCell(profit, new NumberValue(200));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(price, new NumberValue(12)),
                new ScenarioCellValue(profit, new NumberValue(300))
            ]));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Worst Case",
            [
                new ScenarioCellValue(price, new NumberValue(8)),
                new ScenarioCellValue(profit, new NumberValue(120))
            ]));

        var command = new ScenarioSummaryReportCommand([profit]);

        command.Apply(ctx).Success.Should().BeTrue();

        var report = workbook.Sheets.Should().Contain(s => s.Name == "Scenario Summary").Which;
        report.GetValue(8, 1).Should().Be(new TextValue("Result Cells"));
        report.GetValue(8, 2).Should().Be(new TextValue("Best Case"));
        report.GetValue(8, 3).Should().Be(new TextValue("Worst Case"));
        report.GetValue(9, 1).Should().Be(new TextValue("Sheet1!A2"));
        report.GetValue(9, 2).Should().Be(new NumberValue(300));
        report.GetValue(9, 3).Should().Be(new NumberValue(120));
        sheet.GetValue(price).Should().Be(new NumberValue(10));
        sheet.GetValue(profit).Should().Be(new NumberValue(200));
    }

    [Fact]
    public void ScenarioSummaryReportCommand_WithFormulaResultCells_RecalculatesAfterEachScenarioAndRestore()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var price = new CellAddress(sheet.Id, 1, 1);
        var profit = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(price, new NumberValue(10));
        sheet.SetFormula(profit, "A1*2");
        sheet.GetCell(profit)!.Value = new NumberValue(20);
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", [new ScenarioCellValue(price, new NumberValue(12))]));
        workbook.Scenarios.Add(new WorkbookScenario("Worst Case", [new ScenarioCellValue(price, new NumberValue(8))]));

        var evaluator = new FormulaEvaluator();
        var command = new ScenarioSummaryReportCommand(
            [profit],
            (book, _) =>
            {
                var targetSheet = book.GetSheet(sheet.Id)!;
                targetSheet.GetCell(profit)!.Value = evaluator.Evaluate("=A1*2", targetSheet, book, profit);
            });

        command.Apply(ctx).Success.Should().BeTrue();

        var report = workbook.Sheets.Should().Contain(s => s.Name == "Scenario Summary").Which;
        report.GetValue(7, 1).Should().Be(new TextValue("Result Cells"));
        report.GetValue(8, 1).Should().Be(new TextValue("Sheet1!B1"));
        report.GetValue(8, 2).Should().Be(new NumberValue(24));
        report.GetValue(8, 3).Should().Be(new NumberValue(16));
        sheet.GetValue(price).Should().Be(new NumberValue(10));
        sheet.GetValue(profit).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void ScenarioSummaryReportCommand_WithResultCells_RestoresChangingCellsWhenRecalculateFails()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var price = new CellAddress(sheet.Id, 1, 1);
        var profit = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(price, new NumberValue(10));
        sheet.SetCell(profit, new NumberValue(20));
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", [new ScenarioCellValue(price, new NumberValue(12))]));
        var command = new ScenarioSummaryReportCommand(
            [profit],
            (_, _) => throw new InvalidOperationException("boom"));

        var act = () => command.Apply(ctx);

        act.Should().Throw<InvalidOperationException>();
        sheet.GetValue(price).Should().Be(new NumberValue(10));
        sheet.GetValue(profit).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void ScenarioSummaryReportCommand_WithResultCells_RejectsProtectedChangingCellsWithoutPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var price = new CellAddress(sheet.Id, 1, 1);
        var profit = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(price, new NumberValue(10));
        sheet.SetCell(profit, new NumberValue(20));
        sheet.IsProtected = true;
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", [new ScenarioCellValue(price, new NumberValue(12))]));

        var outcome = new ScenarioSummaryReportCommand([profit]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        workbook.Sheets.Should().NotContain(s => s.Name == "Scenario Summary");
        sheet.GetValue(price).Should().Be(new NumberValue(10));
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_ScenarioSummaryManySharedChangingCells_ReportsTimingAndAllocatedBytes()
    {
        const int scenarioCount = 60;
        const int changingCellCount = 300;
        var workbook = new Workbook("scenario");
        var sheet = workbook.AddSheet("Inputs");
        var ctx = new TestCommandContext(workbook);
        var changingCells = new List<CellAddress>(changingCellCount);

        for (uint row = 1; row <= changingCellCount; row++)
        {
            var address = new CellAddress(sheet.Id, row, 1);
            changingCells.Add(address);
            sheet.SetCell(address, new NumberValue(row));
        }

        for (var scenarioIndex = 0; scenarioIndex < scenarioCount; scenarioIndex++)
        {
            workbook.Scenarios.Add(new WorkbookScenario(
                $"Scenario {scenarioIndex + 1}",
                changingCells
                    .Select((address, cellIndex) => new ScenarioCellValue(
                        address,
                        new NumberValue((scenarioIndex + 1) * 1000 + cellIndex)))
                    .ToList()));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var command = new ScenarioSummaryReportCommand();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var outcome = command.Apply(ctx);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF SCENARIO_SUMMARY_SHARED_CHANGES " +
            $"scenarios={scenarioCount} changing_cells={changingCellCount} " +
            $"total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");

        outcome.Success.Should().BeTrue();
        var report = workbook.Sheets.Should().Contain(s => s.Name == "Scenario Summary").Which;
        report.GetValue(4, 1).Should().Be(new TextValue("Inputs!A1"));
        report.GetValue(4, 2).Should().Be(new NumberValue(1000));
        report.GetValue((uint)changingCellCount + 3, (uint)scenarioCount + 1)
            .Should()
            .Be(new NumberValue(scenarioCount * 1000 + changingCellCount - 1));
    }

}
