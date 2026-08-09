using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ScenarioManagerPlannerTests
{
    [Fact]
    public void ScenarioManagerPlanning_IsSingleSharedServiceImplementation()
    {
        var servicesPlannerPath = RepositoryFileLocator.Find("src", "FreeX.App.Services", "ScenarioManagerPlanner.cs");
        var servicesProjectRoot = Path.GetDirectoryName(servicesPlannerPath)
            ?? throw new DirectoryNotFoundException("Could not resolve FreeX.App.Services directory.");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(servicesPlannerPath).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ScenarioManagerPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared ScenarioManagerPlanner instead of carrying a renderer-local facade");
    }

    [Theory]
    [InlineData(0, ScenarioManagerAction.Save)]
    [InlineData(2, ScenarioManagerAction.Show)]
    public void GetDefaultAction_UsesSaveWhenNoScenariosExist(int scenarioCount, ScenarioManagerAction expected)
    {
        ScenarioManagerPlanner.GetDefaultAction(scenarioCount).Should().Be(expected);
    }

    [Theory]
    [InlineData("save", ScenarioManagerAction.Save)]
    [InlineData("add", ScenarioManagerAction.Add)]
    [InlineData("edit", ScenarioManagerAction.Edit)]
    [InlineData("show", ScenarioManagerAction.Show)]
    [InlineData("apply", ScenarioManagerAction.Show)]
    [InlineData("delete", ScenarioManagerAction.Delete)]
    [InlineData("remove", ScenarioManagerAction.Delete)]
    [InlineData("list", ScenarioManagerAction.List)]
    [InlineData("manager", ScenarioManagerAction.List)]
    [InlineData("report", ScenarioManagerAction.Report)]
    [InlineData("summary", ScenarioManagerAction.Report)]
    public void TryParseAction_MapsExcelScenarioAliases(string input, ScenarioManagerAction expected)
    {
        ScenarioManagerPlanner.TryParseAction(input, out var action).Should().BeTrue();
        action.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("scenario")]
    [InlineData(null)]
    public void TryParseAction_RejectsUnknownActions(string? input)
    {
        ScenarioManagerPlanner.TryParseAction(input, out var action).Should().BeFalse();
        action.Should().Be(default);
    }

    [Theory]
    [InlineData(0, "Scenario 1")]
    [InlineData(2, "Scenario 3")]
    public void GetDefaultScenarioName_UsesNextOrdinal(int scenarioCount, string expected)
    {
        ScenarioManagerPlanner.GetDefaultScenarioName(scenarioCount).Should().Be(expected);
    }

    [Fact]
    public void FormatSavedMessage_UsesTrimmedScenarioNameAndChangingCellCount()
    {
        ScenarioManagerPlanner.FormatSavedMessage(" Budget ", 3)
            .Should().Be("Scenario 'Budget' saved for 3 changing cell(s).");
    }

    [Fact]
    public void FormatScenarioList_FormatsEachScenarioOnSeparateLine()
    {
        var sheetId = SheetId.New();
        var scenarios = new[]
        {
            new WorkbookScenario("Base", [new ScenarioCellValue(new CellAddress(sheetId, 1, 1), new NumberValue(1))]),
            new WorkbookScenario("Upside", [
                new ScenarioCellValue(new CellAddress(sheetId, 1, 1), new NumberValue(2)),
                new ScenarioCellValue(new CellAddress(sheetId, 2, 1), new NumberValue(3))
            ])
        };

        ScenarioManagerPlanner.FormatScenarioList(scenarios)
            .Should().Be($"Base: 1 changing cell(s){Environment.NewLine}Upside: 2 changing cell(s)");
    }

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
    public void ScenarioManagerParityFixture_SeedsStableSelectedVisualScenario()
    {
        var workbook = CreateWorkbook(out var sheet);
        var firstCell = new CellAddress(sheet.Id, 2, 3);
        var secondCell = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(firstCell, new NumberValue(10));
        sheet.SetCell(secondCell, new NumberValue(20));

        ScenarioManagerParityFixture.Seed(workbook, sheet.Id);
        ScenarioManagerParityFixture.Seed(workbook, sheet.Id);

        var scenario = workbook.Scenarios.Should().ContainSingle(s => s.Name == ScenarioManagerParityFixture.ScenarioName).Which;
        scenario.Comment.Should().Be(ScenarioManagerParityFixture.ScenarioComment);
        scenario.Locked.Should().BeTrue();
        scenario.Hidden.Should().BeFalse();
        scenario.ChangingCells.Should().Equal(
            new ScenarioCellValue(firstCell, new NumberValue(10)),
            new ScenarioCellValue(secondCell, new NumberValue(20)));

        var range = ScenarioManagerParityFixture.ChangingCellsRange(sheet.Id);
        range.Start.Should().Be(firstCell);
        range.End.Should().Be(secondCell);
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
