using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ScenarioManagerDialogTests
{
    [Fact]
    public void BuildScenarioItems_ReturnsWorkbookScenarioNames()
    {
        var workbook = new Workbook("test");
        workbook.Scenarios.Add(new WorkbookScenario("Best Case", []));
        workbook.Scenarios.Add(new WorkbookScenario("Worst Case", []));

        var items = ScenarioManagerDialog.BuildScenarioItems(workbook);

        items.Select(item => item.Name).Should().Equal("Best Case", "Worst Case");
    }

    [Theory]
    [InlineData("save", ScenarioManagerAction.Save)]
    [InlineData("add", ScenarioManagerAction.Save)]
    [InlineData("show", ScenarioManagerAction.Show)]
    [InlineData("list", ScenarioManagerAction.List)]
    [InlineData("report", ScenarioManagerAction.Report)]
    public void TryParseAction_MapsLegacyPromptWords(string text, ScenarioManagerAction expected)
    {
        ScenarioManagerDialog.TryParseAction(text, out var action).Should().BeTrue();

        action.Should().Be(expected);
    }
}
