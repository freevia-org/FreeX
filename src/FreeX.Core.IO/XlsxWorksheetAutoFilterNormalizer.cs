using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetAutoFilterNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> ValidCalendarTypes =
    [
        "none",
        "gregorian",
        "gregorianUs",
        "japan",
        "taiwan",
        "korea",
        "hijri",
        "thai",
        "hebrew",
        "gregorianMeFrench",
        "gregorianArabic",
        "gregorianXlitEnglish",
        "gregorianXlitFrench"
    ];

    private static readonly HashSet<string> ValidCustomFilterOperators =
    [
        "equal",
        "lessThan",
        "lessThanOrEqual",
        "notEqual",
        "greaterThanOrEqual",
        "greaterThan"
    ];

    private static readonly HashSet<string> ValidDateTimeGroupings =
    [
        "year",
        "month",
        "day",
        "hour",
        "minute",
        "second"
    ];

    private static readonly HashSet<string> ValidDynamicFilterTypes =
    [
        "null",
        "aboveAverage",
        "belowAverage",
        "tomorrow",
        "today",
        "yesterday",
        "nextWeek",
        "thisWeek",
        "lastWeek",
        "nextMonth",
        "thisMonth",
        "lastMonth",
        "nextQuarter",
        "thisQuarter",
        "lastQuarter",
        "nextYear",
        "thisYear",
        "lastYear",
        "yearToDate",
        "Q1",
        "Q2",
        "Q3",
        "Q4",
        "M1",
        "M2",
        "M3",
        "M4",
        "M5",
        "M6",
        "M7",
        "M8",
        "M9",
        "M10",
        "M11",
        "M12"
    ];

    private static readonly HashSet<string> ValidIconSets =
    [
        "3Arrows",
        "3ArrowsGray",
        "3Flags",
        "3TrafficLights1",
        "3TrafficLights2",
        "3Signs",
        "3Symbols",
        "3Symbols2",
        "4Arrows",
        "4ArrowsGray",
        "4RedToBlack",
        "4Rating",
        "4TrafficLights",
        "5Arrows",
        "5ArrowsGray",
        "5Rating",
        "5Quarters"
    ];

    public static bool NormalizeElement(XElement autoFilter)
    {
        var changed = false;
        changed |= NormalizeAttribute(autoFilter, "ref", NormalizeCellOrRangeReference);

        foreach (var filterColumn in autoFilter.Elements(WorksheetNs + "filterColumn").ToList())
            changed |= NormalizeFilterColumnElement(filterColumn);

        return changed;
    }

    private static bool NormalizeFilterColumnElement(XElement filterColumn)
    {
        var normalizedColumnId = NormalizeUnsignedIntOrNull(filterColumn.Attribute("colId")?.Value);
        if (normalizedColumnId is null)
        {
            filterColumn.Remove();
            return true;
        }

        var changed = false;
        changed |= SetAttributeIfChanged(filterColumn, "colId", normalizedColumnId);
        changed |= NormalizeAttribute(filterColumn, "hiddenButton", NormalizeBoolean);
        changed |= NormalizeAttribute(filterColumn, "showButton", NormalizeBoolean);

        foreach (var filters in filterColumn.Elements(WorksheetNs + "filters"))
            changed |= NormalizeFiltersElement(filters);
        foreach (var customFilters in filterColumn.Elements(WorksheetNs + "customFilters"))
            changed |= NormalizeCustomFiltersElement(customFilters);
        foreach (var top10 in filterColumn.Elements(WorksheetNs + "top10"))
            changed |= NormalizeTop10Element(top10);
        foreach (var dynamicFilter in filterColumn.Elements(WorksheetNs + "dynamicFilter"))
            changed |= NormalizeDynamicFilterElement(dynamicFilter);
        foreach (var colorFilter in filterColumn.Elements(WorksheetNs + "colorFilter").ToList())
            changed |= NormalizeColorFilterElement(colorFilter);
        foreach (var iconFilter in filterColumn.Elements(WorksheetNs + "iconFilter").ToList())
            changed |= NormalizeIconFilterElement(iconFilter);

        return changed;
    }

    private static bool NormalizeFiltersElement(XElement filters)
    {
        var changed = false;
        changed |= NormalizeAttribute(filters, "blank", NormalizeBoolean);
        changed |= NormalizeAttribute(filters, "calendarType", value => NormalizeToken(value, ValidCalendarTypes));

        foreach (var dateGroupItem in filters.Elements(WorksheetNs + "dateGroupItem").ToList())
            changed |= NormalizeDateGroupItemElement(dateGroupItem);

        return changed;
    }

    private static bool NormalizeDateGroupItemElement(XElement dateGroupItem)
    {
        var normalizedGrouping = NormalizeToken(
            dateGroupItem.Attribute("dateTimeGrouping")?.Value,
            ValidDateTimeGroupings);
        if (normalizedGrouping is null)
        {
            dateGroupItem.Remove();
            return true;
        }

        var changed = false;
        changed |= SetAttributeIfChanged(dateGroupItem, "dateTimeGrouping", normalizedGrouping);
        changed |= NormalizeAttribute(dateGroupItem, "year", NormalizeUnsignedShortOrNull);
        changed |= NormalizeAttribute(dateGroupItem, "month", NormalizeUnsignedShortOrNull);
        changed |= NormalizeAttribute(dateGroupItem, "day", NormalizeUnsignedShortOrNull);
        changed |= NormalizeAttribute(dateGroupItem, "hour", NormalizeUnsignedShortOrNull);
        changed |= NormalizeAttribute(dateGroupItem, "minute", NormalizeUnsignedShortOrNull);
        changed |= NormalizeAttribute(dateGroupItem, "second", NormalizeUnsignedShortOrNull);
        return changed;
    }

    private static bool NormalizeCustomFiltersElement(XElement customFilters)
    {
        var changed = false;
        changed |= NormalizeAttribute(customFilters, "and", NormalizeBoolean);

        foreach (var customFilter in customFilters.Elements(WorksheetNs + "customFilter"))
        {
            changed |= NormalizeAttribute(
                customFilter,
                "operator",
                value => NormalizeToken(value, ValidCustomFilterOperators));
        }

        return changed;
    }

    private static bool NormalizeTop10Element(XElement top10)
    {
        var changed = false;
        changed |= NormalizeAttribute(top10, "top", NormalizeBoolean);
        changed |= NormalizeAttribute(top10, "percent", NormalizeBoolean);
        changed |= NormalizeAttribute(top10, "val", value => NormalizeDouble(value) ?? "10");
        changed |= NormalizeAttribute(top10, "filterVal", NormalizeDouble);
        return changed;
    }

    private static bool NormalizeDynamicFilterElement(XElement dynamicFilter)
    {
        var changed = false;
        changed |= NormalizeAttribute(
            dynamicFilter,
            "type",
            value => NormalizeToken(value, ValidDynamicFilterTypes) ?? "aboveAverage");
        changed |= NormalizeAttribute(dynamicFilter, "val", NormalizeDouble);
        changed |= NormalizeAttribute(dynamicFilter, "maxVal", NormalizeDouble);
        return changed;
    }

    private static bool NormalizeColorFilterElement(XElement colorFilter)
    {
        if (NormalizeUnsignedIntOrNull(colorFilter.Attribute("dxfId")?.Value) is not { } normalizedDxfId)
        {
            colorFilter.Remove();
            return true;
        }

        var changed = false;
        changed |= SetAttributeIfChanged(colorFilter, "dxfId", normalizedDxfId);
        changed |= NormalizeAttribute(colorFilter, "cellColor", NormalizeBoolean);
        return changed;
    }

    private static bool NormalizeIconFilterElement(XElement iconFilter)
    {
        var normalizedIconSet = NormalizeToken(iconFilter.Attribute("iconSet")?.Value, ValidIconSets);
        var normalizedIconId = NormalizeUnsignedIntOrNull(iconFilter.Attribute("iconId")?.Value);
        if (normalizedIconSet is null || normalizedIconId is null)
        {
            iconFilter.Remove();
            return true;
        }

        var changed = false;
        changed |= SetAttributeIfChanged(iconFilter, "iconSet", normalizedIconSet);
        changed |= SetAttributeIfChanged(iconFilter, "iconId", normalizedIconId);
        return changed;
    }

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        return SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static bool SetAttributeIfChanged(XElement element, string attributeName, string value)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static string? NormalizeCellOrRangeReference(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var parts = trimmed.Split(':');
        if (parts.Length == 1)
            return CellAddress.TryParse(parts[0], SheetId.New(), out _) ? trimmed : null;

        return parts.Length == 2 &&
               CellAddress.TryParse(parts[0], SheetId.New(), out _) &&
               CellAddress.TryParse(parts[1], SheetId.New(), out _)
            ? trimmed
            : null;
    }

    private static string? NormalizeDouble(string? value)
    {
        var trimmed = value?.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
        {
            return null;
        }

        return parsed.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeUnsignedShortOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return ushort.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
