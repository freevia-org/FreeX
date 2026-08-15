using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionScenarioManagerTests
{
    [Fact]
    public void SaveScenario_AddsScenarioMarksDirtyAndUndoRedoRestoresList()
    {
        var workbook = CreateWorkbook(out var sheet);
        var changingCell = CellAddress.Parse("A1", sheet.Id);
        var selectedCell = CellAddress.Parse("B2", sheet.Id);
        sheet.SetCell(changingCell, new NumberValue(10));
        var session = CreateSession(workbook);
        session.SelectCell(selectedCell);

        var result = session.SaveScenario(new ScenarioManagerSaveRequest(
            " Best Case ",
            [new ScenarioCellValue(changingCell, new NumberValue(42))],
            Comment: " Optimistic assumptions ",
            Hidden: true,
            Locked: true));

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(changingCell);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));
        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Best Case");
        workbook.Scenarios[0].Comment.Should().Be("Optimistic assumptions");
        workbook.Scenarios[0].Hidden.Should().BeTrue();
        workbook.Scenarios[0].Locked.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.Scenarios.Should().BeEmpty();
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        workbook.Scenarios.Should().ContainSingle()
            .Which.Name.Should().Be("Best Case");
    }

    [Fact]
    public void SaveScenario_ReplacesScenarioMetadataAndUndoRestoresPriorScenario()
    {
        var workbook = CreateWorkbook(out var sheet);
        var changingCell = CellAddress.Parse("A1", sheet.Id);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Baseline",
            [new ScenarioCellValue(changingCell, new NumberValue(10))],
            "Original",
            Hidden: true,
            Locked: true));
        var session = CreateSession(workbook);

        var result = session.SaveScenario(new ScenarioManagerSaveRequest(
            "Upside",
            [new ScenarioCellValue(changingCell, new NumberValue(25))],
            ReplaceScenarioName: "Baseline",
            Comment: "Updated",
            Hidden: false,
            Locked: false));

        result.Success.Should().BeTrue();
        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Upside");
        workbook.Scenarios[0].Comment.Should().Be("Updated");
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(25));
        workbook.Scenarios[0].Hidden.Should().BeFalse();
        workbook.Scenarios[0].Locked.Should().BeFalse();

        session.UndoLastEdit().Success.Should().BeTrue();

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Baseline");
        workbook.Scenarios[0].Comment.Should().Be("Original");
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(10));
        workbook.Scenarios[0].Hidden.Should().BeTrue();
        workbook.Scenarios[0].Locked.Should().BeTrue();
    }

    [Fact]
    public void ShowScenario_AppliesValuesRecalculatesDependentsSelectsFirstChangingCellAndUndoRedoRestores()
    {
        var workbook = CreateWorkbook(out var sheet);
        var price = CellAddress.Parse("A1", sheet.Id);
        var profit = CellAddress.Parse("B1", sheet.Id);
        var selectedCell = CellAddress.Parse("C3", sheet.Id);
        sheet.SetCell(price, new NumberValue(10));
        sheet.SetFormula(profit, "A1*2");
        sheet.GetCell(profit)!.Value = new NumberValue(20);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(price, new NumberValue(42))]));
        var session = CreateSession(workbook);
        session.SelectCell(selectedCell);

        var result = session.ShowScenario("best case");

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(price);
        sheet.GetValue(price).Should().Be(new NumberValue(42));
        sheet.GetValue(profit).Should().Be(new NumberValue(84));
        session.IsDirty.Should().BeTrue();
        session.ActiveCell.Should().Be(price);
        session.SelectedRange.Should().Be(new GridRange(price, price));
        session.CanRepeatLastAction.Should().BeTrue();

        sheet.SetCell(price, new NumberValue(7));
        session.SelectCell(selectedCell);
        var repeat = session.RepeatLastAction();

        repeat.Success.Should().BeTrue();
        sheet.GetValue(price).Should().Be(new NumberValue(42));
        sheet.GetValue(profit).Should().Be(new NumberValue(84));
        session.ActiveCell.Should().Be(selectedCell);

        session.UndoLastEdit().Success.Should().BeTrue();

        sheet.GetValue(price).Should().Be(new NumberValue(7));
        session.CanRedo.Should().BeTrue();

        session.RedoLastEdit().Success.Should().BeTrue();

        sheet.GetValue(price).Should().Be(new NumberValue(42));
        sheet.GetValue(profit).Should().Be(new NumberValue(84));
    }

    [Fact]
    public void MergeScenarios_AppendsUniqueScenariosPreservesSelectionAndSupportsUndoRedo()
    {
        var workbook = CreateWorkbook(out var sheet);
        var changingCell = CellAddress.Parse("A1", sheet.Id);
        var selectedCell = CellAddress.Parse("B2", sheet.Id);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Baseline",
            [new ScenarioCellValue(changingCell, new NumberValue(1))]));
        var session = CreateSession(workbook);
        session.SelectCell(selectedCell);
        var source = new[]
        {
            new WorkbookScenario(
                "Baseline",
                [new ScenarioCellValue(changingCell, new NumberValue(2))],
                "Imported"),
            new WorkbookScenario(
                "Upside",
                [new ScenarioCellValue(changingCell, new NumberValue(3))])
        };

        var result = session.MergeScenarios(source);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().OnlyContain(address => address == changingCell);
        session.IsDirty.Should().BeTrue();
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));
        workbook.Scenarios.Select(scenario => scenario.Name)
            .Should().Equal("Baseline", "Baseline (2)", "Upside");
        workbook.Scenarios[1].Comment.Should().Be("Imported");

        session.UndoLastEdit().Success.Should().BeTrue();
        workbook.Scenarios.Select(scenario => scenario.Name).Should().Equal("Baseline");

        session.RedoLastEdit().Success.Should().BeTrue();
        workbook.Scenarios.Select(scenario => scenario.Name)
            .Should().Equal("Baseline", "Baseline (2)", "Upside");
    }

    [Fact]
    public void DeleteScenario_RemovesScenarioPreservesSelectionAndUndoRedoRestoresOrder()
    {
        var workbook = CreateWorkbook(out var sheet);
        var changingCell = CellAddress.Parse("A1", sheet.Id);
        var selectedCell = CellAddress.Parse("B2", sheet.Id);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Base",
            [new ScenarioCellValue(changingCell, new NumberValue(1))]));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(changingCell, new NumberValue(42))]));
        var session = CreateSession(workbook);
        session.SelectCell(selectedCell);

        var result = session.DeleteScenario("best case");

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));
        workbook.Scenarios.Select(scenario => scenario.Name).Should().Equal("Base");

        session.UndoLastEdit().Success.Should().BeTrue();

        workbook.Scenarios.Select(scenario => scenario.Name).Should().Equal("Base", "Best Case");
        session.CanRedo.Should().BeTrue();

        session.RedoLastEdit().Success.Should().BeTrue();

        workbook.Scenarios.Select(scenario => scenario.Name).Should().Equal("Base");
    }

    [Fact]
    public void CreateScenarioSummaryReport_CreatesReportSelectsA1RecalculatesFormulaResultsAndUndoRemovesReport()
    {
        var workbook = CreateWorkbook(out var sheet);
        var price = CellAddress.Parse("A1", sheet.Id);
        var profit = CellAddress.Parse("B1", sheet.Id);
        sheet.SetCell(price, new NumberValue(10));
        sheet.SetFormula(profit, "A1*2");
        sheet.GetCell(profit)!.Value = new NumberValue(20);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(price, new NumberValue(12))]));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Worst Case",
            [new ScenarioCellValue(price, new NumberValue(8))]));
        var session = CreateSession(workbook);

        var result = session.CreateScenarioSummaryReport([profit]);

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveSheet.Name.Should().Be("Scenario Summary");
        session.ActiveCell.Should().Be(new CellAddress(session.ActiveSheet.Id, 1, 1));
        session.SelectedRange.Should().Be(new GridRange(session.ActiveCell, session.ActiveCell));
        sheet.GetValue(price).Should().Be(new NumberValue(10));
        sheet.GetValue(profit).Should().Be(new NumberValue(20));

        var report = workbook.Sheets.Should().Contain(s => s.Name == "Scenario Summary").Which;
        report.GetValue(1, 1).Should().Be(new TextValue("Scenario Summary"));
        report.GetValue(3, 1).Should().Be(new TextValue("Changing Cells"));
        report.GetValue(4, 1).Should().Be(new TextValue("Sheet1!A1"));
        report.GetValue(4, 2).Should().Be(new NumberValue(12));
        report.GetValue(4, 3).Should().Be(new NumberValue(8));
        report.GetValue(7, 1).Should().Be(new TextValue("Result Cells"));
        report.GetValue(8, 1).Should().Be(new TextValue("Sheet1!B1"));
        report.GetValue(8, 2).Should().Be(new NumberValue(24));
        report.GetValue(8, 3).Should().Be(new NumberValue(16));

        session.UndoLastEdit().Success.Should().BeTrue();

        workbook.Sheets.Should().NotContain(s => s.Name == "Scenario Summary");
    }

    [Fact]
    public void FailedPlannerAndInvalidActionReturnFailureWithoutSelectionOrDirtyChanges()
    {
        var workbook = CreateWorkbook(out var sheet);
        var selectedCell = CellAddress.Parse("B2", sheet.Id);
        var changingCell = CellAddress.Parse("A1", sheet.Id);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(changingCell, new NumberValue(42))]));
        var session = CreateSession(workbook);
        session.SelectCell(selectedCell);
        var originalSelection = session.SelectedRange;

        var missing = session.ExecuteScenarioManagerShowPlan(
            ScenarioManagerPlanner.CreateShowPlan(workbook, "Missing"));
        var invalidAction = session.ExecuteScenarioManagerDeletePlan(
            ScenarioManagerPlanner.CreateShowPlan(workbook, "Best Case"));

        missing.Success.Should().BeFalse();
        missing.ErrorMessage.Should().Be("Scenario 'Missing' was not found.");
        invalidAction.Success.Should().BeFalse();
        invalidAction.ErrorMessage.Should().Be("Scenario Manager plan operation does not match the requested action.");
        session.SelectedRange.Should().Be(originalSelection);
        session.ActiveCell.Should().Be(selectedCell);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.GetValue(changingCell).Should().Be(BlankValue.Instance);
        workbook.Scenarios.Should().ContainSingle()
            .Which.Name.Should().Be("Best Case");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(
                workbook,
                "Book.fxl",
                "Opened .fxl.",
                IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Budget");
        sheet = workbook.AddSheet("Sheet1");
        return workbook;
    }
}
