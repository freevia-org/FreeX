using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private ConsolidateRangePickerSession? _consolidateRangePickerSession;

    private async void GetDataBtn_Click(object sender, RoutedEventArgs e)
    {
        string[] dataExtensions = [".csv", ".txt", ".tsv", ".tab", ".xml"];
        var adapters = _fileAdapters
            .Where(adapter => dataExtensions.Contains(adapter.Extension, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (adapters.Count == 0)
        {
            RecordDiagnosticEvent("import_failed", new Dictionary<string, string?>
            {
                ["reason"] = "no_adapter"
            });
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoImportAdapters"),
                UiText.Get("MainWindowMessage_GetDataTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var filter = FileDialogFilterBuilder.BuildOpenFilter(adapters);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var adapter = FileDialogFilterBuilder.FindOpenAdapter(adapters, ext, out var format);
        if (adapter is null)
        {
            RecordDiagnosticEvent("import_failed", BuildImportDiagnosticProperties(ext, null, "unsupported_extension"));
            return;
        }

        try
        {
            var importPath = dialog.FileName;
            var imported = await Task.Run(() =>
            {
                using var stream = System.IO.File.OpenRead(importPath);
                return adapter.Load(stream);
            });

            if (imported.Sheets.Count == 0)
            {
                RecordDiagnosticEvent("import_failed", BuildImportDiagnosticProperties(ext, format?.FormatName ?? adapter.FormatName, "empty_workbook", imported.Sheets.Count));
                return;
            }

            var destination = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
            if (!TryExecuteCommand(new ImportSheetCommand(_currentSheetId, destination, imported.Sheets[0]), "Get Data", out var outcome))
            {
                RecordDiagnosticEvent("import_failed", BuildImportDiagnosticProperties(ext, format?.FormatName ?? adapter.FormatName, "command_failed", imported.Sheets.Count));
                return;
            }

            RecalculateIfAutomatic(outcome.AffectedCells ?? []);
            SetActiveCell(destination);
            EnsureCellVisible(destination);
            UpdateViewport();
            RefreshStatusBar();
            RecordDiagnosticEvent("import_completed", BuildImportDiagnosticProperties(ext, format?.FormatName ?? adapter.FormatName, null, imported.Sheets.Count));
        }
        catch (Exception ex)
        {
            var diagnostic = ImportFailureDiagnosticFactory.FromException(ext, ex);
            RecordDiagnosticEvent(
                "import_failed",
                BuildImportDiagnosticProperties(
                    ext,
                    format?.FormatName ?? adapter.FormatName,
                    diagnostic.Reason,
                    errorDetail: diagnostic.Detail));
            ShowOwnedMessage(diagnostic.UserMessage, UiText.Get("MainWindowMessage_GetDataTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Dictionary<string, string?> BuildImportDiagnosticProperties(
        string extension,
        string? format,
        string? reason = null,
        int? worksheetCount = null,
        string? errorDetail = null)
    {
        var properties = new Dictionary<string, string?>
        {
            ["extension"] = extension,
            ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(extension)
        };
        if (!string.IsNullOrWhiteSpace(format))
            properties["format"] = format;
        if (worksheetCount is not null)
            properties["worksheetCount"] = worksheetCount.Value.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(reason))
            properties["reason"] = reason;
        if (!string.IsNullOrWhiteSpace(errorDetail))
            properties["errorDetail"] = errorDetail;
        return properties;
    }
    private void RefreshAllBtn_Click(object sender, RoutedEventArgs e) => CalcNowBtn_Click(sender, e);

    private void TextToColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TextToColumnsDialog.CanConvertRange(range))
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_TextToColumnsSingleColumnRequired"),
                UiText.Get("MainWindowMessage_TextToColumnsTitle"));
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        TextToColumnsDialog? dialog = null;
        dialog = new TextToColumnsDialog(
            TextToColumnsDialog.BuildPreviewRows(sheet, range),
            range.Start,
            request => ApplyTextToColumnsRangeSelection(dialog, request)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        var targetSheetIds = CurrentGroupedEditSheetIds();
        var currentRange = SheetGrid.SelectedRange ?? range;
        if (TextToColumnsCommandPlanner.FindOverwriteTargets(_workbook, targetSheetIds, currentRange, dialog.Result).Count > 0 &&
            !_messageService.AskYesNo(
                UiText.Get("MainWindowMessage_TextToColumnsReplaceDataPrompt"),
                UiText.Get("MainWindowMessage_TextToColumnsTitle")))
        {
            return;
        }

        var outcome = _commandBus.ExecuteRepeatable(
            _workbook.Id,
            () => CreateTextToColumnsCommand(CurrentGroupedEditSheetIds(), currentRange, dialog.Result));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Text to Columns");
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void ApplyTextToColumnsRangeSelection(
        TextToColumnsDialog? dialog,
        TextToColumnsRangeSelectionRequest request)
    {
        if (dialog is null || SheetGrid.SelectedRange is not { } selectedRange)
            return;

        if (request.CollapseDialog)
            dialog.Hide();

        try
        {
            dialog.ApplyRangeSelection(selectedRange.Start);
        }
        finally
        {
            if (request.CollapseDialog)
            {
                dialog.Show();
                dialog.Activate();
            }
        }
    }

    private IWorkbookCommand CreateTextToColumnsCommand(
        IReadOnlyList<SheetId> targetSheetIds,
        GridRange range,
        TextToColumnsDialogResult result) =>
        TextToColumnsCommandPlanner.CreateCommand(_workbook, targetSheetIds, _currentSheetId, range, result);

    private void RemoveDuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        ShowRemoveDuplicatesDialog(range);
    }

    private void ShowRemoveDuplicatesDialog(GridRange range)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var columns = sheet is null
            ? RemoveDuplicatesDialog.BuildColumnChoices(range)
            : RemoveDuplicatesDialog.BuildColumnChoices(sheet, range);
        var genericColumns = sheet is null
            ? RemoveDuplicatesDialog.BuildColumnChoices(range)
            : RemoveDuplicatesDialog.BuildColumnChoices(sheet, range, hasHeaders: false);
        var hasHeaders = sheet is not null && RemoveDuplicatesDialog.GuessHasHeaders(sheet, range);
        var dialog = new RemoveDuplicatesDialog(columns, genericColumns, hasHeaders) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        RemoveDuplicateRowsCommand? activeSheetCommand = null;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Remove Duplicates",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var activeRange = RemoveDuplicatesDialog.ExcludeHeaderRow(currentRange, dialog.Result.HasHeaders);
                    var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(
                        activeRange,
                        sheetId);
                    var command = new RemoveDuplicateRowsCommand(
                        sheetId,
                        sheetRange,
                        dialog.Result.SelectedColumnOffsets);
                    if (sheetId == _currentSheetId)
                        activeSheetCommand = command;
                    return command;
                }))
            return;

        ShowOwnedMessage(
            UiText.Format("MainWindowMessage_RemoveDuplicatesRemovedRows", activeSheetCommand?.RemovedRowCount ?? 0),
            UiText.Get("MainWindowMessage_RemoveDuplicatesTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        UpdateViewport();
    }

    private void AdvancedFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        var defaultList = SheetGrid.SelectedRange is { } selected
            ? FormatWorkbookRange(selected)
            : "A1:C10";
        AdvancedFilterDialog? dialog = null;
        dialog = new AdvancedFilterDialog(
            _currentSheetId,
            defaultList,
            ResolveSheetIdByName,
            request => ApplyAdvancedFilterRangeSelection(dialog, request)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        var result = dialog.Result;
        var outcome = _commandBus.ExecuteRepeatable(
            _workbook.Id,
            () => new AdvancedFilterCommand(
                result.ListRange,
                result.CriteriaRange,
                result.CopyToCell,
                result.UniqueRecordsOnly,
                result.CopyToRange));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Advanced Filter");
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        if (result.CopyToCell is { } destinationCell)
            SetActiveCell(destinationCell);
        UpdateViewport();
    }

    private void ApplyAdvancedFilterRangeSelection(
        AdvancedFilterDialog? dialog,
        AdvancedFilterRangeSelectionRequest request)
    {
        if (dialog is null || SheetGrid.SelectedRange is not { } selectedRange)
            return;

        var rangeText = FormatWorkbookRange(selectedRange);
        if (request.CollapseDialog)
            dialog.Hide();

        try
        {
            dialog.ApplyRangeSelection(request.Target, rangeText);
        }
        finally
        {
            if (request.CollapseDialog)
            {
                dialog.Show();
                dialog.Activate();
            }
        }
    }

    private bool TryParseAdvancedFilterRange(string input, out GridRange range)
        => AdvancedFilterInputParser.TryParseRange(
            _currentSheetId,
            input,
            ResolveSheetIdByName,
            out range);

    private SheetId? ResolveSheetIdByName(string sheetName)
    {
        foreach (var item in _workbook.Sheets)
        {
            if (string.Equals(item.Name, sheetName, StringComparison.CurrentCultureIgnoreCase))
                return item.Id;
        }

        return null;
    }

    private void ConsolidateBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = SheetGrid.SelectedRange;
        var defaultSource = selected is { } selectedRange ? FormatWorkbookRange(selectedRange) : "A1:B2";
        var defaultDestination = selected?.Start.ToA1() ?? "A1";
        ConsolidateDialog? dialog = null;
        dialog = new ConsolidateDialog(
            _currentSheetId,
            defaultSource,
            defaultDestination,
            request => ApplyConsolidateRangeSelection(dialog, request),
            ResolveSheetIdByName) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        if (!TryExecuteRepeatableConsolidateCommand(dialog.Result, out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        SetActiveCell(dialog.Result.DestinationCell);
        EnsureCellVisible(dialog.Result.DestinationCell);
        UpdateViewport();
    }

    private void CircleInvalidDataMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var matches = DataValidationCirclePlanner.FindInvalidDataCells(_workbook, sheet);
        if (matches.Count == 0)
        {
            _messageService.ShowInfo(
                "No invalid data found.",
                "Circle Invalid Data");
            return;
        }

        var ranges = SelectionRangeService.CompressAddresses(matches);
        _selectionAnchor = matches[0];
        _selectionCursor = matches[0];
        SheetGrid.SelectedRange = ranges[0];
        SheetGrid.SelectedRanges = ranges;
        CellAddressBox.Text = ranges.Count == 1
            ? FormatRangeReference(ranges[0].Start, ranges[0].End)
            : $"{matches.Count} cells";
        EnsureCellVisible(matches[0]);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ClearValidationCirclesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SheetGrid.SelectedRanges = null;
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ApplyConsolidateRangeSelection(
        ConsolidateDialog? dialog,
        ConsolidateRangeSelectionRequest request)
    {
        BeginConsolidateRangeSelection(dialog, request);
    }

    private void BeginConsolidateRangeSelection(
        ConsolidateDialog? dialog,
        ConsolidateRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        CancelConsolidateRangeSelection(restoreDialog: false);
        _consolidateRangePickerSession = new ConsolidateRangePickerSession(dialog, request, IsEnabled);
        SheetGrid.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(ConsolidateRangePicker_MouseLeftButtonUp),
            handledEventsToo: true);
        PreviewKeyDown += ConsolidateRangePicker_KeyDown;
        dialog.Closed += ConsolidateRangePickerDialog_Closed;

        if (request.CollapseDialog)
            dialog.Hide();

        IsEnabled = true;
        Activate();
        SheetGrid.Focus();
    }

    private void ConsolidateRangePicker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_consolidateRangePickerSession is null)
            return;

        Dispatcher.BeginInvoke(
            new Action(() => CompleteConsolidateRangeSelection(applySelection: true)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ConsolidateRangePicker_KeyDown(object sender, KeyEventArgs e)
    {
        if (_consolidateRangePickerSession is null)
            return;

        if (e.Key == Key.Escape)
        {
            CompleteConsolidateRangeSelection(applySelection: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CompleteConsolidateRangeSelection(applySelection: true);
            e.Handled = true;
        }
    }

    private void ConsolidateRangePickerDialog_Closed(object? sender, EventArgs e) =>
        CancelConsolidateRangeSelection(restoreDialog: false);

    private void CompleteConsolidateRangeSelection(bool applySelection)
    {
        var session = _consolidateRangePickerSession;
        if (session is null)
            return;

        CancelConsolidateRangeSelection(restoreDialog: false);
        if (applySelection && SheetGrid.SelectedRange is { } selectedRange)
        {
            var rangeText = FormatConsolidateRangeSelection(
                session.Dialog.DefaultSheetId,
                session.Request.Target,
                selectedRange);
            session.Dialog.ApplyRangeSelection(session.Request.Target, rangeText);
        }

        RestoreConsolidateDialogAfterRangeSelection(session);
    }

    private void CancelConsolidateRangeSelection(bool restoreDialog)
    {
        var session = _consolidateRangePickerSession;
        if (session is null)
            return;

        _consolidateRangePickerSession = null;
        SheetGrid.RemoveHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(ConsolidateRangePicker_MouseLeftButtonUp));
        PreviewKeyDown -= ConsolidateRangePicker_KeyDown;
        session.Dialog.Closed -= ConsolidateRangePickerDialog_Closed;
        if (restoreDialog)
            RestoreConsolidateDialogAfterRangeSelection(session);
    }

    private void RestoreConsolidateDialogAfterRangeSelection(ConsolidateRangePickerSession session)
    {
        IsEnabled = session.OwnerWasEnabled;
        if (session.Request.CollapseDialog && !session.Dialog.IsVisible)
            session.Dialog.Show();
        session.Dialog.Activate();
    }

    private string FormatConsolidateRangeSelection(
        SheetId defaultSheetId,
        ConsolidateRangeSelectionTarget target,
        GridRange selectedRange) =>
        target == ConsolidateRangeSelectionTarget.DestinationCell
            ? FormatWorkbookCellReference(selectedRange.Start, defaultSheetId)
            : WorkbookRangeTextCodec.Format(
                selectedRange,
                defaultSheetId,
                sheetId => _workbook.GetSheet(sheetId)?.Name);

    private string FormatWorkbookCellReference(CellAddress address, SheetId defaultSheetId)
    {
        var reference = FormatCellReference(address);
        var sheetName = _workbook.GetSheet(address.Sheet)?.Name;
        return sheetName is null || address.Sheet.Equals(defaultSheetId)
            ? reference
            : $"{PivotUiPlanner.QuoteSheetNameForReference(sheetName)}!{reference}";
    }

    private bool TryExecuteRepeatableConsolidateCommand(
        ConsolidateDialogResult result,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateCommand() =>
            new ConsolidateCommand(
                result.SourceRanges,
                result.DestinationCell,
                result.Function,
                result.UseTopRowLabels,
                result.UseLeftColumnLabels,
                result.CreateLinksToSourceData);

        try
        {
            outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        }
        catch (Exception ex)
        {
            outcome = new CommandOutcome(false, ex.Message);
        }

        if (outcome.Success)
        {
            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, "Consolidate");
        return false;
    }

    private sealed record ConsolidateRangePickerSession(
        ConsolidateDialog Dialog,
        ConsolidateRangeSelectionRequest Request,
        bool OwnerWasEnabled);

    // ── What-If Analysis ─────────────────────────────────────────────────────

    private void WhatIfAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void SubtotalBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_SubtotalSelectRange"),
                UiText.Get("MainWindowMessage_SubtotalTitle"));
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = new SubtotalDialog(sheet is null ? null : SubtotalDialog.BuildColumnChoices(sheet, range)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        if (dialog.Result.Action == SubtotalDialogAction.RemoveAll)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Remove Subtotals",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(currentRange, sheetId);
                        return new RemoveSubtotalRowsCommand(sheetId, sheetRange);
                    },
                    out var removeOutcome))
                return;

            RecalculateIfAutomatic(removeOutcome.AffectedCells ?? []);
            UpdateViewport();
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Subtotal",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(currentRange, sheetId);
                    var subtotalCommand = new SubtotalCommand(
                        sheetId,
                        sheetRange,
                        groupByColumnOffset: dialog.Result.GroupColumnOffset,
                        subtotalColumnOffsets: dialog.Result.SubtotalColumnOffsets,
                        functionNumber: dialog.Result.FunctionNumber,
                        pageBreakBetweenGroups: dialog.Result.PageBreakBetweenGroups,
                        summaryBelowData: dialog.Result.SummaryBelowData);
                    return dialog.Result.ReplaceCurrentSubtotals
                        ? new CompositeWorkbookCommand("Subtotal", [new RemoveSubtotalRowsCommand(sheetId, sheetRange), subtotalCommand])
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
        GoalSeekDialog? dlg = null;
        dlg = new GoalSeekDialog(
            _currentSheetId,
            selectedCell,
            request => ApplyGoalSeekRangeSelection(dlg, request)) { Owner = this };

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

    private void ApplyGoalSeekRangeSelection(
        GoalSeekDialog? dialog,
        GoalSeekRangeSelectionRequest request)
    {
        if (dialog is null || SheetGrid.SelectedRange is not { } selectedRange)
            return;

        if (request.CollapseDialog)
            dialog.Hide();

        try
        {
            dialog.ApplyRangeSelection(request.Target, selectedRange.Start);
        }
        finally
        {
            if (request.CollapseDialog)
            {
                dialog.Show();
                dialog.Activate();
            }
        }
    }

    // ── Review tab ────────────────────────────────────────────────────────────

    private void ForecastSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_ForecastSheetSelectRange"),
                UiText.Get("MainWindowMessage_ForecastSheetTitle"));
            return;
        }

        var dialog = new ForecastSheetDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new ForecastSheetCommand(range, dialog.Result.Periods), "Forecast Sheet"))
            return;

        var forecastSheet = _workbook.Sheets.LastOrDefault();
        var refreshedSelectionUi = false;
        if (forecastSheet is not null)
        {
            _currentSheetId = forecastSheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
            refreshedSelectionUi = true;
        }

        RecalculateWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        if (!refreshedSelectionUi)
            RefreshStatusBar();
    }

    private void DataTableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_DataTableSelectRange"),
                UiText.Get("MainWindowMessage_DataTableTitle"));
            return;
        }

        DataTableDialog? dialog = null;
        dialog = new DataTableDialog(
            _currentSheetId,
            range,
            request => ApplyDataTableRangeSelection(dialog, request)) { Owner = this };
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
            createCommand = currentRange => new OneVariableDataTableCommand(currentRange, formulaCell, inputCell, dialog.Result.Orientation);
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

    private void ApplyDataTableRangeSelection(
        DataTableDialog? dialog,
        DataTableRangeSelectionRequest request)
    {
        if (dialog is null || SheetGrid.SelectedRange is not { } selectedRange)
            return;

        if (request.CollapseDialog)
            dialog.Hide();

        try
        {
            dialog.ApplyRangeSelection(request.Target, selectedRange.Start);
        }
        finally
        {
            if (request.CollapseDialog)
            {
                dialog.Show();
                dialog.Activate();
            }
        }
    }
}
