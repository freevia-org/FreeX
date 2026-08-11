using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void PivotGroupFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = PivotApplication.ReadSourceHeaders(new PivotApplicationTarget(sheet, pivotTable));
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

        var headers = PivotApplication.ReadSourceHeaders(new PivotApplicationTarget(sheet, pivotTable));
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable);
        if (sourceIndex is null)
            return;

        ApplyPivotGroupingResult(
            pivotTable,
            PivotGroupFieldPlanner.CreateSubmission(
                PivotUiPlanner.FieldCaption(headers, sourceIndex.Value),
                sourceIndex.Value,
                PivotFieldGrouping.None,
                ungroup: true,
                start: null,
                end: null,
                interval: null));
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

        var headers = PivotApplication.ReadSourceHeaders(new PivotApplicationTarget(sheet, pivotTable));
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

    private void ApplyPivotGroupingResult(PivotTableModel pivotTable, PivotGroupFieldSubmission submission)
    {
        var layout = PivotGroupFieldPlanner.BuildLayout(pivotTable, submission.Field);

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            layout.RowFields,
            layout.ColumnFields,
            layout.PageFields,
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
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null ||
            !ApplyPivotApplicationPlan(
                PivotApplication.PlanCalculatedConfiguration(
                    new PivotApplicationTarget(sheet, pivotTable),
                    rowFields,
                    columnFields,
                    pageFields,
                    calculatedFields,
                    calculatedItems),
                "PivotTable Calculations"))
            return;

        _pendingPivotLayout = null;
    }

    private int? ResolveSelectedPivotSourceField(IReadOnlyList<string> headers, PivotTableModel pivotTable)
    {
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindFieldSourceIndex(headers, pivotTable, selected ?? "");
        if (sourceIndex is not null)
            return sourceIndex;

        foreach (var field in pivotTable.RowFields)
            return field.SourceFieldIndex;
        foreach (var field in pivotTable.ColumnFields)
            return field.SourceFieldIndex;
        foreach (var field in pivotTable.PageFields)
            return field.SourceFieldIndex;

        return null;
    }
}
