using System;
using System.Windows;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private readonly WorksheetFilterWorkflowSession _filterWorkflowSession = new();

    /// <summary>
    /// R57-formula-subtotal-aggregate-5-1: every filter/sort command in this file dispatches through
    /// TryExecuteRepeatableCurrentRangeCommand/TryExecuteRepeatableCurrentSelectionRangesCommand
    /// (MainWindow.CommandExecution.cs), whose success path only marks the workbook dirty and bumps
    /// the navigation-cache revision -- it never calls RecalculateIfAutomatic/RecalculateWorkbook.
    /// Applying, changing, or clearing an AutoFilter changes which rows are hidden, and Sort
    /// reorders cell values; either way, SUBTOTAL(101-111)/AGGREGATE ignore-hidden formulas (and any
    /// other formula depending on the affected range) keep their stale cached value until an
    /// unrelated later edit happens to trigger a recalc pass that touches them. Real Excel always
    /// recalculates the instant filter visibility (or sorted values) change, so force a full
    /// recalculation here after every filter/sort mutation in this file.
    /// </summary>
    private void RecalculateAfterFilterOrSort() => RecalculateWorkbook();

    /// <summary>
    /// R127-commands-sort-multiarea-1: real Excel refuses Sort outright on a Ctrl+click multi-area
    /// selection ("This operation is not allowed on multiple selections. Select a single range and
    /// click the command again."), rather than quietly reordering only the active area's rows while
    /// every other selected area is left completely untouched -- which is worse than a no-op if the
    /// areas held related data the user expected to stay row-aligned (e.g. two side-by-side blocks).
    /// SortAscButton_Click/SortDescButton_Click/SortCustomButton_Click (and their Home-tab menu
    /// aliases SortAZMenuItem_Click/SortZAMenuItem_Click/SortCustomMenuItem_Click, which delegate
    /// straight into these) all gated only on SheetGrid.SelectedRange with no check of
    /// SheetGrid.SelectedRanges, so a second Ctrl+click area was silently dropped. Mirrors the
    /// identical refusal ExecuteCopy/ExecuteCut already apply for the same multi-area scenario
    /// (CreateMultiRangeClipboardError, MainWindow.ClipboardCommands.cs), and the shared Avalonia
    /// session's SortSelectedRange overloads (WorkbookSession.cs) get the same refusal.
    /// </summary>
    private bool TryRejectMultiAreaSort(GridRange range)
    {
        if (GetCurrentSelectionRanges(range).Count <= 1)
            return false;

        ShowCommandError(new CommandOutcome(false, CreateMultiRangeClipboardError("Sort")), "Sort");
        return true;
    }

    private void SortAscButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (TryRejectMultiAreaSort(range)) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => CreateQuickSortCommand(currentRange, ascending: true)))
            return;
        RecalculateAfterFilterOrSort();
        UpdateViewport();
    }

    private void SortDescButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (TryRejectMultiAreaSort(range)) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => CreateQuickSortCommand(currentRange, ascending: false)))
            return;
        RecalculateAfterFilterOrSort();
        UpdateViewport();
    }

    /// <summary>
    /// R34-commands-sort-custom-deep-2: the quick ribbon Sort Ascending/Descending buttons passed
    /// SelectedRange straight into SortCommand with no header exclusion, so a header row (e.g. "Name",
    /// "Score") got sorted in among the data rows instead of staying pinned at the top -- unlike
    /// SortCustomButton_Click, which already excludes an (opt-in) header row via
    /// SortDialog.ExcludeHeaderRow before building its SortCommand. The quick buttons have no dialog to
    /// ask the user, so auto-detect a header row with the same heuristic Quick Analysis already uses
    /// (first row all-text, at least one data row numeric/date) and exclude it the same way.
    /// </summary>
    private SortCommand CreateQuickSortCommand(GridRange range, bool ascending)
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet)
            return new SortCommand(_currentSheetId, range, sortByColOffset: 0, ascending);

        var plan = QuickSortRangePlanner.Create(sheet, range, SheetGrid.ActiveCell);
        return new SortCommand(_currentSheetId, plan.Range, plan.SortByColOffset, ascending);
    }

    // Auto-detects whether `range` looks like it has a header row, using the same heuristic the
    // quick ribbon Sort Asc/Desc buttons (CreateQuickSortCommand, above) already use, instead of
    // always defaulting the Custom Sort dialog's "My data has headers"
    // checkbox to checked (R51-commands-sort-custom-multilevel-3-1).
    private bool DetectSortDialogHasHeaders(GridRange range) =>
        _workbook.GetSheet(_currentSheetId) is { } sheet &&
        QuickSortRangePlanner.HasLikelyHeaderRow(sheet, range);

    private void SortCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (TryRejectMultiAreaSort(range)) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var hasHeaders = DetectSortDialogHasHeaders(range);
        var dialog = new SortDialog(
            columnChoices: SortDialog.BuildColumnChoices(sheet, range, hasHeaders: true),
            genericColumnChoices: SortDialog.BuildColumnChoices(sheet, range, hasHeaders: false),
            rowChoices: SortDialog.BuildRowChoices(range),
            colorChoices: SortDialog.BuildColorChoices(_workbook, sheet, range),
            cellColorChoices: SortDialog.BuildColorChoices(_workbook, sheet, range, SortOn.CellColor),
            fontColorChoices: SortDialog.BuildColorChoices(_workbook, sheet, range, SortOn.FontColor),
            hasHeaders: hasHeaders,
            iconWorkbook: _workbook,
            iconSheet: sheet,
            iconRange: range)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        var keys = dialog.ResultSortKeys;
        if (CustomSortOrder.TryParse(dialog.ResultOptions.FirstKeySortOrder, out var customOrder))
            keys = SortDialog.ApplyCustomOrderToFirstKey(keys, customOrder);
        var options = new SortOptions(dialog.ResultOptions.CaseSensitive, dialog.ResultOptions.LeftToRight);

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(
                    _currentSheetId,
                    dialog.ResultOptions.LeftToRight
                        ? currentRange
                        : SortDialog.ExcludeHeaderRow(currentRange, dialog.ResultHasHeaders),
                    keys,
                    options)))
            return;
        RecalculateAfterFilterOrSort();
        UpdateViewport();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } selectedRange ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet)
        {
            return;
        }

        var range = AutoFilterToggleRangePlanner.Create(sheet, selectedRange);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Filter",
                range,
                _ => new ToggleWorksheetAutoFilterCommand(_currentSheetId, range)))
        {
            return;
        }

        _filterWorkflowSession.ResetAutoFilterState();
        RecalculateAfterFilterOrSort();
        UpdateFilterViewportAndStatusBar();
    }

    private bool TryExecuteAutoFilterMutation(WorksheetFilterMutationPlan plan)
    {
        if (!TryExecuteRepeatableCurrentRangeCommand(
                plan.HistoryLabel,
                plan.Range,
                plan.CreateCommand))
            return false;

        _filterWorkflowSession.RecordSuccessfulMutation(plan);
        RecalculateAfterFilterOrSort();
        return true;
    }

    private void ReapplyAutoFilter()
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet ||
            _filterWorkflowSession.CreateReapplyPlan(sheet) is not { } plan)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_ReapplyFilterNoFilter"),
                UiText.Get("MainWindowMessage_ReapplyFilterTitle"));
            return;
        }

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Reapply Filter",
                plan.Range,
                _ => plan.CreateCommand("Reapply Filter")))
            return;

        RecalculateAfterFilterOrSort();
        RestoreAutoFilterRangeSelection(plan.Range);
        UpdateFilterViewportAndStatusBar();
    }

    private void UpdateFilterViewportAndStatusBar()
    {
        UpdateViewport();
        RefreshStatusBar();
    }


    private bool ApplyAutoFilterDialogResult(GridRange range, uint filterColOffset, AutoFilterDialogResult result, string title)
    {
        var plan = _filterWorkflowSession.PlanDialogResult(
            _currentSheetId,
            range,
            filterColOffset,
            result);
        if (!plan.Success)
        {
            var message = UiText.Get(WorksheetFilterMessagePlanner.GetPlanErrorResourceKey(plan));
            _messageService.ShowWarning(message, title);
            return false;
        }

        if (!TryExecuteAutoFilterMutation(plan))
            return false;
        RestoreAutoFilterRangeSelection(range);
        return true;
    }

    private void CfRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_SelectRangeFirst"),
                UiText.Get("MainWindowMessage_CfRuleTitle"));
            return;
        }

        var dialog = new ConditionalFormatThresholdDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var cf = new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = dialog.Result.ThresholdText,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };

        ApplyConditionalFormatPreset(cf);
    }

    private void ValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_SelectRangeFirst"),
                UiText.Get("MainWindowMessage_DataValidationTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        DataValidation? existingRule = null;
        if (sheet is not null)
        {
            foreach (var rule in DataValidationService.GetApplicable(sheet, range.Start))
            {
                existingRule = rule;
                break;
            }
        }

        DataValidationDialog? dlg = null;
        dlg = new DataValidationDialog(existingRule, request => ApplyDataValidationRangeSelection(dlg, request))
        {
            Owner = this,
            SelectionSource = DataValidationService.FormatListSourceRange(range, sheet?.Name, sheet?.Name)
        };
        if (dlg.ShowDialog() != true && !dlg.Accepted) return;

        if (dlg.ClearRequested)
        {
            if (!TryExecuteRepeatableCurrentSelectionRangesCommand(
                    "Clear Data Validation",
                    range,
                    (sheetId, currentRange) => new ClearDataValidationCommand(sheetId, currentRange)))
                return;

            UpdateViewport();
            return;
        }

        if (dlg.Result == null) return;

        var dv = dlg.Result;
        var ranges = GetCurrentSelectionRanges(range);
        dv.AppliesTo = ranges[0];
        dv.AdditionalRanges.Clear();
        dv.AdditionalRanges.AddRange(ranges.Skip(1));

        try
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Data Validation",
                    sheetId =>
                    {
                        var rule = GroupedSheetRangePlanner.CloneDataValidationForSheet(dv, sheetId);
                        return CreateDataValidationCommand(
                            sheetId,
                            rule,
                            existingRule,
                            dlg.ApplyToSameSettings);
                    }))
                return;
        }
        catch (Exception ex)
        {
            ShowCommandError(
                new CommandOutcome(false, $"Data validation could not be applied. {ex.Message}"),
                UiText.Get("MainWindowMessage_DataValidationTitle"));
            return;
        }
        UpdateViewport();
    }

    private void ApplyDataValidationRangeSelection(
        DataValidationDialog? dialog,
        DataValidationRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange =>
            {
                var sheet = _workbook.GetSheet(_currentSheetId);
                var formulaText = DataValidationService.FormatListSourceRange(
                    selectedRange,
                    sheet?.Name,
                    sheet?.Name);
                dialog.ApplyRangeSelection(request.Target, formulaText);
            });
    }

    private IWorkbookCommand CreateDataValidationCommand(
        SheetId sheetId,
        DataValidation rule,
        DataValidation? existingRule,
        bool applyToSameSettings)
    {
        if (!applyToSameSettings || existingRule is null || _workbook.GetSheet(sheetId) is not { } sheet)
            return new SetDataValidationCommand(sheetId, rule);

        var commands = sheet.DataValidations
            .Where(candidate => HasSameDataValidationSettings(candidate, existingRule))
            .Select(candidate => new SetDataValidationCommand(
                sheetId,
                CloneDataValidationForRange(rule, candidate.AppliesTo, candidate.Id)))
            .Cast<IWorkbookCommand>()
            .ToList();

        if (commands.Count == 0)
            commands.Add(new SetDataValidationCommand(sheetId, rule));

        return new CompositeWorkbookCommand("Data Validation", commands);
    }

    private static bool HasSameDataValidationSettings(DataValidation left, DataValidation right) =>
        left.Type == right.Type &&
        left.Operator == right.Operator &&
        string.Equals(left.Formula1, right.Formula1, StringComparison.Ordinal) &&
        string.Equals(left.Formula2, right.Formula2, StringComparison.Ordinal) &&
        left.AllowBlank == right.AllowBlank &&
        left.ShowDropdown == right.ShowDropdown &&
        left.AlertStyle == right.AlertStyle &&
        left.ShowInputMessage == right.ShowInputMessage &&
        left.ShowErrorMessage == right.ShowErrorMessage &&
        string.Equals(left.ErrorTitle, right.ErrorTitle, StringComparison.Ordinal) &&
        string.Equals(left.ErrorMessage, right.ErrorMessage, StringComparison.Ordinal) &&
        string.Equals(left.PromptTitle, right.PromptTitle, StringComparison.Ordinal) &&
        string.Equals(left.PromptMessage, right.PromptMessage, StringComparison.Ordinal);

    private static DataValidation CloneDataValidationForRange(DataValidation source, GridRange range, Guid id) =>
        new()
        {
            Id = id,
            AppliesTo = range,
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = source.Formula1,
            Formula2 = source.Formula2,
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = source.NativeChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };

    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } selectedRange ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet)
        {
            return;
        }

        var range = AutoFilterToggleRangePlanner.Create(sheet, selectedRange);
        if (!AutoFilterDropdownMenuPlanner.HasActiveFilter(sheet, range))
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_ClearFilterNoFilter"),
                UiText.Get("MainWindowMessage_ClearFilterTitle"));
            return;
        }

        var clearPlan = _filterWorkflowSession.CreateClearAllPlan(sheet, range);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Clear Filter",
                range,
                currentRange => _filterWorkflowSession.CreateClearAllPlan(sheet, currentRange).Command))
            return;
        _filterWorkflowSession.RecordSuccessfulClearAll(clearPlan);
        RecalculateAfterFilterOrSort();
        RestoreAutoFilterRangeSelection(range);
        UpdateFilterViewportAndStatusBar();
    }

    private void RestoreAutoFilterRangeSelection(GridRange range)
    {
        if (SheetGrid.SelectedRange == range)
            return;

        if (SheetGrid.SelectedRange is not { } selectedRange ||
            selectedRange.RowCount != 1 ||
            selectedRange.ColCount != 1 ||
            selectedRange.Start.Row != range.Start.Row ||
            !range.Contains(selectedRange.Start))
        {
            return;
        }

        SetSelectionRange(range, selectedRange.Start);
    }

    private void NamedRangesButton_Click(object sender, RoutedEventArgs e)
    {
        var initialRange = SheetGrid.SelectedRange;
        NamedRangeDialog? dlg = null;
        dlg = new NamedRangeDialog(
            _workbook,
            ExecuteDialogCommandPreservingSelection,
            initialRange,
            request => ApplyNamedRangeSelection(dlg, request))
        {
            Owner = this
        };
        dlg.ShowDialog();
        UpdateViewport();
    }

}
