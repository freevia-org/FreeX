using System.Linq;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void ScenariosBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScenarioManagerDialog(_workbook) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        switch (dialog.SelectedAction)
        {
            case ScenarioManagerAction.Add:
            case ScenarioManagerAction.Edit:
            case ScenarioManagerAction.Save:
                SaveScenarioFromDialog(dialog.NewScenarioName, dialog.ChangingCellsText, dialog.CommentText);
                break;
            case ScenarioManagerAction.Show:
                ShowScenarioByName(dialog.SelectedScenarioName);
                break;
            case ScenarioManagerAction.Delete:
                DeleteScenarioByName(dialog.SelectedScenarioName);
                break;
            case ScenarioManagerAction.List:
                ListScenarios();
                break;
            case ScenarioManagerAction.Report:
                CreateScenarioSummaryReport();
                break;
        }
    }

    private void SaveScenarioFromDialog(string? scenarioName, string? changingCellsText, string? comment)
    {
        GridRange range;
        if (TryParseScenarioChangingCells(changingCellsText, out var parsedRange))
        {
            range = parsedRange;
        }
        else if (SheetGrid.SelectedRange is { } selectedRange)
        {
            range = selectedRange;
        }
        else
        {
            MessageBox.Show("Select the changing cells for the scenario.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var name = string.IsNullOrWhiteSpace(scenarioName)
            ? (_workbook.Scenarios.Count == 0 ? "Scenario 1" : $"Scenario {_workbook.Scenarios.Count + 1}")
            : scenarioName;
        if (name is null)
            return;

        var changes = range.AllCells()
            .Select(address => new ScenarioCellValue(address, sheet.GetValue(address.Row, address.Col)))
            .ToList();
        if (!TryExecuteCommand(new SaveScenarioCommand(name, changes, comment), "Scenario Manager"))
            return;

        MessageBox.Show(ScenarioManagerPlanner.FormatSavedMessage(name, changes.Count),
            "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool TryParseScenarioChangingCells(string? changingCellsText, out GridRange range)
    {
        if (!string.IsNullOrWhiteSpace(changingCellsText) &&
            WorkbookRangeTextCodec.TryParse(_currentSheetId, changingCellsText, ResolveSheetIdByName, out range))
            return true;

        range = default;
        return false;
    }

    private void ShowScenarioByName(string? scenarioName)
    {
        if (_workbook.Scenarios.Count == 0)
        {
            MessageBox.Show("No scenarios are saved in this workbook.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = string.IsNullOrWhiteSpace(scenarioName) ? _workbook.Scenarios[0].Name : scenarioName;
        if (name is null)
            return;

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, () => new ApplyScenarioCommand(name));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Scenario Manager");
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        if (outcome.AffectedCells?.FirstOrDefault() is { } first)
        {
            SetActiveCell(first);
            EnsureCellVisible(first);
        }

        UpdateViewport();
        RefreshStatusBar();
    }

    private void DeleteScenarioByName(string? scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName))
            return;

        if (!TryExecuteCommand(new DeleteScenarioCommand(scenarioName), "Scenario Manager", out var outcome))
        {
            ShowCommandError(outcome, "Scenario Manager");
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ListScenarios()
    {
        if (_workbook.Scenarios.Count == 0)
        {
            MessageBox.Show("No scenarios are saved in this workbook.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = ScenarioManagerPlanner.FormatScenarioList(_workbook.Scenarios);
        MessageBox.Show(message, "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateScenarioSummaryReport()
    {
        if (!TryExecuteCommand(new ScenarioSummaryReportCommand(), "Scenario Manager"))
            return;

        var report = _workbook.Sheets.LastOrDefault();
        if (report is not null)
        {
            _currentSheetId = report.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        }

        UpdateViewport();
        RefreshSheetTabs();
        RefreshStatusBar();
    }
}
