using System;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void SortAscButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: true)))
            return;
        UpdateViewport();
    }

    private void SortDescButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: false)))
            return;
        UpdateViewport();
    }

    private void SortCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = new SortDialog(
            columnChoices: SortDialog.BuildColumnChoices(sheet, range, hasHeaders: true),
            genericColumnChoices: SortDialog.BuildColumnChoices(sheet, range, hasHeaders: false))
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        var keys = dialog.ResultSortKeys;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, SortDialog.ExcludeHeaderRow(currentRange, dialog.ResultHasHeaders), keys)))
            return;
        UpdateViewport();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        ApplyFilterPrompt(range, filterColOffset: 0);
    }

    private void ApplyFilterPrompt(GridRange range, uint filterColOffset)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = sheet is null
            ? new AutoFilterDialog(Array.Empty<AutoFilterChecklistItem>())
            : new AutoFilterDialog(AutoFilterDropdownPlanner.CreateMenuPlan(_workbook, sheet, new AutoFilterDropdownPlan(range, filterColOffset)));
        dialog.Owner = this;
        dialog.Title = "Filter";
        if (dialog.ShowDialog() != true) return;

        if (!ApplyAutoFilterDialogResult(range, filterColOffset, dialog.Result, "Filter"))
            return;
        UpdateViewport();
    }

    private bool ApplyAutoFilterDialogResult(GridRange range, uint filterColOffset, AutoFilterDialogResult result, string title)
    {
        if (result.SortDirection != AutoFilterSortDirection.None)
        {
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Sort",
                    range,
                    currentRange => new SortCommand(_currentSheetId, currentRange, filterColOffset, result.SortDirection == AutoFilterSortDirection.Ascending)))
                return false;
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
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    label,
                    range,
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
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filterText) &&
            (filterText.StartsWith("top:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("toppercent:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("bottompercent:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("bottom:", StringComparison.OrdinalIgnoreCase)))
        {
            if (!FilterInputParser.TryParseTopBottom(value, out var count, out var top, out var percent, out var error))
            {
                MessageBox.Show(error ?? "Enter top:n, bottom:n, toppercent:n, or bottompercent:n.",
                    title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => percent
                        ? TopBottomFilterCommand.Percent(_currentSheetId, currentRange, filterColOffset, count, top)
                        : new TopBottomFilterCommand(_currentSheetId, currentRange, filterColOffset, count, top)))
                return false;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filterText) &&
            FilterInputParser.TryParseAverage(value, out var aboveAverage))
        {
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => new AverageFilterCommand(_currentSheetId, currentRange, filterColOffset, aboveAverage)))
                return false;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filterText) &&
            (filterText.Equals("blank", StringComparison.OrdinalIgnoreCase) ||
             filterText.Equals("nonblank", StringComparison.OrdinalIgnoreCase) ||
             filterText.Equals("non-blank", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("date=", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("date>", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("date<", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("datebetween:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("contains:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("notcontains:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("begins:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("ends:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("text=", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("text<>", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("and:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("or:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("between:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith('>') ||
             filterText.StartsWith('<') ||
             filterText.StartsWith('=')))
        {
            if (!FilterInputParser.TryParseCriterion(value, out var criterion, out var error) || criterion is null)
            {
                MessageBox.Show(error ?? "Enter a supported filter criterion.",
                    title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => new FilterConditionCommand(_currentSheetId, currentRange, filterColOffset, criterion)))
                return false;
            return true;
        }

        if (string.IsNullOrWhiteSpace(filterText) && result.SelectedValues.Count == 0)
        {
            MessageBox.Show("Select at least one filter item.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var allowedValues = FilterInputParser.ParseAllowedValues(value);
        if (allowedValues.Count == 0)
            allowedValues = result.SelectedValues;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Filter",
                range,
                currentRange => new FilterCommand(_currentSheetId, currentRange, filterColOffset, allowedValues: allowedValues)))
            return false;

        return true;
    }

    private void CfRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range first.", "CF Rule");
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

        if (!TryExecuteGroupedSheetCommand(
                "Conditional Formatting",
                sheetId => new ApplyConditionalFormatCommand(sheetId, GroupedSheetRangePlanner.CloneConditionalFormatForSheet(cf, sheetId))))
            return;
        UpdateViewport();
    }

    private void ValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range first.", "Data Validation");
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        var existingRule = sheet is null
            ? null
            : DataValidationService.GetApplicable(sheet, range.Start).FirstOrDefault();
        var dlg = new DataValidationDialog(existingRule)
        {
            Owner = this,
            SelectionSource = DataValidationService.FormatListSourceRange(range, sheet?.Name, sheet?.Name)
        };
        if (dlg.ShowDialog() != true) return;

        if (dlg.ClearRequested)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Clear Data Validation",
                    sheetId => new ClearDataValidationCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId))))
                return;

            UpdateViewport();
            return;
        }

        if (dlg.Result == null) return;

        var dv = dlg.Result;
        dv.AppliesTo = range;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Data Validation",
                sheetId =>
                {
                    var rule = GroupedSheetRangePlanner.CloneDataValidationForSheet(dv, sheetId);
                    rule.AppliesTo = GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId);
                    return CreateDataValidationCommand(
                        sheetId,
                        rule,
                        existingRule,
                        dlg.ApplyToSameSettings);
                }))
            return;
        UpdateViewport();
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
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Filter",
                range,
                currentRange => new FilterCommand(_currentSheetId, currentRange, filterColOffset: 0, allowedValues: [])))
            return;
        UpdateViewport();
    }

    private void NamedRangesButton_Click(object sender, RoutedEventArgs e)
    {
        var initialRange = SheetGrid.SelectedRange;
        var dlg = new NamedRangeDialog(_workbook, _commandBus, initialRange)
        {
            Owner = this
        };
        dlg.ShowDialog();
        UpdateViewport();
    }
}
