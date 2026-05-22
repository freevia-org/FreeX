using System;
using System.IO;
using System.Linq;
using System.Windows;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void GetDataBtn_Click(object sender, RoutedEventArgs e)
    {
        var adapters = _fileAdapters
            .Where(adapter => adapter.Extension is ".csv")
            .ToList();
        if (adapters.Count == 0)
        {
            MessageBox.Show("No import adapters are available.", "Get Data", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var filter = string.Join("|", adapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog() != true) return;

        var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var adapter = adapters.FirstOrDefault(a => a.Extension == ext);
        if (adapter is null) return;

        try
        {
            using var stream = System.IO.File.OpenRead(dialog.FileName);
            var imported = adapter.Load(stream);
            if (imported.Sheets.Count == 0) return;

            var destination = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
            if (!TryExecuteCommand(new ImportSheetCommand(_currentSheetId, destination, imported.Sheets[0]), "Get Data", out var outcome))
                return;

            RecalculateIfAutomatic(outcome.AffectedCells ?? []);
            SetActiveCell(destination);
            EnsureCellVisible(destination);
            UpdateViewport();
            RefreshStatusBar();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to import data:\n{ex.Message}", "Get Data", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void RefreshAllBtn_Click(object sender, RoutedEventArgs e) => CalcNowBtn_Click(sender, e);

    private void TextToColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = new TextToColumnsDialog(TextToColumnsDialog.BuildPreviewRows(sheet, range), range.Start) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Text to Columns",
                range,
                currentRange => CreateTextToColumnsCommand(currentRange, dialog.Result),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private IWorkbookCommand CreateTextToColumnsCommand(GridRange range, TextToColumnsDialogResult result)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return new EditCellsCommand(_currentSheetId, []);

        var edits = result.SplitMode == TextToColumnsSplitMode.FixedWidth
            ? TextToColumnsPlanner.BuildFixedWidthEdits(
                sheet,
                range,
                result.Destination ?? range.Start,
                result.FixedWidthBreakPositions ?? [],
                result.ColumnFormats)
            : TextToColumnsPlanner.BuildEdits(
                sheet,
                range,
                result.Destination ?? range.Start,
                result.Delimiters,
                result.TextQualifierChar,
                result.TreatConsecutiveDelimitersAsOne,
                result.ColumnFormats);

        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
    }

    private void RemoveDuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var columns = sheet is null
            ? RemoveDuplicatesDialog.BuildColumnChoices(range)
            : RemoveDuplicatesDialog.BuildColumnChoices(sheet, range);
        var genericColumns = sheet is null
            ? RemoveDuplicatesDialog.BuildColumnChoices(range)
            : RemoveDuplicatesDialog.BuildColumnChoices(sheet, range, hasHeaders: false);
        var dialog = new RemoveDuplicatesDialog(columns, genericColumns) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        RemoveDuplicateRowsCommand? command = null;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Remove Duplicates",
                range,
                currentRange =>
                {
                    command = new RemoveDuplicateRowsCommand(
                        _currentSheetId,
                        RemoveDuplicatesDialog.ExcludeHeaderRow(currentRange, dialog.Result.HasHeaders),
                        dialog.Result.SelectedColumnOffsets);
                    return command;
                }))
            return;

        MessageBox.Show($"Removed {command?.RemovedRowCount ?? 0} duplicate rows.", "Remove Duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
        UpdateViewport();
    }

    private void AdvancedFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        var defaultList = SheetGrid.SelectedRange is { } selected
            ? FormatWorkbookRange(selected)
            : "A1:C10";
        var dialog = new AdvancedFilterDialog(_currentSheetId, defaultList) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        var outcome = _commandBus.Execute(
            _workbook.Id,
            new AdvancedFilterCommand(dialog.Result.ListRange, dialog.Result.CriteriaRange, dialog.Result.CopyToCell, dialog.Result.UniqueRecordsOnly));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Advanced Filter");
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        if (dialog.Result.CopyToCell is { } destinationCell)
            SetActiveCell(destinationCell);
        UpdateViewport();
    }

    private bool TryParseAdvancedFilterRange(string input, out GridRange range)
        => AdvancedFilterInputParser.TryParseRange(
            _currentSheetId,
            input,
            sheetName => _workbook.Sheets.FirstOrDefault(item =>
                string.Equals(item.Name, sheetName, StringComparison.CurrentCultureIgnoreCase))?.Id,
            out range);

    private void ConsolidateBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = SheetGrid.SelectedRange;
        var defaultSource = selected?.ToString() ?? "A1:B2";
        var defaultDestination = selected?.Start.ToA1() ?? "A1";
        var dialog = new ConsolidateDialog(_currentSheetId, defaultSource, defaultDestination) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        var outcome = _commandBus.ExecuteRepeatable(
            _workbook.Id,
            () => new ConsolidateCommand(dialog.Result.SourceRanges, dialog.Result.DestinationCell));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Consolidate");
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        SetActiveCell(dialog.Result.DestinationCell);
        EnsureCellVisible(dialog.Result.DestinationCell);
        UpdateViewport();
    }

    // ── What-If Analysis ─────────────────────────────────────────────────────

    private void SubtotalBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range with a header row and data rows.", "Subtotal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = new SubtotalDialog(sheet is null ? null : SubtotalDialog.BuildColumnChoices(sheet, range)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        if (dialog.Result.Action == SubtotalDialogAction.RemoveAll)
        {
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Remove Subtotals",
                    range,
                    currentRange => new RemoveSubtotalRowsCommand(_currentSheetId, currentRange),
                    out var removeOutcome))
                return;

            RecalculateIfAutomatic(removeOutcome.AffectedCells ?? []);
            UpdateViewport();
            return;
        }

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Subtotal",
                range,
                currentRange =>
                {
                    var subtotalCommand = new SubtotalCommand(
                        _currentSheetId,
                        currentRange,
                        groupByColumnOffset: dialog.Result.GroupColumnOffset,
                        subtotalColumnOffsets: dialog.Result.SubtotalColumnOffsets,
                        functionNumber: dialog.Result.FunctionNumber,
                        pageBreakBetweenGroups: dialog.Result.PageBreakBetweenGroups,
                        summaryBelowData: dialog.Result.SummaryBelowData);
                    return dialog.Result.ReplaceCurrentSubtotals
                        ? new CompositeWorkbookCommand("Subtotal", [new RemoveSubtotalRowsCommand(_currentSheetId, currentRange), subtotalCommand])
                        : subtotalCommand;
                },
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void GoalSeekBtn_Click(object sender, RoutedEventArgs e)
    {
        var selectedCell = _selectionAnchor;
        var dlg = new GoalSeekDialog(_currentSheetId, selectedCell) { Owner = this };

        if (dlg.ShowDialog() != true)
            return;

        var setCell = dlg.SetCell!.Value;
        var changingCell = dlg.ChangingCell!.Value;
        var targetValue = dlg.TargetValue;

        var result = GoalSeekService.Seek(_workbook, _recalcEngine, setCell, targetValue, changingCell);

        var statusDialog = new GoalSeekStatusDialog(result, targetValue) { Owner = this };
        if (statusDialog.ShowDialog() == true && statusDialog.ApplyResult)
        {
            var cmd = new GoalSeekCommand(changingCell, result.FoundValue);
            if (TryExecuteCommand(cmd, "Goal Seek"))
                RecalculateIfAutomatic([changingCell]);
        }
    }

    // ── Review tab ────────────────────────────────────────────────────────────

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
                SaveScenarioFromSelection(dialog.NewScenarioName);
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

    private void SaveScenarioFromSelection(string? scenarioName)
    {
        if (SheetGrid.SelectedRange is not { } range)
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
        if (!TryExecuteCommand(new SaveScenarioCommand(name, changes), "Scenario Manager"))
            return;

        MessageBox.Show(ScenarioManagerPlanner.FormatSavedMessage(name, changes.Count),
            "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
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

        if (!TryExecuteCommand(new ApplyScenarioCommand(name), "Scenario Manager", out var outcome))
            return;

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

    private void ForecastSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a two-column range with headers and at least two data rows.",
                "Forecast Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ForecastSheetDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new ForecastSheetCommand(range, dialog.Result.Periods), "Forecast Sheet"))
            return;

        var forecastSheet = _workbook.Sheets.LastOrDefault();
        if (forecastSheet is not null)
        {
            _currentSheetId = forecastSheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        }

        RecalculateWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        RefreshStatusBar();
    }

    private void DataTableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select the data table range, including the formula row and input values.",
                "Data Table", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new DataTableDialog(_currentSheetId, range) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
            return;
        var formulaCell = dialog.Result.FormulaCell;
        Func<GridRange, IWorkbookCommand> createCommand;
        if (dialog.Result.Mode == DataTableMode.TwoVariable)
        {
            createCommand = currentRange => new TwoVariableDataTableCommand(currentRange, formulaCell, dialog.Result.RowInputCell!.Value, dialog.Result.ColumnInputCell!.Value);
        }
        else
        {
            var inputCell = dialog.Result.RowInputCell ?? dialog.Result.ColumnInputCell!.Value;
            createCommand = currentRange => new OneVariableDataTableCommand(currentRange, formulaCell, inputCell);
        }

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Data Table",
                range,
                createCommand,
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
        RefreshStatusBar();
    }
}
