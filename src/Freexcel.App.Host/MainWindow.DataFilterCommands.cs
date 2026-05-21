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
        var dialog = new SortDialog(columnChoices: SortDialog.BuildColumnChoices(sheet, range, hasHeaders: true)) { Owner = this };
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
            : new AutoFilterDialog(AutoFilterDropdownPlanner.CreateMenuPlan(sheet, new AutoFilterDropdownPlan(range, filterColOffset)));
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
        var dlg = new DataValidationDialog
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
                    return new SetDataValidationCommand(sheetId, rule);
                }))
            return;
        UpdateViewport();
    }

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
