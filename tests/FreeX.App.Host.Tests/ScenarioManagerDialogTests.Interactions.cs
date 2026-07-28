using FluentAssertions;
using FreeX.Core.Model;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host.Tests;

public sealed partial class ScenarioManagerDialogTests
{
    [Fact]
    public void SelectingScenario_PopulatesEditFields()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            workbook.Scenarios.Add(new WorkbookScenario(
                "Best Case",
                [
                    new ScenarioCellValue(new CellAddress(sheet.Id, 2, 2), new NumberValue(10)),
                    new ScenarioCellValue(new CellAddress(sheet.Id, 3, 4), new NumberValue(20))
                ],
                "Use growth plan"));

            var dialog = new ScenarioManagerDialog(workbook, sheet.Id, name => name == sheet.Name ? sheet.Id : null);
            try
            {
                GetField<TextBox>(dialog, "_newNameBox").Text.Should().Be("Best Case");
                GetField<TextBox>(dialog, "_changingCellsBox").Text.Should().Be("B2:B2,D3:D3");
                GetField<TextBox>(dialog, "_commentBox").Text.Should().Be("Use growth plan");
                GetField<CheckBox>(dialog, "_lockedBox").IsChecked.Should().BeFalse();
                GetField<CheckBox>(dialog, "_hiddenBox").IsChecked.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ScenarioListDoubleClick_WithoutSelectionDoesNotHandleMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ScenarioManagerDialog(new Workbook("test"));
            var scenarioList = GetField<ListBox>(dialog, "_scenarioList");
            scenarioList.SelectedItem = null;

            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();
            scenarioList.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.DialogResult.Should().BeNull();
        });
    }

    [Fact]
    public void RangePickersRaiseRequestsAndApplyRangeSelectionUpdatesTargetBoxes()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            var requests = new List<ScenarioManagerRangeSelectionRequest>();
            var dialog = new ScenarioManagerDialog(
                workbook,
                sheet.Id,
                name => name == sheet.Name ? sheet.Id : null,
                requests.Add);
            try
            {
                var pickers = WpfTestTree.FindLogicalDescendants<Button>(dialog)
                    .Where(button => string.Equals(button.Content?.ToString(), "...", StringComparison.Ordinal))
                    .ToList();

                DialogSourceTestSupport.ClickButton(pickers[0]);
                DialogSourceTestSupport.ClickButton(pickers[1]);
                dialog.ApplyRangeSelection(ScenarioManagerRangeSelectionTarget.ChangingCells, "Sheet1!B2:C4");
                dialog.ApplyRangeSelection(ScenarioManagerRangeSelectionTarget.ResultCells, "Sheet1!D2:D4");

                requests.Should().Equal(
                    new ScenarioManagerRangeSelectionRequest(ScenarioManagerRangeSelectionTarget.ChangingCells, "", CollapseDialog: true),
                    new ScenarioManagerRangeSelectionRequest(ScenarioManagerRangeSelectionTarget.ResultCells, "", CollapseDialog: true));
                GetField<TextBox>(dialog, "_changingCellsBox").Text.Should().Be("Sheet1!B2:C4");
                GetField<TextBox>(dialog, "_resultCellsBox").Text.Should().Be("Sheet1!D2:D4");
                dialog.RangeSelectionRequest.Should().Be(requests[^1]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
