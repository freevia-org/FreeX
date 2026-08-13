using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        clone.FilterColumns.AddRange(autoFilter.FilterColumns.Select(column => CloneColumn(column)));
        return clone;
    }

    public static WorksheetAutoFilterColumnModel CloneColumn(
        WorksheetAutoFilterColumnModel column,
        int? columnId = null) =>
        new(
            columnId ?? column.ColumnId,
            column.Values.ToArray(),
            column.IncludeBlank,
            column.CustomFilters.Select(CloneCustomFilter).ToArray(),
            column.CustomFiltersAnd,
            column.CustomFiltersAndRaw,
            CloneDictionary(column.NativeCustomFiltersAttributes),
            CloneTop10(column.Top10),
            CloneDynamicFilter(column.DynamicFilter),
            CloneColorFilter(column.ColorFilter),
            CloneIconFilter(column.IconFilter),
            column.DateGroups.Select(CloneDateGroup).ToArray(),
            CloneDictionary(column.NativeFiltersAttributes),
            column.NativeFilterXmls.ToArray(),
            CloneDictionary(column.NativeAttributes));

    private static WorksheetAutoFilterCustomFilterModel CloneCustomFilter(
        WorksheetAutoFilterCustomFilterModel filter) =>
        new(filter.Operator, filter.Value, CloneDictionary(filter.NativeAttributes));

    private static WorksheetAutoFilterDateGroupItemModel CloneDateGroup(
        WorksheetAutoFilterDateGroupItemModel dateGroup) =>
        dateGroup with { NativeAttributes = CloneDictionary(dateGroup.NativeAttributes) };

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
