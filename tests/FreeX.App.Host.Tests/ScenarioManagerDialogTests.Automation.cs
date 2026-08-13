using FluentAssertions;
using FreeX.Core.Model;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed partial class ScenarioManagerDialogTests
{
    [Fact]
    public void DialogControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            var dialog = new ScenarioManagerDialog(workbook, sheet.Id, name => name == sheet.Name ? sheet.Id : null);
            try
            {
                AssertAutomation(
                    GetField<ListBox>(dialog, "_scenarioList"),
                    "Scenarios",
                    "ScenarioManagerScenarioList",
                    "Select a scenario to show, edit, or delete.");
                AssertAutomation(
                    GetField<TextBox>(dialog, "_newNameBox"),
                    "Scenario name",
                    "ScenarioManagerScenarioNameBox",
                    "Enter the scenario name to add or edit.");
                AssertAutomation(
                    GetField<TextBox>(dialog, "_changingCellsBox"),
                    "Changing cells",
                    "ScenarioManagerChangingCellsBox",
                    "Enter the worksheet cells whose values change in the scenario.");
                AssertAutomation(
                    GetField<TextBox>(dialog, "_resultCellsBox"),
                    "Result cells",
                    "ScenarioManagerResultCellsBox",
                    "Enter optional result cells to include in a scenario summary.");
                AssertAutomation(
                    GetField<TextBox>(dialog, "_commentBox"),
                    "Comment",
                    "ScenarioManagerCommentBox",
                    "Enter an optional comment for the scenario.");
                AssertAutomation(
                    GetField<CheckBox>(dialog, "_lockedBox"),
                    "Prevent changes",
                    "ScenarioManagerPreventChangesCheckBox",
                    "Prevent changes to the scenario when the sheet is protected.");
                AssertAutomation(
                    GetField<CheckBox>(dialog, "_hiddenBox"),
                    "Hide",
                    "ScenarioManagerHideCheckBox",
                    "Hide the scenario when the sheet is protected.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogActionButtonsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            workbook.Scenarios.Add(new WorkbookScenario("Best Case", []));
            var dialog = new ScenarioManagerDialog(workbook, sheet.Id, name => name == sheet.Name ? sheet.Id : null);
            try
            {
                AssertAutomation(
                    GetField<Button>(dialog, "_addButton"),
                    UiText.Get("ScenarioManager_AddScenarioAutomationName"),
                    "ScenarioManagerAddButton",
                    UiText.Get("ScenarioManager_AddAScenarioUsingTheScenarioFields"));
                AssertAutomation(
                    GetField<Button>(dialog, "_editButton"),
                    UiText.Get("ScenarioManager_EditScenarioAutomationName"),
                    "ScenarioManagerEditButton",
                    UiText.Get("ScenarioManager_EditTheSelectedScenarioUsingTheScenarioFields"));
                AssertAutomation(
                    GetField<Button>(dialog, "_deleteButton"),
                    UiText.Get("ScenarioManager_DeleteScenarioAutomationName"),
                    "ScenarioManagerDeleteButton",
                    UiText.Get("ScenarioManager_DeleteTheSelectedScenario"));
                AssertAutomation(
                    GetField<Button>(dialog, "_showButton"),
                    UiText.Get("ScenarioManager_ShowScenarioAutomationName"),
                    "ScenarioManagerShowButton",
                    UiText.Get("ScenarioManager_ApplyTheSelectedScenarioToTheWorkbook"));
            }
            finally
            {
                dialog.Close();
            }
        });

        var source = ReadScenarioManagerDialogSource();
        source.Should().Contain("AutomationProperties.SetName(button, GetActionAutomationName(action));");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, FreeXAutomationIdCatalog.ScenarioManager.WpfActionButton(action));");
        source.Should().Contain("ScenarioManagerAction.Report => UiText.Get(\"ScenarioManager_ScenarioSummaryAutomationName\")");
        source.Should().Contain("ScenarioManagerAction.Report => UiText.Get(\"ScenarioManager_CreateAScenarioSummaryReport\")");
        source.Should().Contain("AutomationProperties.SetName(closeButton, UiText.Get(\"ScenarioManager_CloseAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(closeButton, FreeXAutomationIdCatalog.ScenarioManager.CloseButton);");
        source.Should().Contain("AutomationProperties.SetHelpText(closeButton, UiText.Get(\"ScenarioManager_CloseTheScenarioManagerDialog\"));");
    }

    [Fact]
    public void DialogVisibleSideButtonsMatchExcelScenarioManager()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            workbook.Scenarios.Add(new WorkbookScenario("Best Case", []));
            var dialog = new ScenarioManagerDialog(workbook, sheet.Id, name => name == sheet.Name ? sheet.Id : null);
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                var sideButtonIds = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Select(AutomationProperties.GetAutomationId)
                    .Where(id => id.StartsWith("ScenarioManager", StringComparison.Ordinal) &&
                        id != "ScenarioManagerCloseButton")
                    .ToList();

                sideButtonIds.Should().Equal(
                    "ScenarioManagerAddButton",
                    "ScenarioManagerEditButton",
                    "ScenarioManagerDeleteButton",
                    "ScenarioManagerShowButton",
                    "ScenarioManagerReportButton");
                sideButtonIds.Should().NotContain("ScenarioManagerListButton");
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
