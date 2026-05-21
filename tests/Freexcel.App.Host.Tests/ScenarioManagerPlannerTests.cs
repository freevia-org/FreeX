using FluentAssertions;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ScenarioManagerPlannerTests
{
    [Theory]
    [InlineData(0, "save")]
    [InlineData(2, "show")]
    public void GetDefaultAction_UsesSaveWhenNoScenariosExist(int scenarioCount, string expected)
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
    public void TryParseAction_RejectsUnknownActions(string input)
    {
        ScenarioManagerPlanner.TryParseAction(input, out var action).Should().BeFalse();
        action.Should().BeNull();
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
}
