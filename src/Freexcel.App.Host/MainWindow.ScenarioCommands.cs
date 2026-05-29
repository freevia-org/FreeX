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
        var dialog = new ScenarioManagerDialog(_workbook, _currentSheetId, ResolveSheetIdByName) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        switch (dialog.SelectedAction)
        {
            case ScenarioManagerAction.Add:
            case ScenarioManagerAction.Edit:
            case ScenarioManagerAction.Save:
                SaveScenarioFromDialog(
                    dialog.NewScenarioName,
                    dialog.ChangingCellsText,
                    dialog.CommentText,
                    dialog.ScenarioHidden,
                    dialog.ScenarioLocked,
                    dialog.SelectedAction == ScenarioManagerAction.Edit ? dialog.SelectedScenarioName : null);
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
                CreateScenarioSummaryReport(dialog.ResultCellsText);
                break;
        }
    }

    private void SaveScenarioFromDialog(
        string? scenarioName,
        string? changingCellsText,
        string? comment,
        bool hidden,
        bool locked,
        string? replaceScenarioName = null)
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
            _messageService.ShowInfo("Select the changing cells for the scenario.", "Scenario Manager");
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
        if (!TryExecuteCommand(new SaveScenarioCommand(name, changes, comment, hidden, locked, replaceScenarioName), "Scenario Manager"))
            return;

        _messageService.ShowInfo(ScenarioManagerPlanner.FormatSavedMessage(name, changes.Count), "Scenario Manager");
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
            _messageService.ShowInfo("No scenarios are saved in this workbook.", "Scenario Manager");
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
            _messageService.ShowInfo("No scenarios are saved in this workbook.", "Scenario Manager");
            return;
        }

        var message = ScenarioManagerPlanner.FormatScenarioList(_workbook.Scenarios);
        _messageService.ShowInfo(message, "Scenario Manager");
    }

    private IReadOnlyList<CellAddress> ParseScenarioResultCells(string? resultCellsText)
    {
        if (!string.IsNullOrWhiteSpace(resultCellsText) &&
            WorkbookRangeTextCodec.TryParseMany(_currentSheetId, resultCellsText, ResolveSheetIdByName, out var ranges))
            return ranges.SelectMany(range => range.AllCells()).Distinct().ToList();

        return [];
    }

    private void CreateScenarioSummaryReport(string? resultCellsText = null)
    {
        if (!TryExecuteCommand(
            new ScenarioSummaryReportCommand(
                ParseScenarioResultCells(resultCellsText),
                (workbook, changedCells) =>
                {
                    if (workbook.CalculationMode == WorkbookCalculationMode.Automatic)
                        _recalcEngine.Recalculate(workbook, changedCells);
                }),
            "Scenario Manager"))
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
