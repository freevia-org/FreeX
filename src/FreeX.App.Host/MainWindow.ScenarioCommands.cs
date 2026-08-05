using System.Linq;
using System.Windows;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

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

        if (!TryExecuteRepeatableCommand(
                () => new ApplyScenarioCommand(name),
                "Scenario Manager",
                out var outcome))
            return;

        // Scenario "Show" writes the changing cells' values directly (Sheet.SetCell), and Excel
        // always reflects a value change immediately regardless of calculation mode -- only
        // formula recalculation is deferred by Manual mode. RecalculateIfAutomatic above is a
        // no-op outside Automatic/AutomaticExceptDataTables mode, so it never bumps the
        // navigation-cache revision that SparklineValueCache/StatusBarStatsCache are keyed on;
        // force that invalidation here so sparklines and status-bar stats over the changed cells
        // refresh immediately instead of showing pre-scenario data until an unrelated edit
        // happens to bump the revision. Mirrors the Goal Seek fix in MainWindow.DataCommands.cs.
        InvalidateNavigationCachesIfManual();

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
                (_, changedCells) => _session.RecalculateChangedCellsAlways(changedCells)),
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

    /// <summary>
    /// Excel's Scenario Manager "Merge..." command: lets the user pick another saved workbook and
    /// pulls every scenario it contains into this workbook, matching each source scenario's
    /// changing cells onto this workbook's own sheets by sheet name. A scenario referencing a
    /// sheet name that doesn't exist here is skipped rather than guessed at.
    /// </summary>
    private async Task MergeScenariosFromFileAsync()
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FileDialogFilterBuilder.BuildOpenFilter(_fileAdapters),
            Title = "Merge Scenarios",
            CheckFileExists = true
        };
        if (openDialog.ShowDialog(this) != true)
            return;

        if (!WorkbookOpenTargetPlanner.TryCreateOpenTarget(_fileAdapters, openDialog.FileName, out var target, out _))
        {
            _messageService.ShowInfo("The selected file could not be opened for merging scenarios.", "Scenario Manager");
            return;
        }

        Workbook sourceWorkbook;
        try
        {
            var loader = new OpenWorkbookLoader(recalculateAllFormulas: _ => { });
            var result = await loader.LoadAsync(
                target!.Path,
                target.Adapter,
                FileFormatResolver.NormalizeExtension(target.Extension),
                target.Format,
                new Progress<OpenProgressUpdate>(_ => { }));
            sourceWorkbook = result.Workbook;
        }
        catch (Exception)
        {
            _messageService.ShowInfo("The selected file could not be opened for merging scenarios.", "Scenario Manager");
            return;
        }

        var mergeCandidates = RemapScenariosBySheetName(sourceWorkbook, _workbook);
        if (!TryExecuteCommand(new MergeScenarioCommand(mergeCandidates), "Scenario Manager", out var outcome))
        {
            ShowCommandError(outcome, "Scenario Manager");
            return;
        }

        UpdateViewport();
        RefreshStatusBar();
    }

    /// <summary>
    /// Remaps every source scenario's changing cells from <paramref name="source"/>'s sheets onto
    /// <paramref name="target"/>'s sheets of the same name (source and target workbooks each mint
    /// their own <see cref="SheetId"/>s, so a scenario's addresses can never be reused as-is). A
    /// scenario with any changing cell on a sheet name absent from the target is dropped entirely
    /// rather than partially merged.
    /// </summary>
    private static List<WorkbookScenario> RemapScenariosBySheetName(Workbook source, Workbook target)
    {
        var remapped = new List<WorkbookScenario>();
        foreach (var scenario in source.Scenarios)
        {
            var remappedCells = new List<ScenarioCellValue>(scenario.ChangingCells.Count);
            var allResolved = true;
            foreach (var cell in scenario.ChangingCells)
            {
                var sourceSheet = source.GetSheet(cell.Address.Sheet);
                var targetSheet = sourceSheet is null ? null : target.GetSheet(sourceSheet.Name);
                if (targetSheet is null)
                {
                    allResolved = false;
                    break;
                }

                remappedCells.Add(new ScenarioCellValue(
                    new CellAddress(targetSheet.Id, cell.Address.Row, cell.Address.Col),
                    cell.Value));
            }

            if (allResolved && remappedCells.Count > 0)
                remapped.Add(scenario with { ChangingCells = remappedCells });
        }

        return remapped;
    }
}
