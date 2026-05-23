using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class ScenarioManagerCommandTests
{
    [Fact]
    public void SaveScenarioCommand_AddsScenarioAndUndoRemovesIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
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
    public void SaveScenarioCommand_ReplacesExistingScenarioAndUndoRestoresIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
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
    public void SaveScenarioCommand_RejectsProtectedSheetWithoutEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new SimpleCtx(workbook);
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
        var ctx = new SimpleCtx(workbook);
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
        var ctx = new SimpleCtx(workbook);
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
        var ctx = new SimpleCtx(workbook);
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
    public void DeleteScenarioCommand_RemovesScenarioAndUndoRestoresIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
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
        var ctx = new SimpleCtx(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", [new ScenarioCellValue(address, new NumberValue(42))]));
        sheet.IsProtected = true;

        var outcome = new DeleteScenarioCommand("Best Case").Apply(ctx);

        outcome.Success.Should().BeFalse();
        workbook.Scenarios.Should().ContainSingle();
    }

    [Fact]
    public void ScenarioSummaryReportCommand_CreatesReportSheetAndUndoRemovesIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
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

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
