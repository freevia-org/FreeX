using System.Linq;
using System.Windows;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void ScenariosBtn_Click(object sender, RoutedEventArgs e)
    {
        ScenarioManagerDialog? dialog = null;
        dialog = new ScenarioManagerDialog(
            _workbook,
            _currentSheetId,
            ResolveSheetIdByName,
            request => ApplyScenarioManagerRangeSelection(dialog, request)) { Owner = this };
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

    private void ApplyScenarioManagerRangeSelection(
        ScenarioManagerDialog? dialog,
        ScenarioManagerRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(request.Target, FormatWorkbookRange(selectedRange)));
    }

    private void SaveScenarioFromDialog(
        string? scenarioName,
        string? changingCellsText,
        string? comment,
        bool hidden,
        bool locked,
        string? replaceScenarioName = null)
    {
        IReadOnlyList<GridRange> ranges;
        if (TryParseScenarioChangingCells(changingCellsText, out var parsedRanges))
        {
            ranges = parsedRanges;
        }
        else if (SheetGrid.SelectedRange is { } selectedRange)
        {
            ranges = [selectedRange];
        }
        else
        {
            _messageService.ShowInfo(UiText.Get("MainWindowMessage_ScenarioSelectChangingCells"), UiText.Get("MainWindowMessage_ScenarioManagerTitle"));
            return;
        }

        var name = string.IsNullOrWhiteSpace(scenarioName)
            ? (_workbook.Scenarios.Count == 0 ? "Scenario 1" : $"Scenario {_workbook.Scenarios.Count + 1}")
            : scenarioName;
        if (name is null)
            return;

        var changes = new List<ScenarioCellValue>();
        var seen = new HashSet<CellAddress>();
        foreach (var range in ranges)
        {
            var sheet = _workbook.GetSheet(range.Start.Sheet);
            if (sheet is null)
                continue;

            foreach (var address in range.AllCells())
            {
                if (seen.Add(address))
                    changes.Add(new ScenarioCellValue(address, sheet.GetValue(address.Row, address.Col)));
            }
        }

        if (!TryExecuteCommand(new SaveScenarioCommand(name, changes, comment, hidden, locked, replaceScenarioName), "Scenario Manager"))
            return;

        _messageService.ShowInfo(ScenarioManagerPlanner.FormatSavedMessage(name, changes.Count), "Scenario Manager");
    }

    private bool TryParseScenarioChangingCells(string? changingCellsText, out IReadOnlyList<GridRange> ranges)
    {
        if (!string.IsNullOrWhiteSpace(changingCellsText) &&
            WorkbookRangeTextCodec.TryParseMany(_currentSheetId, changingCellsText, ResolveSheetIdByName, out ranges))
            return true;

        ranges = [];
        return false;
    }

    private void ShowScenarioByName(string? scenarioName)
    {
        if (_workbook.Scenarios.Count == 0)
        {
            _messageService.ShowInfo(UiText.Get("MainWindowMessage_ScenarioNoScenarios"), UiText.Get("MainWindowMessage_ScenarioManagerTitle"));
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
        var refreshedSelectionUi = false;
        CellAddress? first = null;
        if (outcome.AffectedCells is not null)
        {
            foreach (var cell in outcome.AffectedCells)
            {
                first = cell;
                break;
            }
        }

        if (first is { } firstCell)
        {
            SetActiveCell(firstCell);
            EnsureCellVisible(firstCell);
            refreshedSelectionUi = true;
        }

        UpdateViewport();
        if (!refreshedSelectionUi)
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
            _messageService.ShowInfo(UiText.Get("MainWindowMessage_ScenarioNoScenarios"), UiText.Get("MainWindowMessage_ScenarioManagerTitle"));
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
                // Always recalculate here, independent of the workbook's calculation mode: the
                // summary report's whole purpose is to show each scenario's distinct computed
                // result, so Manual mode must not leave every scenario column reading the same
                // stale pre-report value (Excel's own Scenario Summary always computes fresh
                // per-scenario results).
                (workbook, changedCells) => _recalcEngine.Recalculate(workbook, changedCells)),
            "Scenario Manager"))
            return;

        var report = _workbook.Sheets.LastOrDefault();
        var refreshedSelectionUi = false;
        if (report is not null)
        {
            _currentSheetId = report.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
            refreshedSelectionUi = true;
        }

        UpdateViewport();
        RefreshSheetTabs();
        if (!refreshedSelectionUi)
            RefreshStatusBar();
    }
}
