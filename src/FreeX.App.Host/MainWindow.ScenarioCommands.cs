using System.Linq;
using System.Windows;
using FreeX.App.Presentation.ScenarioManager;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FileDialogFilterBuilder = Free.Shared.IO.FileDialogFilterBuilder;
using FileFormatDialogDescriptorAdapter = Free.Shared.IO.FileFormatDialogDescriptorAdapter;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async void ScenariosBtn_Click(object sender, RoutedEventArgs e)
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
            case ScenarioManagerAction.Merge:
                // ScenarioManagerDialog has no Merge button yet (that UI entry point is a
                // separate, later addition), so this case is unreachable from the shipped dialog
                // today -- but it must not silently no-op if a future trigger (or a direct caller)
                // ever selects Merge, so it's wired to the real merge flow now.
                await MergeScenariosFromFileAsync();
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
            ? ScenarioManagerPlanner.GetDefaultScenarioName(_workbook.Scenarios.Select(scenario => scenario.Name))
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

        var scenarioManagerTitle = ScenarioManagerDialogPlanner.Title.Resolve(UiText.Get, UiText.Format);
        var request = new ScenarioManagerSaveRequest(
            name,
            changes,
            replaceScenarioName,
            comment,
            hidden,
            locked);
        if (!TryExecuteWorksheetLayout(() => _session.SaveScenario(request), scenarioManagerTitle))
            return;

        _messageService.ShowInfo(ScenarioManagerPlanner.FormatSavedMessage(name, changes.Count), scenarioManagerTitle);
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

        if (!TryExecuteWorksheetLayout(
                () => _session.ShowScenario(name),
                ScenarioManagerDialogPlanner.Title.Resolve(UiText.Get, UiText.Format)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void DeleteScenarioByName(string? scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName))
            return;

        var scenarioManagerTitle = ScenarioManagerDialogPlanner.Title.Resolve(UiText.Get, UiText.Format);
        if (!TryExecuteWorksheetLayout(() => _session.DeleteScenario(scenarioName), scenarioManagerTitle))
            return;

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
        _messageService.ShowInfo(
            message,
            ScenarioManagerDialogPlanner.Title.Resolve(UiText.Get, UiText.Format));
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
        if (!TryExecuteWorksheetLayout(
                () => _session.CreateScenarioSummaryReport(ParseScenarioResultCells(resultCellsText)),
                ScenarioManagerDialogPlanner.Title.Resolve(UiText.Get, UiText.Format)))
            return;

        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_session.ActiveSheet.Id);
        _sheetGroupAnchor = _session.ActiveSheet.Id;

        UpdateViewport();
        RefreshSheetTabs();
        RefreshStatusBar();
    }

    /// <summary>
    /// Excel's Scenario Manager "Merge..." command: lets the user pick another saved workbook and
    /// pulls every scenario it contains into this workbook, matching each source scenario's
    /// changing cells onto this workbook's own sheets by sheet name. A scenario referencing a
    /// sheet name that doesn't exist here is skipped rather than guessed at.
    /// </summary>
    private async Task MergeScenariosFromFileAsync()
    {
        var scenarioManagerTitle = ScenarioManagerDialogPlanner.Title.Resolve(UiText.Get, UiText.Format);
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FileDialogFilterBuilder.BuildOpenFilter(
                FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(
                    _fileAdapters.SelectMany(adapter => adapter.Formats))),
            Title = ScenarioManagerDialogPlanner.MergeDialogTitle.Resolve(UiText.Get, UiText.Format),
            CheckFileExists = true
        };
        if (openDialog.ShowDialog(this) != true)
            return;

        if (!WorkbookOpenTargetPlanner.TryCreateOpenTarget(_fileAdapters, openDialog.FileName, out var target, out _))
        {
            _messageService.ShowInfo(
                ScenarioManagerDialogPlanner.MergeOpenFailedMessage.Resolve(UiText.Get, UiText.Format),
                scenarioManagerTitle);
            return;
        }

        Workbook sourceWorkbook;
        try
        {
            var loader = new WorkbookOpenService(recalculateAllFormulas: _ => { });
            var result = await loader.LoadAsync(
                target!.Path,
                target.Adapter,
                FileFormatResolver.NormalizeExtension(target.Extension),
                target.Format,
                new Progress<WorkbookOpenProgressUpdate>(_ => { }));
            sourceWorkbook = result.Workbook;
        }
        catch (Exception)
        {
            _messageService.ShowInfo(
                ScenarioManagerDialogPlanner.MergeOpenFailedMessage.Resolve(UiText.Get, UiText.Format),
                scenarioManagerTitle);
            return;
        }

        var mergeCandidates = ScenarioManagerPlanner.RemapScenariosBySheetName(sourceWorkbook, _workbook);
        if (!TryExecuteWorksheetLayout(() => _session.MergeScenarios(mergeCandidates), scenarioManagerTitle))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }
}
