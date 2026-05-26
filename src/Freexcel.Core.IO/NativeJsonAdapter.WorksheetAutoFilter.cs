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
                FilterColumns = autoFilter.FilterColumns.Select(column => new WorksheetAutoFilterColumnDto
                {
                    ColumnId = column.ColumnId,
                    Values = column.Values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList(),
                    IncludeBlank = column.IncludeBlank,
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
                    NativeFilterXmls = column.NativeFilterXmls.Where(xml => !string.IsNullOrWhiteSpace(xml)).ToList(),
                    NativeAttributes = CleanNativeAttributesForSave(column.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                }).ToList()
            };
}
