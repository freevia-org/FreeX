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
    private GridRange? _lastAutoFilterRange;

    /// <summary>
    /// R65-services-autofilter-6-2: remembers the command factory for EVERY currently active
    /// AutoFilter column, keyed by that column's ABSOLUTE index (range.Start.Col + filterColOffset)
    /// -- not just a single "last applied" slot. Excel's Data &gt; Reapply re-evaluates every active
    /// filter criterion on the sheet, so <see cref="ReapplyAutoFilter"/> must be able to rebuild and
    /// re-run each column's own mechanism (value list, Top 10/Above-Average, custom condition, or
    /// color) together, not just whichever one happened to be applied most recently. A later call
    /// for the SAME column intentionally overwrites that column's own entry (Excel allows only one
    /// active AutoFilter criterion per column); a different column's entry is left untouched. Sort
    /// commands are never inserted here (see <see cref="TryExecuteAutoFilterSortCommand"/>) so a
    /// last-used Sort can never be replayed as "reapply filter".
    /// </summary>
    private readonly Dictionary<uint, Func<GridRange, IWorkbookCommand>> _activeAutoFilterColumnFactories = new();

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
            hasHeaders: hasHeaders)
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

        ClearRememberedAutoFilterCommand();
        RecalculateAfterFilterOrSort();
        UpdateFilterViewportAndStatusBar();
    }

    /// <summary>Back-compat entry point for callers with no distinct per-column identity of their
    /// own (e.g. screenshot-tour scripts driving a single-column scenario, and reflection-based
    /// tests that resolve this method by name) -- remembers the factory under offset 0 of
    /// <paramref name="range"/>. Genuine multi-column call sites in this file use
    /// <see cref="TryExecuteRememberedAutoFilterColumnCommand"/> (a distinctly-named method, so
    /// name-only reflection lookups of THIS method stay unambiguous) so each column keeps its own
    /// entry.</summary>
    private bool TryExecuteRememberedAutoFilterCommand(
        string title,
        GridRange range,
        Func<GridRange, IWorkbookCommand> createCommand) =>
        TryExecuteRememberedAutoFilterColumnCommand(title, range, filterColOffset: 0, createCommand);

    private bool TryExecuteRememberedAutoFilterColumnCommand(
        string title,
        GridRange range,
        uint filterColOffset,
        Func<GridRange, IWorkbookCommand> createCommand)
    {
        if (!TryExecuteRepeatableCurrentRangeCommand(title, range, _ => createCommand(range)))
            return false;

        _lastAutoFilterRange = range;
        _activeAutoFilterColumnFactories[range.Start.Col + filterColOffset] = createCommand;
        RecalculateAfterFilterOrSort();
        return true;
    }

    /// <summary>
    /// R65-services-autofilter-6-2: executes a Sort command triggered from the AutoFilter dropdown
    /// WITHOUT remembering it in <see cref="_activeAutoFilterColumnFactories"/> -- Data &gt; Reapply
    /// must only ever re-run active FILTER criteria, never replay a one-off Sort.
    /// </summary>
    private bool TryExecuteAutoFilterSortCommand(
        string title,
        GridRange range,
        Func<GridRange, IWorkbookCommand> createCommand)
    {
        if (!TryExecuteRepeatableCurrentRangeCommand(title, range, _ => createCommand(range)))
            return false;

        _lastAutoFilterRange = range;
        RecalculateAfterFilterOrSort();
        return true;
    }

    /// <summary>
    /// R65-services-autofilter-6-2: Data &gt; Reapply must re-evaluate EVERY currently active
    /// AutoFilter column criterion against the sheet's current data, not just whichever column's
    /// filter happened to be applied most recently. Rebuild one fresh command per remembered column
    /// (see <see cref="_activeAutoFilterColumnFactories"/>), in column order, and run them together
    /// as a single undoable operation.
    /// </summary>
    private void ReapplyAutoFilter()
    {
        if (_lastAutoFilterRange is not { } range || _activeAutoFilterColumnFactories.Count == 0)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_ReapplyFilterNoFilter"),
                UiText.Get("MainWindowMessage_ReapplyFilterTitle"));
            return;
        }

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Reapply Filter",
                range,
                _ => BuildReapplyAllActiveAutoFilterColumnsCommand(range)))
            return;

        RecalculateAfterFilterOrSort();
        RestoreAutoFilterRangeSelection(range);
        UpdateFilterViewportAndStatusBar();
    }

    private IWorkbookCommand BuildReapplyAllActiveAutoFilterColumnsCommand(GridRange range)
    {
        var columnCommands = _activeAutoFilterColumnFactories
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value(range))
            .ToList();

        return columnCommands.Count == 1
            ? columnCommands[0]
            : new CompositeWorkbookCommand("Reapply Filter", columnCommands);
    }

    private void ClearRememberedAutoFilterCommand()
    {
        _lastAutoFilterRange = null;
        _activeAutoFilterColumnFactories.Clear();
    }

    /// <summary>
    /// R65-services-autofilter-6-2: clears only ONE column's remembered filter factory (e.g. that
    /// column's dropdown "Clear Filter" action), leaving every OTHER active column's remembered
    /// filter intact so <see cref="ReapplyAutoFilter"/> still re-evaluates them.
    /// </summary>
    private void ClearRememberedAutoFilterColumn(uint absoluteColumn) =>
        _activeAutoFilterColumnFactories.Remove(absoluteColumn);

    private void UpdateFilterViewportAndStatusBar()
    {
        UpdateViewport();
        RefreshStatusBar();
    }

    private bool ApplyAutoFilterDialogResult(GridRange range, uint filterColOffset, AutoFilterDialogResult result, string title)
    {
        if (result.Action == AutoFilterDialogAction.ClearFilter)
        {
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Clear Filter",
                    range,
                    _ => new FilterCommand(_currentSheetId, range, filterColOffset, allowedValues: [])))
                return false;
            ClearRememberedAutoFilterColumn(range.Start.Col + filterColOffset);
            RecalculateAfterFilterOrSort();
            RestoreAutoFilterRangeSelection(range);
            return true;
        }

        if (result.SortDirection != AutoFilterSortDirection.None)
        {
            if (!TryExecuteAutoFilterSortCommand(
                    "Sort",
                    range,
                    currentRange => new SortCommand(_currentSheetId, currentRange, filterColOffset, result.SortDirection == AutoFilterSortDirection.Ascending)))
                return false;
            RestoreAutoFilterRangeSelection(range);
            return true;
        }

        var value = result.CriteriaText;
        var filterText = value.TrimStart();
        if (result.ColorFilter is { } colorFilter)
        {
            var label = colorFilter.Kind switch
            {
                AutoFilterColorFilterKind.FontColor => "Filter by Font Color",
                AutoFilterColorFilterKind.NoFill => "Filter by No Fill",
                _ => "Filter by Cell Color"
            };
            if (!TryExecuteRememberedAutoFilterColumnCommand(
                    label,
                    range,
                    filterColOffset,
                    currentRange => colorFilter.Kind switch
                    {
                        AutoFilterColorFilterKind.FontColor when colorFilter.Color is { } fontColor =>
                            new CellFontColorFilterCommand(_currentSheetId, currentRange, filterColOffset, fontColor),
                        AutoFilterColorFilterKind.NoFill =>
                            new CellNoFillColorFilterCommand(_currentSheetId, currentRange, filterColOffset),
                        AutoFilterColorFilterKind.CellFillColor when colorFilter.Color is { } fillColor =>
                            new CellFillColorFilterCommand(_currentSheetId, currentRange, filterColOffset, fillColor),
                        _ => new FilterCommand(_currentSheetId, currentRange, filterColOffset, [])
                    }))
                return false;
            RestoreAutoFilterRangeSelection(range);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filterText))
        {
            if (!FilterPromptPlanner.TryPlan(value, out var promptPlan, out var promptError) || promptPlan is null)
            {
                _messageService.ShowWarning(
                    FormatFilterPromptPlanError(promptError),
                    title);
                return false;
            }

            if (!TryExecuteRememberedAutoFilterColumnCommand(
                    "Filter",
                    range,
                    filterColOffset,
                    currentRange => promptPlan.CreateCommand(_currentSheetId, currentRange, filterColOffset)))
                return false;
            RestoreAutoFilterRangeSelection(range);
            return true;
        }

        if (string.IsNullOrWhiteSpace(filterText) && result.SelectedValues.Count == 0)
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_FilterSelectAtLeastOneItem"),
                title);
            return false;
        }

        var allowedValues = result.SelectedValues.Count > 0
            ? result.SelectedValues
            : FilterInputParser.ParseAllowedValues(value);

        if (!TryExecuteRememberedAutoFilterColumnCommand(
                "Filter",
                range,
                filterColOffset,
                currentRange => new FilterCommand(_currentSheetId, currentRange, filterColOffset, allowedValues: allowedValues)))
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

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Clear Filter",
                range,
                currentRange => BuildClearAllValueFiltersCommand(currentRange)))
            return;
        ClearRememberedAutoFilterCommand();
        RecalculateAfterFilterOrSort();
        RestoreAutoFilterRangeSelection(range);
        UpdateFilterViewportAndStatusBar();
    }

    /// <summary>
    /// R33-commands-autofilter-slicer-2: the "Clear Filter" command must clear EVERY column's active
    /// filter in <paramref name="range"/>, not just the first (offset 0). A single FilterCommand
    /// only removes one column's entry from sheet.ActiveValueFilterColumns; if more than one column
    /// carries an active filter (e.g. B only, or A and C), clearing offset 0 alone leaves the other
    /// column(s) filtered and the same rows hidden. Mirror the per-column dropdown's own clear (a
    /// FilterCommand with empty allowedValues for that column's offset), but issue one per active
    /// column found in the range so every filter is actually removed.
    ///
    /// R34-meta-1: value-list filters register in sheet.ActiveValueFilterColumns, but Top10/
    /// Above-Average filters (TopBottomFilterCommand/AverageFilterCommand) only register in
    /// sheet.ColumnFilterOwnedRows, never in ActiveValueFilterColumns. Walking ActiveValueFilterColumns
    /// alone misses those columns, so their hidden rows survive "Clear Filter". Union both dictionaries'
    /// keys (ColumnFilterOwnedRows is the superset of all filtered columns, of every kind) so every
    /// active filter column is found and cleared.
    /// </summary>
    private IWorkbookCommand BuildClearAllValueFiltersCommand(GridRange range)
    {
        var offsets = new List<uint>();
        if (_workbook.GetSheet(_currentSheetId) is { } sheet)
        {
            var activeCols = new HashSet<uint>(sheet.ActiveValueFilterColumns.Keys);
            activeCols.UnionWith(sheet.ColumnFilterOwnedRows.Keys);

            foreach (var col in activeCols.OrderBy(c => c))
            {
                if (col >= range.Start.Col && col <= range.End.Col)
                    offsets.Add(col - range.Start.Col);
            }
        }

        if (offsets.Count == 0)
            offsets.Add(0);

        if (offsets.Count == 1)
            return new FilterCommand(_currentSheetId, range, offsets[0], allowedValues: []);

        var commands = new List<IWorkbookCommand>(offsets.Count);
        foreach (var offset in offsets)
            commands.Add(new FilterCommand(_currentSheetId, range, offset, allowedValues: []));

        return new CompositeWorkbookCommand("Clear Filter", commands);
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
