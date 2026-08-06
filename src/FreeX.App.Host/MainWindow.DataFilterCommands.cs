using System;
using System.Windows;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.QuickAnalysis;
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

    private void SortAscButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, ExcludeHeaderRowForQuickSort(currentRange), sortByColOffset: 0, ascending: true)))
            return;
        RecalculateAfterFilterOrSort();
        UpdateViewport();
    }

    private void SortDescButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, ExcludeHeaderRowForQuickSort(currentRange), sortByColOffset: 0, ascending: false)))
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
    private GridRange ExcludeHeaderRowForQuickSort(GridRange range)
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet)
            return range;

        var hasHeaderRow = QuickAnalysisSelectionReader.Describe(sheet, range).HasHeaderRow;
        return SortDialog.ExcludeHeaderRow(range, hasHeaderRow);
    }

    // Auto-detects whether `range` looks like it has a header row, using the same heuristic the
    // quick ribbon Sort Asc/Desc buttons (ExcludeHeaderRowForQuickSort, above) and Quick Analysis
    // already use, instead of always defaulting the Custom Sort dialog's "My data has headers"
    // checkbox to checked (R51-commands-sort-custom-multilevel-3-1).
    private bool DetectSortDialogHasHeaders(GridRange range) =>
        _workbook.GetSheet(_currentSheetId) is { } sheet &&
        QuickAnalysisSelectionReader.Describe(sheet, range).HasHeaderRow;

    private void SortCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
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
            var message = plan.Error switch
            {
                WorksheetFilterMutationError.InvalidCriteria => FormatFilterPromptPlanError(plan.PromptError),
                WorksheetFilterMutationError.SelectionRequired => UiText.Get("MainWindowMessage_FilterSelectAtLeastOneItem"),
                _ => UiText.Get("MainWindowMessage_FilterUnsupportedCriterion")
            };
            _messageService.ShowWarning(message, title);
            return false;
        }

        if (!TryExecuteAutoFilterMutation(plan))
            return false;
        RestoreAutoFilterRangeSelection(range);
        return true;
    }

    private static string FormatFilterPromptPlanError(FilterPromptPlanError error) =>
        error switch
        {
            FilterPromptPlanError.TopBottomSyntax => UiText.Get("FilterPrompt_ErrorTopBottomSyntax"),
            FilterPromptPlanError.PercentageRange => UiText.Get("FilterPrompt_ErrorPercentageRange"),
            FilterPromptPlanError.PositiveItemCount => UiText.Get("FilterPrompt_ErrorPositiveItemCount"),
            FilterPromptPlanError.CompositeSyntax => UiText.Get("FilterPrompt_ErrorCompositeSyntax"),
            FilterPromptPlanError.DateBetweenSyntax => UiText.Get("FilterPrompt_ErrorDateBetweenSyntax"),
            FilterPromptPlanError.BetweenSyntax => UiText.Get("FilterPrompt_ErrorBetweenSyntax"),
            FilterPromptPlanError.TextToMatch => UiText.Get("FilterPrompt_ErrorTextToMatch"),
            FilterPromptPlanError.ComparisonNumber => UiText.Get("FilterPrompt_ErrorComparisonNumber"),
            FilterPromptPlanError.DateFormat => UiText.Get("FilterPrompt_ErrorDateFormat"),
            _ => UiText.Get("MainWindowMessage_FilterUnsupportedCriterion")
        };

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
            _commandBus,
            initialRange,
            request => ApplyNamedRangeSelection(dlg, request))
        {
            Owner = this
        };
        dlg.ShowDialog();
        UpdateViewport();
    }

}
