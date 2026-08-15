using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FreeX.App.Presentation.Consolidate;
using FreeX.App.Presentation.DataTools;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using AdvancedFilterDialogResult = FreeX.App.Presentation.Filtering.AdvancedFilterDialogResult;
using AdvancedFilterPlanner = FreeX.App.Presentation.Filtering.AdvancedFilterPlanner;
using AdvancedFilterRangeSelectionRequest = FreeX.App.Presentation.Filtering.AdvancedFilterRangeSelectionRequest;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private ConsolidateRangePickerSession? _consolidateRangePickerSession;

    // Guards GetDataBtn_Click's background import so a concurrent File > Open (which can swap
    // _workbook/_workbookRef.Current and _currentSheetId out from under the awaited Task.Run) is
    // detected: the import always captures its target session/sheet/destination before the await
    // and executes through that captured session, so the data never lands in a different (newly
    // opened) workbook (R68-async-ordering-race-sweep-2).
    private bool _isImportingData;

    /// <summary>
    /// R134-io-getdata-refresh-shrink-wpf: the extent (row/col count) the most recent SUCCESSFUL
    /// Get Data import wrote at a given (workbook, sheet, destination anchor). The WPF host has no
    /// dedicated "Refresh" action (unlike the Avalonia shell's Data ▸ Refresh All, which re-runs a
    /// remembered file source via MainWindow.GetData.cs's <c>RefreshImportedData</c>/
    /// <c>_lastImportSource</c>) -- here, RefreshAllBtn_Click still only recalculates (see the
    /// comment on that method below). What DOES exist, and what this fixes, is the plain case of a
    /// user running Get Data twice into the same destination cell (e.g. always importing into A1):
    /// without remembering the prior extent, a second import from a SHRUNK source (fewer
    /// rows/columns than the first) left the first import's leftover cells behind with stale
    /// values, indistinguishable from freshly imported data. Fed into
    /// <see cref="WorkbookImportWorkflow"/>, which constructs the shared import command with the
    /// prior extent so <c>Apply</c> can clear exactly that leftover rectangle.
    /// Keyed by workbook id (not just sheet+destination) so a concurrent File > Open mid-import
    /// (R68-async-ordering-race-sweep-2) can never cause one workbook's remembered extent to bleed
    /// into another's.
    /// </summary>
    private (WorkbookId WorkbookId, SheetId SheetId, CellAddress Destination, uint RowCount, uint ColCount)? _lastImportExtent;

    private async void GetDataBtn_Click(object sender, RoutedEventArgs e)
    {
        var plan = ImportDataFilePickerPlanner.BuildAdapterOpenDialogPlan(_fileAdapters);
        var adapters = plan.Adapters;
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

        var result = WpfFileDialogService.ShowOpenDialog(
            this,
            plan.Filter,
            checkFileExists: plan.CheckFileExists,
            multiselect: plan.Multiselect);
        if (!result.Chosen) return;

        var ext = System.IO.Path.GetExtension(result.FileName!).ToLowerInvariant();
        var adapter = FileFormatResolver.FindOpenAdapter(adapters, ext, out var format);
        if (adapter is null)
        {
            RecordDiagnosticEvent("import_failed", BuildImportDiagnosticProperties(ext, null, "unsupported_extension"));
            return;
        }

        await ImportDataFromFileAsync(result.FileName!, adapter, ext, format);
    }

    /// <summary>
    /// Runs the Get Data background import (adapter.Load off the UI thread, then materializing an
    /// ImportSheetCommand) for a file already chosen by the caller. Split out of GetDataBtn_Click so
    /// the ordering-race guard below is directly testable without driving a real WPF OpenFileDialog.
    /// </summary>
    private async Task ImportDataFromFileAsync(string importPath, IFileAdapter adapter, string ext, FileFormatDescriptor? format)
    {
        if (_isImportingData) return;

        try
        {
            _isImportingData = true;
            // Block input for the duration of the import (mirrors ExportAsPdf's RootGrid.IsEnabled
            // guard) so File > Open cannot be reached from the ribbon/menus while the import is in
            // flight -- matching Excel's modal Get Data behavior.
            RootGrid.IsEnabled = false;

            // Capture the target session/workbook/sheet/destination BEFORE the await: a concurrent File >
            // Open reachable via a keyboard shortcut (not gated by RootGrid.IsEnabled above) can
            // still swap _workbook/_workbookRef.Current and _currentSheetId out from under this
            // await. Executing through the captured session below (instead of the current session)
            // guarantees
            // the imported data lands in the workbook Get Data was invoked on, never in a workbook
            // opened afterward (R68-async-ordering-race-sweep-2).
            SynchronizeWorkbookSessionSelection();
            var targetSession = _session;
            var targetWorkbook = targetSession.Workbook;
            var targetSheetId = _currentSheetId;
            var destination = SheetGrid.SelectedRange?.Start ?? new CellAddress(targetSheetId, 1, 1);

            (uint RowCount, uint ColCount)? previousExtent = null;
            if (_lastImportExtent is { } previousImport &&
                previousImport.WorkbookId == targetWorkbook.Id &&
                previousImport.SheetId == targetSheetId &&
                previousImport.Destination == destination)
            {
                previousExtent = (previousImport.RowCount, previousImport.ColCount);
            }

            var importResult = await WorkbookImportWorkflow.ImportPathAsync(
                importPath,
                ext,
                adapter,
                targetSheetId,
                destination,
                command => ToCommandOutcome(targetSession.ExecuteCommandPreservingSelection(command)),
                previousExtent: previousExtent);

            if (importResult.Outcome == WorkbookImportExecutionOutcome.EmptyWorkbook)
            {
                RecordDiagnosticEvent("import_failed", BuildImportDiagnosticProperties(
                    ext, format?.FormatName ?? adapter.FormatName, importResult.Reason, importResult.WorksheetCount));
                return;
            }

            if (importResult.Outcome == WorkbookImportExecutionOutcome.Failed)
            {
                RecordDiagnosticEvent("import_failed", BuildImportDiagnosticProperties(
                    ext,
                    format?.FormatName ?? adapter.FormatName,
                    importResult.Reason,
                    importResult.WorksheetCount,
                    importResult.ErrorDetail));
                ShowOwnedMessage(
                    importResult.UserMessage ?? UiText.Get("GetData_ImportFailedMessage"),
                    UiText.Get("MainWindowMessage_GetDataTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (importResult.Outcome == WorkbookImportExecutionOutcome.Canceled)
                return;

            var outcome = importResult.CommandOutcome
                ?? throw new InvalidOperationException("Import command did not produce a result.");
            RecordDiagnosticEvent("command_invoked", new Dictionary<string, string?>
            {
                ["command"] = "Get Data",
                ["status"] = outcome.Success ? "succeeded" : "failed"
            });
            if (!outcome.Success)
            {
                if (ReferenceEquals(_workbook, targetWorkbook))
                    ShowCommandError(outcome, "Get Data");
                RecordDiagnosticEvent("import_failed", BuildImportDiagnosticProperties(
                    ext, format?.FormatName ?? adapter.FormatName, importResult.Reason, importResult.WorksheetCount));
                return;
            }

            // Remember this import's actual written extent (the source sheet's used range) for the
            // next Get Data into this same anchor, regardless of whether this window still shows
            // targetWorkbook (a concurrent File > Open does not invalidate what was actually written).
            var importedUsedRange = importResult.ImportedWorkbook!.Sheets[0].GetUsedRange();
            _lastImportExtent = (
                targetWorkbook.Id,
                targetSheetId,
                destination,
                importedUsedRange?.RowCount ?? 0,
                importedUsedRange?.ColCount ?? 0);

            // A concurrent File > Open replaced this window's workbook while the import was in
            // flight. The data above still landed correctly in the originally-targeted workbook (via
            // the captured id), but that workbook is no longer the one this window displays, so the
            // window-level follow-up below -- which all key off the CURRENT _workbook/_currentSheetId
            // -- must be skipped or it would incorrectly touch the NEW workbook instead.
            if (ReferenceEquals(_workbook, targetWorkbook))
            {
                if (!outcome.IsNoOp)
                {
                    ApplySuccessfulWorkbookSessionCommand();
                    ApplyWorkbookSessionDocumentStateToRenderer();
                }

                SetActiveCell(destination);
                EnsureCellVisible(destination);
                UpdateViewport();
                PruneCorrectedValidationCircles();
                RefreshStatusBar();
            }

            RecordDiagnosticEvent("import_completed", BuildImportDiagnosticProperties(
                ext, format?.FormatName ?? adapter.FormatName, worksheetCount: importResult.WorksheetCount));
        }
        catch (Exception ex)
        {
            var diagnostic = WorkbookImportFailurePlanner.FromException(ext, ex);
            RecordDiagnosticEvent(
                "import_failed",
                BuildImportDiagnosticProperties(
                    ext,
                    format?.FormatName ?? adapter.FormatName,
                    diagnostic.Reason,
                    errorDetail: diagnostic.Detail));
            ShowOwnedMessage(diagnostic.UserMessage, UiText.Get("MainWindowMessage_GetDataTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isImportingData = false;
            RootGrid.IsEnabled = true;
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
            ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(extension)
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
    // R134-io-getdata-refresh-shrink-wpf: this is a plain RECALCULATION, not a data re-import --
    // the WPF host has no "remembered import source" concept (no equivalent of the Avalonia shell's
    // MainWindow.GetData.cs _lastImportSource/RefreshImportedData), so there is nothing here for
    // Data ▸ Refresh All to re-run. Building that (remembering the file path/adapter/encoding
    // options across arbitrary file-format adapters -- not just the delimited-text wizard Avalonia
    // has -- and re-invoking Load off the UI thread with its own error handling) is a materially
    // larger feature than this fix's scope. Deliberately left as-is; see _lastImportExtent above
    // for the narrower shrink-on-reimport fix that IS in scope here (a second Get Data run into the
    // same destination cell, which does not go through this button).
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
        // The Destination range-picker (ApplyTextToColumnsRangeSelection) temporarily repurposes
        // SheetGrid.SelectedRange to capture the picked destination cell and never restores the
        // grid's selection afterward, so it must NOT be read here as the split's source range -
        // only the originally selected `range` is the source; the destination comes from
        // dialog.Result.Destination, parsed from the dialog's own text box.
        var currentRange = range;
        if (TextToColumnsApplyPlanner.FindOverwriteTargets(_workbook, targetSheetIds, currentRange, dialog.Result).Count > 0 &&
            !_messageService.AskYesNo(
                UiText.Get("MainWindowMessage_TextToColumnsReplaceDataPrompt"),
                UiText.Get("MainWindowMessage_TextToColumnsTitle")))
        {
            return;
        }

        if (!TryExecuteRepeatableCommand(
                () => CreateTextToColumnsCommand(CurrentGroupedEditSheetIds(), currentRange, dialog.Result),
                "Text to Columns",
                out _))
            return;
        UpdateViewport();
        PruneCorrectedValidationCircles();
    }

    private void ApplyTextToColumnsRangeSelection(
        TextToColumnsDialog? dialog,
        TextToColumnsRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(selectedRange.Start));
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
            ? RemoveDuplicatesPlanner.BuildColumnChoices(range, RemoveDuplicatesText)
            : RemoveDuplicatesPlanner.BuildColumnChoices(sheet, range, hasHeaders: true, RemoveDuplicatesText);
        var genericColumns = sheet is null
            ? RemoveDuplicatesPlanner.BuildColumnChoices(range, RemoveDuplicatesText)
            : RemoveDuplicatesPlanner.BuildColumnChoices(sheet, range, hasHeaders: false, RemoveDuplicatesText);
        var hasHeaders = sheet is not null && RemoveDuplicatesPlanner.GuessHasHeaders(sheet, range);
        var dialog = new RemoveDuplicatesDialog(range, columns, genericColumns, hasHeaders) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        var plan = dialog.Result;
        RemoveDuplicateRowsCommand? activeSheetCommand = null;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Remove Duplicates",
                sheetId =>
                {
                    var command = plan.CreateCommand(sheetId);
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
        PruneCorrectedValidationCircles();
    }

    private static RemoveDuplicatesPlannerText RemoveDuplicatesText =>
        new(UiText.Get("RemoveDuplicates_ColumnLabel"));

    private void AdvancedFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var defaultList = SheetGrid.SelectedRange is { } selected && sheet is not null
            ? FormatWorkbookRange(AdvancedFilterPlanner.CreateDefaultListRange(sheet, selected))
            : "A1:C10";
        AdvancedFilterDialog? dialog = null;
        dialog = new AdvancedFilterDialog(
            _currentSheetId,
            defaultList,
            ResolveSheetIdByName,
            request => ApplyAdvancedFilterRangeSelection(dialog, request)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        ApplyAdvancedFilterResult(dialog.Result);
    }

    /// <summary>
    /// Applies a completed Advanced Filter dialog result: runs the command, remembers an in-place
    /// (no copy destination) filter for Data &gt; Reapply, and refreshes the UI. Split out of
    /// AdvancedFilterBtn_Click so the R72-commands-sort-filter-4-3 remember-for-Reapply behavior is
    /// directly testable without driving the real modal AdvancedFilterDialog.
    /// </summary>
    private void ApplyAdvancedFilterResult(AdvancedFilterDialogResult result)
    {
        if (!TryExecuteRepeatableCommand(
                () => new AdvancedFilterCommand(
                result.ListRange,
                result.CriteriaRange,
                result.CopyToCell,
                result.UniqueRecordsOnly,
                result.CopyToRange),
                "Advanced Filter",
                out _))
            return;

        // R72-commands-sort-filter-4-3: remember an IN-PLACE Advanced Filter (no "Copy to another
        // location" destination) so Data > Reapply (MainWindow.DataFilterCommands.cs
        // ReapplyAutoFilter) can re-run it after the underlying data changes, exactly like the
        // AutoFilter column factories it already remembers there. A "copy to another location"
        // Advanced Filter is a one-time extraction, not a persistent in-place filter, so that case is
        // intentionally left unremembered (and does not clear a previously remembered in-place one).
        if (result.CopyToCell is null)
        {
            _filterWorkflowSession.RememberAdvancedFilter(
                result.ListRange,
                result.CriteriaRange,
                filterInPlace: true,
                uniqueRecordsOnly: result.UniqueRecordsOnly);
        }

        if (result.CopyToCell is { } destinationCell)
            SetActiveCell(destinationCell);
        UpdateViewport();
        PruneCorrectedValidationCircles();
        RefreshStatusBar();
    }

    private void ApplyAdvancedFilterRangeSelection(
        AdvancedFilterDialog? dialog,
        AdvancedFilterRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(request.Target, FormatWorkbookRange(selectedRange)));
    }

    private bool TryParseAdvancedFilterRange(string input, out GridRange range)
        => AdvancedFilterPlanner.TryParseRange(
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

        var plan = ConsolidateApplicationWorkflow.Plan(_workbook, dialog.Result, overwriteConfirmed: false);
        if (ShowConsolidateValidationIssue(plan))
            return;

        if (plan.Disposition == ConsolidateApplicationDisposition.ConfirmOverwrite)
        {
            var choice = ShowOwnedMessage(
                ConsolidateApplicationWorkflow
                    .DescribeOverwriteConfirmation(plan)
                    .Resolve(UiText.Get, UiText.Format),
                UiText.Get("Consolidate_Consolidate"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (choice != MessageBoxResult.OK)
                return;

            plan = ConsolidateApplicationWorkflow.Plan(_workbook, dialog.Result, overwriteConfirmed: true);
            if (ShowConsolidateValidationIssue(plan))
                return;
        }

        if (!TryExecuteRepeatableConsolidateCommand(plan, out var outcome))
            return;

        SetActiveCell(outcome.DestinationCell);
        EnsureCellVisible(outcome.DestinationCell);
        UpdateViewport();
        PruneCorrectedValidationCircles();
    }

    private bool ShowConsolidateValidationIssue(ConsolidateApplicationPlan plan)
    {
        if (plan.Disposition != ConsolidateApplicationDisposition.Invalid)
            return false;

        var presentation = ConsolidateDialogPlanner.DescribeIssue(
            plan.Issue,
            ConsolidateDialogMessageContext.FinalValidation);
        ShowOwnedMessage(
            presentation.Message.Resolve(UiText.Get, UiText.Format),
            UiText.Get("Consolidate_Consolidate"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return true;
    }

    private void CircleInvalidDataMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var matches = DataValidationCirclePlanner.FindInvalidDataCells(_workbook, sheet);
        if (matches.Count == 0)
        {
            SheetGrid.ValidationCircleCells = null;
            sheet.ValidationCircleCells = null;
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_CircleInvalidDataNoInvalidData"),
                UiText.Get("MainWindowMessage_CircleInvalidDataTitle"));
            return;
        }

        // Match Excel: Circle Invalid Data draws a persistent overlay of red ovals around every
        // cell that currently fails its validation rule. It does not change the current selection --
        // the previous implementation only reused the (transient) multi-range selection as a stand-in
        // for the circles, which vanished the instant the user clicked elsewhere or pressed an arrow key.
        // Mirrored onto Sheet.ValidationCircleCells (R90-print-twin-two-tier-sweep-1) so a print/PDF
        // renderer -- which only has the Workbook/SheetId, not this GridView instance -- can eventually
        // read the same circled-cell set instead of the state being trapped in a screen-only DependencyProperty.
        SheetGrid.ValidationCircleCells = matches;
        sheet.ValidationCircleCells = matches;
        EnsureCellVisible(matches[0]);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ClearValidationCirclesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SheetGrid.ValidationCircleCells = null;
        if (_workbook.GetSheet(_currentSheetId) is { } sheet)
            sheet.ValidationCircleCells = null;
        UpdateViewport();
        RefreshStatusBar();
    }

    // Excel auto-clears a cell's red "invalid data" circle the instant the flagged value is
    // corrected -- the user never has to manually re-run Data Validation > Circle Invalid Data.
    // SheetGrid.ValidationCircleCells is otherwise only ever (re)populated by
    // CircleInvalidDataMenuItem_Click and cleared by ClearValidationCirclesMenuItem_Click, so any
    // data-changing command in this file that can land on an already-circled cell (Get Data,
    // Text to Columns, Remove Duplicates, Advanced Filter, Consolidate, Subtotal, Data Table) needs
    // to re-check the still-circled set afterward, or a corrected cell stays circled until the user
    // manually re-runs the command.
    private void PruneCorrectedValidationCircles()
    {
        if (SheetGrid.ValidationCircleCells is not { Count: > 0 } circled)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        // The actual re-check is the shared WorkbookSession.PruneCorrectedValidationCircles helper
        // (FreeX.App.Services) so the Avalonia shell's equivalent overlay (MainWindow.DataTools.cs)
        // applies the identical rule.
        var pruned = WorkbookSession.PruneCorrectedValidationCircles(_workbook, sheet, circled);
        if (ReferenceEquals(pruned, circled))
            return;

        var remaining = pruned.Count == 0 ? null : pruned;
        SheetGrid.ValidationCircleCells = remaining;
        sheet.ValidationCircleCells = remaining;
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

        CancelConsolidateRangeSelection(restoreDialog: true);
        var session = new ConsolidateRangePickerSession(
            dialog,
            request,
            IsEnabled,
            dialog.Left,
            dialog.Top,
            dialog.Opacity,
            dialog.IsHitTestVisible);
        _consolidateRangePickerSession = session;
        SheetGrid.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(ConsolidateRangePicker_MouseLeftButtonUp),
            handledEventsToo: true);
        PreviewKeyDown += ConsolidateRangePicker_KeyDown;
        dialog.Closed += ConsolidateRangePickerDialog_Closed;

        if (request.CollapseDialog)
            CollapseConsolidateDialogForRangeSelection(session);

        SetConsolidateOwnerInputEnabled(true);
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
        SetConsolidateOwnerInputEnabled(session.OwnerWasEnabled);
        if (session.Request.CollapseDialog)
        {
            session.Dialog.Left = session.DialogLeft;
            session.Dialog.Top = session.DialogTop;
            session.Dialog.Opacity = session.DialogOpacity;
            session.Dialog.IsHitTestVisible = session.DialogIsHitTestVisible;
        }

        if (session.Dialog.IsVisible)
            session.Dialog.Activate();
    }

    private void SetConsolidateOwnerInputEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            EnableWindow(handle, isEnabled);
    }

    private static void CollapseConsolidateDialogForRangeSelection(ConsolidateRangePickerSession session)
    {
        var dialogWidth = EffectiveConsolidateDialogDimension(session.Dialog.ActualWidth, session.Dialog.Width, 420);
        var dialogHeight = EffectiveConsolidateDialogDimension(session.Dialog.ActualHeight, session.Dialog.Height, 560);
        session.Dialog.Opacity = 0;
        session.Dialog.IsHitTestVisible = false;
        session.Dialog.Left = SystemParameters.VirtualScreenLeft - dialogWidth - 32;
        session.Dialog.Top = SystemParameters.VirtualScreenTop - dialogHeight - 32;
    }

    private static double EffectiveConsolidateDialogDimension(double actual, double configured, double fallback)
    {
        if (!double.IsNaN(actual) && actual > 0)
            return actual;
        if (!double.IsNaN(configured) && configured > 0)
            return configured;
        return fallback;
    }

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

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
            : $"{SheetNameFormatter.QuoteIfNeeded(sheetName)}!{reference}";
    }

    private bool TryExecuteRepeatableConsolidateCommand(
        ConsolidateApplicationPlan plan,
        out ConsolidateExecutionOutcome outcome)
    {
        CommandOutcome? commandOutcome = null;
        outcome = ConsolidateApplicationWorkflow.Execute(
            plan,
            commandFactory =>
            {
                var success = TryExecuteRepeatableCommand(commandFactory, "Consolidate", out var result);
                commandOutcome = result;
                return new ConsolidateCommandAdapterResult(success, result.ErrorMessage);
            });

        if (outcome.Success)
            return true;

        if (commandOutcome is null)
            ShowCommandError(new CommandOutcome(false, outcome.ErrorMessage), "Consolidate");
        return false;
    }

    private sealed record ConsolidateRangePickerSession(
        ConsolidateDialog Dialog,
        ConsolidateRangeSelectionRequest Request,
        bool OwnerWasEnabled,
        double DialogLeft,
        double DialogTop,
        double DialogOpacity,
        bool DialogIsHitTestVisible);

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
        if (sheet is null)
        {
            _messageService.ShowWarning(
                SubtotalPlanner.NoOccupiedDataMessage,
                UiText.Get("MainWindowMessage_SubtotalTitle"));
            return;
        }

        if (!SubtotalPlanner.TryCreateSourceRange(
                sheet,
                range,
                out var sourceRange,
                out var sourceRangeError,
                requireCompleteTableShape: false))
        {
            _messageService.ShowWarning(
                sourceRangeError ?? SubtotalPlanner.NoOccupiedDataMessage,
                UiText.Get("MainWindowMessage_SubtotalTitle"));
            return;
        }

        var dialog = new SubtotalDialog(SubtotalDialog.BuildColumnChoices(sheet, sourceRange), sheet.OutlineSummaryBelow ?? true) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        if (dialog.Result.Action == SubtotalDialogPlanAction.RemoveAll)
        {
            if (!TryExecuteWorksheetLayout(
                    _session.RemoveSelectedRangeSubtotals,
                    "Remove Subtotals"))
                return;

            UpdateViewport();
            PruneCorrectedValidationCircles();
            return;
        }

        if (!TryExecuteWorksheetLayout(
                () => _session.ExecuteSubtotalOptions(dialog.Result.ToInputOptions()),
                "Subtotal"))
            return;

        UpdateViewport();
        PruneCorrectedValidationCircles();
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

        var proposal = _session.FindGoalSeekProposal(new GoalSeekRequest(setCell, targetValue, changingCell));
        if (!proposal.Success)
        {
            _messageService.ShowWarning(proposal.ErrorMessage!, "Microsoft Excel");
            return;
        }

        var result = proposal.SeekResult!;

        var statusDialog = new GoalSeekStatusDialog(result, targetValue) { Owner = this };
        if (statusDialog.ShowDialog() == true && statusDialog.ApplyResult)
        {
            var applyResult = _session.ApplyGoalSeekProposal(proposal);
            if (!applyResult.Success)
            {
                _messageService.ShowWarning(
                    applyResult.ErrorMessage ?? UiText.Get("MainWindowMessage_CommandCouldNotBeCompleted"),
                    UiText.Get("MainWindowMessage_CommandErrorTitle"));
                return;
            }

            ApplySuccessfulWorkbookSessionCommand();
            ApplyWorkbookSessionDocumentStateToRenderer();
            UpdateViewport();
        }
    }

    private void ApplyGoalSeekRangeSelection(
        GoalSeekDialog? dialog,
        GoalSeekRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(request.Target, selectedRange.Start));
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

        var plan = ForecastSheetPlanner.CreatePlan(_workbook, range, dialog.Result.Periods);
        var result = _session.ExecuteForecastSheetPlan(plan);
        if (!result.Success)
        {
            _messageService.ShowWarning(
                result.ErrorMessage ?? UiText.Get("MainWindowMessage_ForecastSheetSelectRange"),
                UiText.Get("MainWindowMessage_ForecastSheetTitle"));
            return;
        }

        _currentSheetId = _session.ActiveSheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        ApplyWorkbookSessionSelectionToRenderer();
        UpdateViewport();
        PruneCorrectedValidationCircles();
        RefreshSheetTabs();
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
        var plan = DataTablePlanner.CreatePlan(range, dialog.Result);

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Data Table",
                range,
                plan.CreateCommand,
                out var outcome))
            return;

        UpdateViewport();
        PruneCorrectedValidationCircles();
        RefreshStatusBar();
    }

    private void ApplyDataTableRangeSelection(
        DataTableDialog? dialog,
        DataTableRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(request.Target, selectedRange.Start));
    }
}
