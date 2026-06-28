using FreeX.Core.Model;
using SharedPivotFieldListPanePlan = FreeX.App.Presentation.PivotUI.PivotFieldListPanePlan;
using SharedPivotShowDetailsTarget = FreeX.App.Presentation.PivotUI.PivotShowDetailsTarget;
using SharedPivotUiPlanner = FreeX.App.Presentation.PivotUI.PivotUiPlanner;

namespace FreeX.App.Host;

public sealed record PivotFieldListItem(string Caption, bool IsChecked);

public sealed record PendingPivotLayoutUpdate(
    bool IsDeferred,
    string? AvailableFieldsSearchText,
    IReadOnlyList<PivotFieldListItem> Fields);

/// <summary>
/// WPF control-state adapter for the shared PivotUI planner. Keep pivot domain decisions in
/// <see cref="SharedPivotUiPlanner"/>; this facade only preserves existing Host call sites and list item
/// extraction for WPF controls.
/// </summary>
public static class PivotUiPlanner
{
    public static string FieldCaption(IReadOnlyList<string> headers, int sourceFieldIndex) =>
        SharedPivotUiPlanner.FieldCaption(headers, sourceFieldIndex);

    public static int? FindSourceFieldIndex(IReadOnlyList<string> headers, string? caption) =>
        SharedPivotUiPlanner.FindSourceFieldIndex(headers, caption);

    public static int? FindDataFieldIndex(PivotTableModel pivotTable, string? caption) =>
        SharedPivotUiPlanner.FindDataFieldIndex(pivotTable, caption);

    public static int? FindFieldSourceIndex(IReadOnlyList<string> headers, PivotTableModel pivotTable, string caption) =>
        SharedPivotUiPlanner.FindFieldSourceIndex(headers, pivotTable, caption);

    public static PivotTableModel? FindPivotTableForSelection(Sheet sheet, GridRange? selectedRange) =>
        SharedPivotUiPlanner.FindPivotTableForSelection(sheet, selectedRange);

    public static PivotTableModel? FindPivotTableContainingSelection(Sheet sheet, GridRange? selectedRange) =>
        SharedPivotUiPlanner.FindPivotTableContainingSelection(sheet, selectedRange);

    public static PivotTableModel? FindPivotTableContainingCell(Sheet sheet, CellAddress cell) =>
        SharedPivotUiPlanner.FindPivotTableContainingCell(sheet, cell);

    public static GridRange VisiblePivotRange(PivotTableModel pivotTable) =>
        SharedPivotUiPlanner.VisiblePivotRange(pivotTable);

    public static SharedPivotFieldListPanePlan CreateFieldListPanePlan(Sheet? sheet, GridRange? selectedRange) =>
        SharedPivotUiPlanner.CreateFieldListPanePlan(sheet, selectedRange);

    public static CellAddress? ReconcileSelectionAfterPivotResize(
        GridRange previousVisibleRange,
        GridRange updatedVisibleRange,
        GridRange? selectedRange) =>
        SharedPivotUiPlanner.ReconcileSelectionAfterPivotResize(previousVisibleRange, updatedVisibleRange, selectedRange);

    public static SharedPivotShowDetailsTarget? ResolveShowDetailsTarget(Sheet? sheet, GridRange? selectedRange) =>
        SharedPivotUiPlanner.ResolveShowDetailsTarget(sheet, selectedRange);

    public static Sheet ResolvePivotSourceSheet(Workbook workbook, Sheet fallbackSheet, PivotTableModel pivotTable) =>
        SharedPivotUiPlanner.ResolvePivotSourceSheet(workbook, fallbackSheet, pivotTable);

    public static int ChooseDefaultDataField(Sheet sheet, GridRange sourceRange) =>
        SharedPivotUiPlanner.ChooseDefaultDataField(sheet, sourceRange);

    public static GridRange DefaultTargetRange(Sheet sheet, GridRange sourceRange) =>
        SharedPivotUiPlanner.DefaultTargetRange(sheet, sourceRange);

    public static string GenerateUniquePivotTableName(Sheet sheet) =>
        SharedPivotUiPlanner.GenerateUniquePivotTableName(sheet);

    public static string NormalizePivotTableName(string? name) =>
        SharedPivotUiPlanner.NormalizePivotTableName(name);

    public static bool IsPivotTableNameAvailable(Workbook workbook, PivotTableModel targetPivotTable, string name) =>
        SharedPivotUiPlanner.IsPivotTableNameAvailable(workbook, targetPivotTable, name);

    public static GridRange ResolvePivotTableSelectionRange(PivotTableModel pivotTable) =>
        SharedPivotUiPlanner.ResolvePivotTableSelectionRange(pivotTable);

    public static bool TryCreateMovedTargetRange(
        PivotTableModel pivotTable,
        CellAddress targetStart,
        out GridRange targetRange) =>
        SharedPivotUiPlanner.TryCreateMovedTargetRange(pivotTable, targetStart, out targetRange);

    public static string UnquoteSheetName(string sheetName) =>
        SharedPivotUiPlanner.UnquoteSheetName(sheetName);

    public static PivotDataFieldModel CreateDefaultDataField(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        int sourceFieldIndex) =>
        SharedPivotUiPlanner.CreateDefaultDataField(sheet, pivotTable, headers, sourceFieldIndex);

    public static bool IsNumericSourceField(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex) =>
        SharedPivotUiPlanner.IsNumericSourceField(sheet, pivotTable, sourceFieldIndex);

    public static bool TryParseLabelFilter(string input, int sourceFieldIndex, out PivotLabelFilterModel filter) =>
        SharedPivotUiPlanner.TryParseLabelFilter(input, sourceFieldIndex, out filter);

    public static bool TryParseValueFilter(string input, int sourceFieldIndex, out PivotValueFilterModel filter) =>
        SharedPivotUiPlanner.TryParseValueFilter(input, sourceFieldIndex, out filter);

    public static string? ResolvePivotChartFieldButtonCaption(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        string fieldButton) =>
        SharedPivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivotTable, headers, fieldButton);

    public static PivotFieldModel FindExistingPivotField(PivotTableModel pivotTable, int sourceFieldIndex) =>
        SharedPivotUiPlanner.FindExistingPivotField(pivotTable, sourceFieldIndex);

    public static List<PivotFieldModel> SetFieldSelectedItems(
        IReadOnlyList<PivotFieldModel> fields,
        int sourceFieldIndex,
        IReadOnlyList<string>? selectedItems) =>
        SharedPivotUiPlanner.SetFieldSelectedItems(fields, sourceFieldIndex, selectedItems);

    public static string? GetFieldListCaption(object? item) =>
        item switch
        {
            string value when !string.IsNullOrWhiteSpace(value) => value,
            PivotFieldListItem field when !string.IsNullOrWhiteSpace(field.Caption) => field.Caption,
            _ => null
        };

    public static IReadOnlyList<PivotFieldListItem> FilterPivotFieldListItems(
        IEnumerable<PivotFieldListItem> fields,
        string? searchText) =>
        fields
            .Where(field => SharedPivotUiPlanner.FieldListCaptionMatchesSearch(field.Caption, searchText))
            .ToList();

    public static void InsertOrAppend<T>(List<T> items, T item, int index) =>
        SharedPivotUiPlanner.InsertOrAppend(items, item, index);
}
