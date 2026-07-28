using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ScenarioManagerDialogTests
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

    [Fact]
    public void BuildScenarioItems_IncludesChangingCellsAndCommentForEditing()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var first = new CellAddress(sheet.Id, 2, 2);
        var second = new CellAddress(sheet.Id, 4, 3);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(first, new NumberValue(10)),
                new ScenarioCellValue(second, new NumberValue(20))
            ],
            "Revenue lift",
            Hidden: true,
            Locked: true));

        var item = ScenarioManagerDialog.BuildScenarioItems(workbook).Single();

        item.Name.Should().Be("Best Case");
        item.ChangingCellsText.Should().Be("B2:B2,C4:C4");
        item.Comment.Should().Be("Revenue lift");
        item.Hidden.Should().BeTrue();
        item.Locked.Should().BeTrue();
    }
}
