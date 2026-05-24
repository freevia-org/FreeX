using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void PivotGroupFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable);
        if (sourceIndex is null)
            return;

        var currentField = PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value);
        var dialog = new PivotFieldGroupingDialog(headers, currentField) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotGroupingResult(pivotTable, dialog.Result);
    }

    private void PivotUngroupFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable);
        if (sourceIndex is null)
            return;

        ApplyPivotGroupingResult(
            pivotTable,
            PivotFieldGroupingDialog.CreateResult(
                PivotUiPlanner.FieldCaption(headers, sourceIndex.Value),
                sourceIndex.Value,
                PivotFieldGrouping.None,
                groupStart: null,
                groupEnd: null,
                groupInterval: null,
                ungroup: true));
    }

    private void PivotCalculatedFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out _, out var pivotTable))
            return;

        var dialog = new PivotCalculatedFieldDialog { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.Name) ||
            string.IsNullOrWhiteSpace(dialog.Result.Formula))
        {
            return;
        }

        var calculatedFields = pivotTable.CalculatedFields
            .Where(field => !string.Equals(field.Name, dialog.Result.Name, StringComparison.CurrentCultureIgnoreCase))
            .Append(dialog.Result.ToModel())
            .ToList();

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            calculatedFields,
            pivotTable.CalculatedItems.ToList());
    }

    private void PivotCalculatedItemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable) ?? 0;
        var dialog = new PivotCalculatedItemDialog(headers, sourceIndex) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.Name) ||
            string.IsNullOrWhiteSpace(dialog.Result.Formula))
        {
            return;
        }

        var calculatedItems = pivotTable.CalculatedItems
            .Where(item =>
                item.SourceFieldIndex != dialog.Result.SourceFieldIndex ||
                !string.Equals(item.Name, dialog.Result.Name, StringComparison.CurrentCultureIgnoreCase))
            .Append(dialog.Result.ToModel())
            .ToList();

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.CalculatedFields.ToList(),
            calculatedItems);
    }

    private void ApplyPivotGroupingResult(PivotTableModel pivotTable, PivotFieldGroupingDialogResult result)
    {
        var groupedField = new PivotFieldModel(
            result.SourceFieldIndex,
            Grouping: result.Ungroup ? PivotFieldGrouping.None : result.Grouping,
            GroupStart: result.Ungroup ? null : result.GroupStart,
            GroupEnd: result.Ungroup ? null : result.GroupEnd,
            GroupInterval: result.Ungroup ? null : result.GroupInterval);
        var fieldAlreadyInLayout =
            pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                .Any(field => field.SourceFieldIndex == groupedField.SourceFieldIndex);
        var rowFields = fieldAlreadyInLayout
            ? ReplacePivotField(pivotTable.RowFields, groupedField)
            : pivotTable.RowFields.Append(groupedField).ToList();
        var columnFields = ReplacePivotField(pivotTable.ColumnFields, groupedField);
        var pageFields = ReplacePivotField(pivotTable.PageFields, groupedField);

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            rowFields,
            columnFields,
            pageFields,
            pivotTable.CalculatedFields.ToList(),
            pivotTable.CalculatedItems.ToList());
    }

    private void ApplyPivotAdvancedConfiguration(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableCalculatedItemsCommand(
                    _currentSheetId,
                    pivotTable.Name,
                    rowFields,
                    columnFields,
                    pageFields,
                    calculatedFields,
                    calculatedItems),
                "PivotTable Calculations"))
            return;

        _pendingPivotLayout = null;
        RefreshPivotFieldListPane();
        UpdateViewport();
    }

    private int? ResolveSelectedPivotSourceField(IReadOnlyList<string> headers, PivotTableModel pivotTable)
    {
        var selected = GetSelectedPivotFieldListItem();
        return PivotUiPlanner.FindFieldSourceIndex(headers, pivotTable, selected ?? "")
               ?? pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                   .FirstOrDefault()
                   ?.SourceFieldIndex;
    }

    private static List<PivotFieldModel> ReplacePivotField(
        IReadOnlyList<PivotFieldModel> fields,
        PivotFieldModel replacement) =>
        fields
            .Select(field => field.SourceFieldIndex == replacement.SourceFieldIndex
                ? replacement
                : field)
            .ToList();
}
