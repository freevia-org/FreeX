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
                GetField<TextBox>(dialog, "_changingCellsBox").Text.Should().Be("B2:D3");
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

            var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent
            };
            scenarioList.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.DialogResult.Should().BeNull();
        });
    }
}
