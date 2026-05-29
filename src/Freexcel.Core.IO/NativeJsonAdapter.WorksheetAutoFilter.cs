using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetAutoFilterModel? ToWorksheetAutoFilter(WorksheetAutoFilterDto? dto)
    {
        if (dto is null ||
            (string.IsNullOrWhiteSpace(dto.Reference) &&
             string.IsNullOrWhiteSpace(dto.NativeXml) &&
             (dto.FilterColumns is null || dto.FilterColumns.Count == 0)))
        {
            return null;
        }

        var autoFilter = new WorksheetAutoFilterModel(dto.Reference, dto.NativeXml)
        {
            NativeAttributes = CleanNativeAttributes(dto.NativeAttributes),
            NativeChildXmls = dto.NativeChildXmls?
                .Where(xml => !string.IsNullOrWhiteSpace(xml))
                .ToArray()
        };
        foreach (var column in dto.FilterColumns ?? [])
        {
            if (column.ColumnId >= 0)
            {
                autoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
                    column.ColumnId,
                    column.Values?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [],
                    column.IncludeBlank,
                    column.CustomFilters?
                        .Select(filter => new WorksheetAutoFilterCustomFilterModel(
                            filter.Operator,
                            filter.Value,
                            CleanNativeAttributes(filter.NativeAttributes)))
                        .ToArray() ?? [],
                    column.CustomFiltersAnd,
                    column.CustomFiltersAndRaw,
                    CleanNativeAttributes(column.NativeCustomFiltersAttributes),
                    column.Top10 is null
                        ? null
                        : new WorksheetAutoFilterTop10Model(
                            column.Top10.Top,
                            column.Top10.Percent,
                            column.Top10.Value,
                            column.Top10.FilterValue,
                            column.Top10.TopRaw,
                            column.Top10.PercentRaw,
                            column.Top10.ValueRaw,
                            column.Top10.FilterValueRaw,
                            CleanNativeAttributes(column.Top10.NativeAttributes)),
                    column.DynamicFilter is null
                        ? null
                        : new WorksheetAutoFilterDynamicFilterModel(
                            column.DynamicFilter.Type,
                            column.DynamicFilter.Value,
                            column.DynamicFilter.MaxValue,
                            column.DynamicFilter.ValueRaw,
                            column.DynamicFilter.MaxValueRaw,
                            CleanNativeAttributes(column.DynamicFilter.NativeAttributes)),
                    column.ColorFilter is null
                        ? null
                        : new WorksheetAutoFilterColorFilterModel(
                            column.ColorFilter.DifferentialFormatId,
                            column.ColorFilter.CellColor,
                            column.ColorFilter.DifferentialFormatIdRaw,
                            column.ColorFilter.CellColorRaw,
                            CleanNativeAttributes(column.ColorFilter.NativeAttributes)),
                    column.IconFilter is null
                        ? null
                        : new WorksheetAutoFilterIconFilterModel(
                            column.IconFilter.IconSet,
                            column.IconFilter.IconId,
                            column.IconFilter.IconIdRaw,
                            CleanNativeAttributes(column.IconFilter.NativeAttributes)),
                    column.DateGroups?
                        .Select(dateGroup => new WorksheetAutoFilterDateGroupItemModel(
                            dateGroup.Year,
                            dateGroup.Month,
                            dateGroup.Day,
                            dateGroup.Hour,
                            dateGroup.Minute,
                            dateGroup.Second,
                            dateGroup.DateTimeGrouping,
                            dateGroup.YearRaw,
                            dateGroup.MonthRaw,
                            dateGroup.DayRaw,
                            dateGroup.HourRaw,
                            dateGroup.MinuteRaw,
                            dateGroup.SecondRaw,
                            CleanNativeAttributes(dateGroup.NativeAttributes)))
                        .ToArray() ?? [],
                    CleanNativeAttributes(column.NativeFiltersAttributes),
                    column.NativeFilterXmls?.Where(xml => !string.IsNullOrWhiteSpace(xml)).ToArray() ?? [],
                    CleanNativeAttributes(column.NativeAttributes)));
            }
        }

        return autoFilter;
    }

    private static WorksheetAutoFilterDto? ToWorksheetAutoFilterDto(WorksheetAutoFilterModel? autoFilter) =>
        autoFilter is null
            ? null
            : new WorksheetAutoFilterDto
            {
                Reference = autoFilter.Reference,
                NativeXml = autoFilter.NativeXml,
                NativeAttributes = CleanNativeAttributesForSave(autoFilter.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
                NativeChildXmls = autoFilter.NativeChildXmls?
                    .Where(xml => !string.IsNullOrWhiteSpace(xml))
                    .ToList(),
                FilterColumns = autoFilter.FilterColumns
                    .Where(column => column.ColumnId >= 0)
                    .Select(column => new WorksheetAutoFilterColumnDto
                    {
                        ColumnId = column.ColumnId,
                        Values = column.Values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList(),
                        IncludeBlank = column.IncludeBlank,
                        DateGroups = column.DateGroups.Select(dateGroup => new WorksheetAutoFilterDateGroupItemDto
                        {
                            Year = dateGroup.Year,
                            Month = dateGroup.Month,
                            Day = dateGroup.Day,
                            Hour = dateGroup.Hour,
                            Minute = dateGroup.Minute,
                            Second = dateGroup.Second,
                            DateTimeGrouping = dateGroup.DateTimeGrouping,
                            YearRaw = dateGroup.YearRaw,
                            MonthRaw = dateGroup.MonthRaw,
                            DayRaw = dateGroup.DayRaw,
                            HourRaw = dateGroup.HourRaw,
                            MinuteRaw = dateGroup.MinuteRaw,
                            SecondRaw = dateGroup.SecondRaw,
                            NativeAttributes = CleanNativeAttributesForSave(dateGroup.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                        }).ToList(),
                        NativeFiltersAttributes = CleanNativeAttributesForSave(column.NativeFiltersAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
                        CustomFilters = column.CustomFilters.Select(filter => new WorksheetAutoFilterCustomFilterDto
                        {
                            Operator = filter.Operator,
                            Value = filter.Value,
                            NativeAttributes = CleanNativeAttributesForSave(filter.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                        }).ToList(),
                        CustomFiltersAnd = column.CustomFiltersAnd,
                        CustomFiltersAndRaw = column.CustomFiltersAndRaw,
                        NativeCustomFiltersAttributes = CleanNativeAttributesForSave(column.NativeCustomFiltersAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
                        Top10 = column.Top10 is null
                            ? null
                            : new WorksheetAutoFilterTop10Dto
                            {
                                Top = column.Top10.Top,
                                Percent = column.Top10.Percent,
                                Value = column.Top10.Value,
                                FilterValue = column.Top10.FilterValue,
                                TopRaw = column.Top10.TopRaw,
                                PercentRaw = column.Top10.PercentRaw,
                                ValueRaw = column.Top10.ValueRaw,
                                FilterValueRaw = column.Top10.FilterValueRaw,
                                NativeAttributes = CleanNativeAttributesForSave(column.Top10.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                            },
                        DynamicFilter = column.DynamicFilter is null
                            ? null
                            : new WorksheetAutoFilterDynamicFilterDto
                            {
                                Type = column.DynamicFilter.Type,
                                Value = column.DynamicFilter.Value,
                                MaxValue = column.DynamicFilter.MaxValue,
                                ValueRaw = column.DynamicFilter.ValueRaw,
                                MaxValueRaw = column.DynamicFilter.MaxValueRaw,
                                NativeAttributes = CleanNativeAttributesForSave(column.DynamicFilter.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                            },
                        ColorFilter = column.ColorFilter is null
                            ? null
                            : new WorksheetAutoFilterColorFilterDto
                            {
                                DifferentialFormatId = column.ColorFilter.DifferentialFormatId,
                                CellColor = column.ColorFilter.CellColor,
                                DifferentialFormatIdRaw = column.ColorFilter.DifferentialFormatIdRaw,
                                CellColorRaw = column.ColorFilter.CellColorRaw,
                                NativeAttributes = CleanNativeAttributesForSave(column.ColorFilter.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                            },
                        IconFilter = column.IconFilter is null
                            ? null
                            : new WorksheetAutoFilterIconFilterDto
                            {
                                IconSet = column.IconFilter.IconSet,
                                IconId = column.IconFilter.IconId,
                                IconIdRaw = column.IconFilter.IconIdRaw,
                                NativeAttributes = CleanNativeAttributesForSave(column.IconFilter.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                            },
                        NativeFilterXmls = column.NativeFilterXmls.Where(xml => !string.IsNullOrWhiteSpace(xml)).ToList(),
                        NativeAttributes = CleanNativeAttributesForSave(column.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                    }).ToList()
            };
}
