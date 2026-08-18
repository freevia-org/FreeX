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
    /// Keeps invalid selections from opening the modal Custom Sort dialog. The actual policy lives
    /// in WorkbookSession and is also enforced by both shared sort execution methods.
    /// </summary>
    private bool TryRejectInvalidSortSelection()
    {
        SynchronizeWorkbookSessionSelection();
        if (_session.GetSelectedRangeSortError() is not { } error)
            return false;

        ShowCommandError(new CommandOutcome(false, error), "Sort");
        return true;
    }

    private bool TryExecuteWorksheetFilterCommand(
        Func<WorkbookCellEditResult> execute,
        string title)
    {
        SynchronizeWorkbookSessionSelection();
        var result = execute();
        return CompleteWorksheetSessionCommand(result, title);
    }

    private void SortAscButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(
                () => _session.SortSelectedRange(ascending: true),
                "Sort"))
            return;
        RecalculateAfterFilterOrSort();
        UpdateViewport();
    }

    private void SortDescButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(
                () => _session.SortSelectedRange(ascending: false),
                "Sort"))
            return;
        RecalculateAfterFilterOrSort();
        UpdateViewport();
    }

    /// <summary>
    /// Auto-detects whether the range looks like it has a header row, using the same heuristic as
    /// the shared quick-sort planner instead of always checking "My data has headers".
    /// </summary>
    private bool DetectSortDialogHasHeaders(GridRange range) =>
        _workbook.GetSheet(_currentSheetId) is { } sheet &&
        QuickSortRangePlanner.HasLikelyHeaderRow(sheet, range);

    private void SortCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } rawRange) return;
        if (TryRejectInvalidSortSelection()) return;

        // R142-services-sort-customdialog-1: resolve Excel's Sort Warning (expand to the whole
        // adjacent data block?) up front, exactly like Quick Sort/ribbon A-Z, so the dialog's
        // column/row/color/icon choices below are built from the range that will actually be
        // sorted -- not the raw (possibly narrower) selection, which would silently misalign the
        // dialog's column offsets against whatever wider range the warning later expanded into.
        var range = _session.ResolveSortRangeAfterAdjacentDataPrompt(rawRange);
        var sheet = _workbook.GetSheet(_currentSheetId);
        var hasHeaders = DetectSortDialogHasHeaders(range);
        var dialog = new SortDialog(
            columnChoices: SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: true, SortDialog.PlannerText),
            genericColumnChoices: SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: false, SortDialog.PlannerText),
            rowChoices: SortDialogPlanner.BuildRowChoices(range, SortDialog.PlannerText),
            colorChoices: SortDialogPlanner.BuildColorChoices(_workbook, sheet, range),
            cellColorChoices: SortDialogPlanner.BuildColorChoices(_workbook, sheet, range, SortOn.CellColor),
            fontColorChoices: SortDialogPlanner.BuildColorChoices(_workbook, sheet, range, SortOn.FontColor),
            hasHeaders: hasHeaders,
            iconWorkbook: _workbook,
            iconSheet: sheet,
            iconRange: range)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        var sortPlan = SortDialogPlanner.CreateCommandPlan(
            dialog.Levels,
            dialog.ResultOptions,
            dialog.ResultHasHeaders,
            SortDialog.PlannerText);

        if (!TryExecuteWorksheetLayout(
                () => _session.SortSelectedRange(sortPlan, range),
                "Sort"))
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

        if (!TryExecuteWorksheetFilterCommand(
                _session.ToggleSelectedRangeAutoFilter,
                "Filter"))
        {
            return;
        }

        _filterWorkflowSession.ResetAutoFilterState();
        RecalculateAfterFilterOrSort();
        UpdateFilterViewportAndStatusBar();
    }

    private bool TryExecuteAutoFilterMutation(WorksheetFilterMutationPlan plan)
    {
        if (!TryExecuteWorksheetFilterCommand(
                () => _session.ExecuteWorksheetFilterMutationPlan(plan),
                plan.HistoryLabel))
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

        if (!TryExecuteWorksheetFilterCommand(
                () => _session.ExecuteWorksheetFilterReapplyPlan(plan, "Reapply Filter"),
                "Reapply Filter"))
            return;

        RecalculateAfterFilterOrSort();
        UpdateFilterViewportAndStatusBar();
    }

    private void UpdateFilterViewportAndStatusBar()
    {
        UpdateViewport();
        RefreshStatusBar();
    }


    private bool ApplyAutoFilterDialogResult(GridRange range, uint filterColOffset, AutoFilterDialogResult result, string title)
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet)
            return false;

        var plan = _filterWorkflowSession.PlanDialogResult(
            sheet,
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

        // "Apply to all cells with the same settings" must still cover the selection the user just
        // made -- rule.AppliesTo/rule.AdditionalRanges, set by the caller from the live selection --
        // AND extend the new settings to every OTHER range on the sheet that shared the OLD rule's
        // settings, keeping each of those ranges' own footprint (AppliesTo + AdditionalRanges)
        // intact instead of collapsing every match onto the edited rule's single old AppliesTo with
        // an empty AdditionalRanges. The retarget commands run FIRST so SetDataValidationCommand's
        // own overlap-clearing (ClearOtherOverlappingRules) then correctly folds any of them the new
        // selection now also covers into the primary command below, while leaving genuinely disjoint
        // areas (e.g. an AdditionalRanges area the user didn't reselect) intact under the new
        // settings instead of dropping them.
        var commands = sheet.DataValidations
            .Where(candidate => candidate.HasSameSettings(existingRule))
            .Select(candidate => (IWorkbookCommand)new SetDataValidationCommand(
                sheetId,
                rule.CloneForRanges(candidate.AppliesTo, candidate.AdditionalRanges, candidate.Id)))
            .ToList();

        commands.Add(new SetDataValidationCommand(sheetId, rule));

        return new CompositeWorkbookCommand("Data Validation", commands);
    }

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
        if (!TryExecuteWorksheetFilterCommand(
                () => _session.ExecuteWorksheetFilterCommand(
                    range,
                    currentRange => _filterWorkflowSession.CreateClearAllPlan(sheet, currentRange).Command),
                "Clear Filter"))
            return;
        _filterWorkflowSession.RecordSuccessfulClearAll(clearPlan);
        RecalculateAfterFilterOrSort();
        UpdateFilterViewportAndStatusBar();
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
