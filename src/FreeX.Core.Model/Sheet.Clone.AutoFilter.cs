namespace FreeX.Core.Model;

public sealed partial class Sheet
{
    private static WorksheetAutoFilterModel? CloneAutoFilter(WorksheetAutoFilterModel? autoFilter)
    {
        if (autoFilter is null)
            return null;

        var clone = new WorksheetAutoFilterModel(autoFilter.Reference, autoFilter.NativeXml)
        {
            NativeAttributes = autoFilter.NativeAttributes is null
                ? null
                : new Dictionary<string, string>(autoFilter.NativeAttributes, StringComparer.Ordinal),
            NativeChildXmls = autoFilter.NativeChildXmls?.ToArray()
        };
        clone.FilterColumns.AddRange(autoFilter.FilterColumns.Select(CloneAutoFilterColumn));
        return clone;
    }

    private static WorksheetAutoFilterColumnModel CloneAutoFilterColumn(WorksheetAutoFilterColumnModel column) =>
        new(
            column.ColumnId,
            column.Values.ToArray(),
            column.IncludeBlank,
            column.CustomFilters.Select(CloneAutoFilterCustomFilter).ToArray(),
            column.CustomFiltersAnd,
            column.CustomFiltersAndRaw,
            column.NativeCustomFiltersAttributes is null
                ? null
                : new Dictionary<string, string>(column.NativeCustomFiltersAttributes, StringComparer.Ordinal),
            CloneAutoFilterTop10(column.Top10),
            CloneAutoFilterDynamicFilter(column.DynamicFilter),
            CloneAutoFilterColorFilter(column.ColorFilter),
            CloneAutoFilterIconFilter(column.IconFilter),
            column.DateGroups.Select(CloneAutoFilterDateGroup).ToArray(),
            column.NativeFiltersAttributes is null
                ? null
                : new Dictionary<string, string>(column.NativeFiltersAttributes, StringComparer.Ordinal),
            column.NativeFilterXmls.ToArray(),
            column.NativeAttributes is null
                ? null
                : new Dictionary<string, string>(column.NativeAttributes, StringComparer.Ordinal));

    private static WorksheetAutoFilterCustomFilterModel CloneAutoFilterCustomFilter(WorksheetAutoFilterCustomFilterModel filter) =>
        new(
            filter.Operator,
            filter.Value,
            filter.NativeAttributes is null
                ? null
                : new Dictionary<string, string>(filter.NativeAttributes, StringComparer.Ordinal));

    private static WorksheetAutoFilterDateGroupItemModel CloneAutoFilterDateGroup(WorksheetAutoFilterDateGroupItemModel dateGroup) =>
        dateGroup with
        {
            NativeAttributes = dateGroup.NativeAttributes is null
                ? null
                : new Dictionary<string, string>(dateGroup.NativeAttributes, StringComparer.Ordinal)
        };

    private static WorksheetAutoFilterTop10Model? CloneAutoFilterTop10(WorksheetAutoFilterTop10Model? top10) =>
        top10 is null
            ? null
            : top10 with
            {
                NativeAttributes = top10.NativeAttributes is null
                    ? null
                    : new Dictionary<string, string>(top10.NativeAttributes, StringComparer.Ordinal)
            };

    private static WorksheetAutoFilterDynamicFilterModel? CloneAutoFilterDynamicFilter(WorksheetAutoFilterDynamicFilterModel? dynamicFilter) =>
        dynamicFilter is null
            ? null
            : dynamicFilter with
            {
                NativeAttributes = dynamicFilter.NativeAttributes is null
                    ? null
                    : new Dictionary<string, string>(dynamicFilter.NativeAttributes, StringComparer.Ordinal)
            };

    private static WorksheetAutoFilterColorFilterModel? CloneAutoFilterColorFilter(WorksheetAutoFilterColorFilterModel? colorFilter) =>
        colorFilter is null
            ? null
            : colorFilter with
            {
                NativeAttributes = colorFilter.NativeAttributes is null
                    ? null
                    : new Dictionary<string, string>(colorFilter.NativeAttributes, StringComparer.Ordinal)
            };

    private static WorksheetAutoFilterIconFilterModel? CloneAutoFilterIconFilter(WorksheetAutoFilterIconFilterModel? iconFilter) =>
        iconFilter is null
            ? null
            : iconFilter with
            {
                NativeAttributes = iconFilter.NativeAttributes is null
                    ? null
                    : new Dictionary<string, string>(iconFilter.NativeAttributes, StringComparer.Ordinal)
            };
}
