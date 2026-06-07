using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;
using static FreeX.Core.IO.XlsxXmlNormalizationHelpers;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetAutoFilterNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly HashSet<string> AutoFilterAttributes = ["ref"];
    private static readonly HashSet<string> AutoFilterChildren = ["filterColumn", "sortState", "extLst"];
    private static readonly HashSet<string> FilterColumnAttributes = ["colId", "hiddenButton", "showButton"];
    private static readonly HashSet<string> FilterColumnChildren =
    [
        "filters",
        "top10",
        "customFilters",
        "dynamicFilter",
        "colorFilter",
        "iconFilter"
    ];
    private static readonly HashSet<string> FiltersAttributes = ["blank", "calendarType"];
    private static readonly HashSet<string> FilterAttributes = ["val"];
    private static readonly HashSet<string> DateGroupItemAttributes = ["year", "month", "day", "hour", "minute", "second", "dateTimeGrouping"];
    private static readonly HashSet<string> CustomFiltersAttributes = ["and"];
    private static readonly HashSet<string> CustomFilterAttributes = ["operator", "val"];
    private static readonly HashSet<string> Top10Attributes = ["top", "percent", "val", "filterVal"];
    private static readonly HashSet<string> DynamicFilterAttributes = ["type", "val", "maxVal"];
    private static readonly HashSet<string> ColorFilterAttributes = ["dxfId", "cellColor"];
    private static readonly HashSet<string> IconFilterAttributes = ["iconSet", "iconId"];

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
        changed |= RemoveUnknownAttributes(autoFilter, AutoFilterAttributes);
        changed |= RemoveChildElementsExcept(autoFilter, WorksheetNs, AutoFilterChildren);
        changed |= RemoveDuplicateChildren(autoFilter, "sortState");
        changed |= NormalizeExtensionLists(autoFilter);
        changed |= NormalizeAttribute(autoFilter, "ref", NormalizeCellOrRangeReference);

        foreach (var filterColumn in autoFilter.Elements(WorksheetNs + "filterColumn").ToList())
            changed |= NormalizeFilterColumnElement(filterColumn);
        foreach (var sortState in autoFilter.Elements(WorksheetNs + "sortState").ToList())
            changed |= XlsxWorksheetSortStateNormalizer.NormalizeElement(sortState);

        changed |= NormalizeChildOrder(autoFilter, AutoFilterChildOrder);
        return changed;
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var autoFilter = worksheetRoot.Element(WorksheetNs + "autoFilter");
        return autoFilter is not null && NormalizeElement(autoFilter);
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is not null &&
                NormalizeWorksheetRoot(root))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
            }
        }
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
        changed |= RemoveUnknownAttributes(filterColumn, FilterColumnAttributes);
        changed |= RemoveChildElementsExcept(filterColumn, WorksheetNs, FilterColumnChildren);
        changed |= RemoveDuplicateChildren(filterColumn, "filters");
        changed |= RemoveDuplicateChildren(filterColumn, "top10");
        changed |= RemoveDuplicateChildren(filterColumn, "customFilters");
        changed |= RemoveDuplicateChildren(filterColumn, "dynamicFilter");
        changed |= RemoveDuplicateChildren(filterColumn, "colorFilter");
        changed |= RemoveDuplicateChildren(filterColumn, "iconFilter");
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

        changed |= NormalizeChildOrder(filterColumn, FilterColumnChildOrder);
        return changed;
    }

    private static bool NormalizeFiltersElement(XElement filters)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(filters, FiltersAttributes);
        changed |= NormalizeAttribute(filters, "blank", NormalizeBoolean);
        changed |= NormalizeAttribute(filters, "calendarType", value => NormalizeToken(value, ValidCalendarTypes));

        foreach (var filter in filters.Elements(WorksheetNs + "filter"))
        {
            changed |= RemoveUnknownAttributes(filter, FilterAttributes);
            changed |= RemoveAllNodes(filter);
        }

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
        changed |= RemoveUnknownAttributes(dateGroupItem, DateGroupItemAttributes);
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
        changed |= RemoveUnknownAttributes(customFilters, CustomFiltersAttributes);
        changed |= NormalizeAttribute(customFilters, "and", NormalizeBoolean);

        foreach (var customFilter in customFilters.Elements(WorksheetNs + "customFilter"))
        {
            changed |= RemoveUnknownAttributes(customFilter, CustomFilterAttributes);
            changed |= NormalizeAttribute(
                customFilter,
                "operator",
                value => NormalizeToken(value, ValidCustomFilterOperators));
            changed |= RemoveAllNodes(customFilter);
        }

        return changed;
    }

    private static bool NormalizeTop10Element(XElement top10)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(top10, Top10Attributes);
        changed |= NormalizeAttribute(top10, "top", NormalizeBoolean);
        changed |= NormalizeAttribute(top10, "percent", NormalizeBoolean);
        changed |= NormalizeAttribute(top10, "val", value => NormalizeDouble(value) ?? "10");
        changed |= NormalizeAttribute(top10, "filterVal", NormalizeDouble);
        return changed;
    }

    private static bool NormalizeDynamicFilterElement(XElement dynamicFilter)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(dynamicFilter, DynamicFilterAttributes);
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
        changed |= RemoveUnknownAttributes(colorFilter, ColorFilterAttributes);
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
        changed |= RemoveUnknownAttributes(iconFilter, IconFilterAttributes);
        changed |= SetAttributeIfChanged(iconFilter, "iconSet", normalizedIconSet);
        changed |= SetAttributeIfChanged(iconFilter, "iconId", normalizedIconId);
        return changed;
    }

    private static bool NormalizeExtensionLists(XElement parent)
    {
        var changed = false;
        var keptExtensionList = false;
        foreach (var extensionList in parent.Elements(WorksheetNs + "extLst").ToList())
        {
            if (keptExtensionList)
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxWorksheetExtensionListNormalizer.NormalizeExtensionListElement(extensionList);
            if (XlsxWorksheetExtensionListNormalizer.ShouldRemoveExtensionListElement(extensionList))
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            keptExtensionList = true;
        }

        return changed;
    }

    private static bool RemoveDuplicateChildren(XElement element, string localName)
    {
        var changed = false;
        var seen = false;
        foreach (var child in element.Elements(WorksheetNs + localName).ToList())
        {
            if (!seen)
            {
                seen = true;
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static int AutoFilterChildOrder(XElement child) =>
        child.Name == WorksheetNs + "filterColumn" ? 0 :
        child.Name == WorksheetNs + "sortState" ? 10 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

    private static int FilterColumnChildOrder(XElement child) =>
        child.Name == WorksheetNs + "extLst" ? 100 : 0;

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

    private static string? NormalizeUnsignedShortOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return ushort.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
