namespace FreeX.Core.Model;

/// <summary>
/// Owns the canonical deep clone for the mutable worksheet AutoFilter metadata graph. Commands use
/// the same implementation for undo/custom-view snapshots and may override only a column's relative
/// id while shifting worksheet columns.
/// </summary>
internal static class WorksheetAutoFilterCloner
{
    public static WorksheetAutoFilterModel? Clone(WorksheetAutoFilterModel? autoFilter)
    {
        if (autoFilter is null)
            return null;

        var clone = new WorksheetAutoFilterModel(autoFilter.Reference, autoFilter.NativeXml)
        {
            NativeAttributes = CloneDictionary(autoFilter.NativeAttributes),
            NativeChildXmls = autoFilter.NativeChildXmls?.ToArray()
        };
        clone.FilterColumns.EnsureCapacity(autoFilter.FilterColumns.Count);
        foreach (var column in autoFilter.FilterColumns)
            clone.FilterColumns.Add(CloneColumn(column));
        return clone;
    }

    public static WorksheetAutoFilterColumnModel CloneColumn(
        WorksheetAutoFilterColumnModel column,
        int? columnId = null) =>
        new(
            columnId ?? column.ColumnId,
            column.Values.ToArray(),
            column.IncludeBlank,
            CloneCustomFilters(column.CustomFilters),
            column.CustomFiltersAnd,
            column.CustomFiltersAndRaw,
            CloneDictionary(column.NativeCustomFiltersAttributes),
            CloneTop10(column.Top10),
            CloneDynamicFilter(column.DynamicFilter),
            CloneColorFilter(column.ColorFilter),
            CloneIconFilter(column.IconFilter),
            CloneDateGroups(column.DateGroups),
            CloneDictionary(column.NativeFiltersAttributes),
            column.NativeFilterXmls.ToArray(),
            CloneDictionary(column.NativeAttributes));

    private static WorksheetAutoFilterCustomFilterModel[] CloneCustomFilters(
        IReadOnlyList<WorksheetAutoFilterCustomFilterModel> filters)
    {
        if (filters.Count == 0)
            return [];

        var clones = new WorksheetAutoFilterCustomFilterModel[filters.Count];
        for (var i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            clones[i] = new WorksheetAutoFilterCustomFilterModel(
                filter.Operator,
                filter.Value,
                CloneDictionary(filter.NativeAttributes));
        }

        return clones;
    }

    private static WorksheetAutoFilterDateGroupItemModel[] CloneDateGroups(
        IReadOnlyList<WorksheetAutoFilterDateGroupItemModel> dateGroups)
    {
        if (dateGroups.Count == 0)
            return [];

        var clones = new WorksheetAutoFilterDateGroupItemModel[dateGroups.Count];
        for (var i = 0; i < dateGroups.Count; i++)
        {
            var dateGroup = dateGroups[i];
            clones[i] = dateGroup with
            {
                NativeAttributes = CloneDictionary(dateGroup.NativeAttributes)
            };
        }

        return clones;
    }

    private static WorksheetAutoFilterTop10Model? CloneTop10(WorksheetAutoFilterTop10Model? top10) =>
        top10 is null ? null : top10 with { NativeAttributes = CloneDictionary(top10.NativeAttributes) };

    private static WorksheetAutoFilterDynamicFilterModel? CloneDynamicFilter(
        WorksheetAutoFilterDynamicFilterModel? dynamicFilter) =>
        dynamicFilter is null
            ? null
            : dynamicFilter with { NativeAttributes = CloneDictionary(dynamicFilter.NativeAttributes) };

    private static WorksheetAutoFilterColorFilterModel? CloneColorFilter(
        WorksheetAutoFilterColorFilterModel? colorFilter) =>
        colorFilter is null
            ? null
            : colorFilter with { NativeAttributes = CloneDictionary(colorFilter.NativeAttributes) };

    private static WorksheetAutoFilterIconFilterModel? CloneIconFilter(
        WorksheetAutoFilterIconFilterModel? iconFilter) =>
        iconFilter is null
            ? null
            : iconFilter with { NativeAttributes = CloneDictionary(iconFilter.NativeAttributes) };

    private static IReadOnlyDictionary<string, string>? CloneDictionary(
        IReadOnlyDictionary<string, string>? source) =>
        source is null ? null : new Dictionary<string, string>(source, StringComparer.Ordinal);
}
