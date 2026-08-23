using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal interface IPivotTableCommandStateSnapshot
{
    void Restore(PivotTableModel pivotTable);
}

internal sealed record PivotFilterStateSnapshot(
    IReadOnlyList<PivotFieldModel> RowFields,
    IReadOnlyList<PivotFieldModel> ColumnFields,
    IReadOnlyList<PivotFieldModel> PageFields,
    IReadOnlyList<PivotLabelFilterModel> LabelFilters,
    IReadOnlyList<PivotValueFilterModel> ValueFilters,
    IReadOnlyList<PivotSortModel> Sorts,
    GridRange? LastRenderedRange) : IPivotTableCommandStateSnapshot
{
    internal static PivotFilterStateSnapshot Capture(PivotTableModel pivotTable) =>
        new(
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.LabelFilters.ToList(),
            pivotTable.ValueFilters.ToList(),
            pivotTable.Sorts.ToList(),
            pivotTable.LastRenderedRange);

    public void Restore(PivotTableModel pivotTable)
    {
        PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
        PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
        PivotTableCommandCollections.Replace(pivotTable.LabelFilters, LabelFilters);
        PivotTableCommandCollections.Replace(pivotTable.ValueFilters, ValueFilters);
        PivotTableCommandCollections.Replace(pivotTable.Sorts, Sorts);
        pivotTable.LastRenderedRange = LastRenderedRange;
    }
}

internal sealed record PivotLayoutStateSnapshot(
    IReadOnlyList<PivotFieldModel> RowFields,
    IReadOnlyList<PivotFieldModel> ColumnFields,
    IReadOnlyList<PivotFieldModel> PageFields,
    IReadOnlyList<PivotDataFieldModel> DataFields,
    IReadOnlyList<PivotLabelFilterModel> LabelFilters,
    IReadOnlyList<PivotValueFilterModel> ValueFilters,
    IReadOnlyList<PivotSortModel> Sorts,
    GridRange? LastRenderedRange) : IPivotTableCommandStateSnapshot
{
    internal static PivotLayoutStateSnapshot Capture(PivotTableModel pivotTable) =>
        new(
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.DataFields.ToList(),
            pivotTable.LabelFilters.ToList(),
            pivotTable.ValueFilters.ToList(),
            pivotTable.Sorts.ToList(),
            pivotTable.LastRenderedRange);

    public void Restore(PivotTableModel pivotTable)
    {
        PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
        PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
        PivotTableCommandCollections.Replace(pivotTable.DataFields, DataFields);
        PivotTableCommandCollections.Replace(pivotTable.LabelFilters, LabelFilters);
        PivotTableCommandCollections.Replace(pivotTable.ValueFilters, ValueFilters);
        PivotTableCommandCollections.Replace(pivotTable.Sorts, Sorts);
        pivotTable.LastRenderedRange = LastRenderedRange;
    }
}

internal sealed record PivotViewStateSnapshot(
    IReadOnlyList<PivotLabelFilterModel> LabelFilters,
    IReadOnlyList<PivotValueFilterModel> ValueFilters,
    IReadOnlyList<PivotSortModel> Sorts,
    GridRange? LastRenderedRange) : IPivotTableCommandStateSnapshot
{
    internal static PivotViewStateSnapshot Capture(PivotTableModel pivotTable) =>
        new(
            pivotTable.LabelFilters.ToList(),
            pivotTable.ValueFilters.ToList(),
            pivotTable.Sorts.ToList(),
            pivotTable.LastRenderedRange);

    public void Restore(PivotTableModel pivotTable)
    {
        PivotTableCommandCollections.Replace(pivotTable.LabelFilters, LabelFilters);
        PivotTableCommandCollections.Replace(pivotTable.ValueFilters, ValueFilters);
        PivotTableCommandCollections.Replace(pivotTable.Sorts, Sorts);
        pivotTable.LastRenderedRange = LastRenderedRange;
    }
}

internal sealed record PivotCalculatedItemsStateSnapshot(
    IReadOnlyList<PivotFieldModel> RowFields,
    IReadOnlyList<PivotFieldModel> ColumnFields,
    IReadOnlyList<PivotFieldModel> PageFields,
    IReadOnlyList<PivotCalculatedFieldModel> CalculatedFields,
    IReadOnlyList<PivotCalculatedItemModel> CalculatedItems,
    GridRange? LastRenderedRange) : IPivotTableCommandStateSnapshot
{
    internal static PivotCalculatedItemsStateSnapshot Capture(PivotTableModel pivotTable) =>
        new(
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.CalculatedFields.ToList(),
            pivotTable.CalculatedItems.ToList(),
            pivotTable.LastRenderedRange);

    public void Restore(PivotTableModel pivotTable)
    {
        PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
        PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
        PivotTableCommandCollections.Replace(pivotTable.CalculatedFields, CalculatedFields);
        PivotTableCommandCollections.Replace(pivotTable.CalculatedItems, CalculatedItems);
        pivotTable.LastRenderedRange = LastRenderedRange;
    }
}

internal sealed record PivotFieldLayoutStateSnapshot(
    IReadOnlyList<PivotFieldModel> RowFields,
    IReadOnlyList<PivotFieldModel> ColumnFields,
    IReadOnlyList<PivotFieldModel> PageFields,
    GridRange? LastRenderedRange) : IPivotTableCommandStateSnapshot
{
    internal static PivotFieldLayoutStateSnapshot Capture(PivotTableModel pivotTable) =>
        new(
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.LastRenderedRange);

    public void Restore(PivotTableModel pivotTable)
    {
        PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
        PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
        pivotTable.LastRenderedRange = LastRenderedRange;
    }
}

internal sealed record PivotTableTargetStateSnapshot(
    Sheet Sheet,
    PivotTableModel PivotTable,
    PivotFieldLayoutStateSnapshot State)
{
    internal static PivotTableTargetStateSnapshot Capture(Sheet sheet, PivotTableModel pivotTable) =>
        new(sheet, pivotTable, PivotFieldLayoutStateSnapshot.Capture(pivotTable));

    internal void Restore() => State.Restore(PivotTable);
}
