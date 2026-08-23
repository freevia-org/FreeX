using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableReader
{
    private static List<PivotValueFilterModel> ReadPivotValueFilters(XElement? valueFiltersElement, XNamespace workbookNs)
    {
        if (valueFiltersElement is null)
            return [];

        return valueFiltersElement
            .Elements(workbookNs + "valueFilter")
            .Select(filter => new PivotValueFilterModel(
                XlsxXmlAttributeReader.ReadIntAttribute(filter, "dataField") ?? -1,
                ReadPivotValueFilterKind(filter.Attribute("type")?.Value),
                XlsxXmlAttributeReader.ReadIntAttribute(filter, "count") ?? 0,
                XlsxXmlAttributeReader.ReadDoubleAttribute(filter, "comparisonValue"),
                XlsxXmlAttributeReader.ReadDoubleAttribute(filter, "comparisonValue2"),
                XlsxXmlAttributeReader.ReadIntAttribute(filter, "field")))
            .Where(filter => filter.DataFieldIndex >= 0 &&
                             (filter.Count > 0 ||
                              filter.ComparisonValue is not null ||
                              filter.Kind is PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage))
            .ToList();
    }

    private static List<PivotLabelFilterModel> ReadPivotLabelFilters(XElement? labelFiltersElement, XNamespace workbookNs)
    {
        if (labelFiltersElement is null)
            return [];

        return labelFiltersElement
            .Elements(workbookNs + "labelFilter")
            .Select(filter => new PivotLabelFilterModel(
                XlsxXmlAttributeReader.ReadIntAttribute(filter, "field") ?? -1,
                ReadPivotLabelFilterKind(filter.Attribute("type")?.Value),
                filter.Attribute("value")?.Value ?? "",
                filter.Attribute("value2")?.Value))
            .Where(filter => filter.SourceFieldIndex >= 0 && !string.IsNullOrEmpty(filter.Value))
            .ToList();
    }

    private static List<PivotValueFilterModel> ReadNativePivotValueFilters(XElement? filtersElement, XNamespace workbookNs)
    {
        if (filtersElement is null)
            return [];

        return filtersElement
            .Elements(workbookNs + "filter")
            .Select(filter =>
            {
                var kind = XlsxPivotFilterKindCodec.DecodeValue(filter, workbookNs);
                if (kind is null)
                    return null;

                return new PivotValueFilterModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "iMeasureFld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "dataField") ?? 0,
                    kind.Value,
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "count") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "val") ?? ReadNativeTopFilterCount(filter, workbookNs) ?? (kind.Value is PivotValueFilterKind.Top or PivotValueFilterKind.Bottom ? 10 : 0),
                    ReadNativePivotFilterDoubleValue(filter, "stringValue1", "value1", "val"),
                    ReadNativePivotFilterDoubleValue(filter, "stringValue2", "value2"),
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "fld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "field"));
            })
            .Where(filter => filter is not null)
            .Select(filter => filter!)
            .ToList();
    }

    // R82-io-pivot-layout-5-2: the real <filter>'s nested <autoFilter><filterColumn><top10 val="N"/>
    // (written by XlsxPivotTableWriter.cs's ToPivotValueFilterAutoFilterFillerXml) is where an actual
    // Excel-authored Top-10 filter's count genuinely lives -- CT_PivotFilter itself has no "count"/"val"
    // attribute at all (confirmed via reflection against the OpenXml SDK), so the flat-attribute reads
    // above are FreeX-authored-only fallbacks that never match a real Excel file.
    private static int? ReadNativeTopFilterCount(XElement filter, XNamespace workbookNs)
    {
        var top10 = filter.Element(workbookNs + "autoFilter")?
            .Element(workbookNs + "filterColumn")?
            .Element(workbookNs + "top10");
        return top10 is not null && XlsxXmlAttributeReader.ReadDoubleAttribute(top10, "val") is { } value
            ? (int)value
            : null;
    }

    private static List<PivotLabelFilterModel> ReadNativePivotLabelFilters(XElement? filtersElement, XNamespace workbookNs)
    {
        if (filtersElement is null)
            return [];

        return filtersElement
            .Elements(workbookNs + "filter")
            .Select(filter =>
            {
                var kind = XlsxPivotFilterKindCodec.DecodeLabel(filter.Attribute("type")?.Value);
                if (kind is null)
                    return null;

                var value = ReadNativePivotFilterTextValue(filter, "stringValue1", "value1", "val");
                // R36-io-pivot-cache-2-3: Excel's "relative period" date filters (Today/ThisQuarter/
                // YearToDate/etc.) carry no value attribute at all -- the period is implied entirely by
                // the type token, computed dynamically from the current date. Only the value-bearing
                // kinds (caption*, dateEqual..dateNotBetween) require a non-empty value to be captured.
                if (string.IsNullOrEmpty(value) && !XlsxPivotFilterKindCodec.AllowsEmptyLabelValue(kind.Value))
                    return null;

                return new PivotLabelFilterModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "fld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "field") ?? -1,
                    kind.Value,
                    value ?? "",
                    ReadNativePivotFilterTextValue(filter, "stringValue2", "value2"));
            })
            .Where(filter => filter is not null && filter.SourceFieldIndex >= 0)
            .Select(filter => filter!)
            .ToList();
    }

    private static string? ReadNativePivotFilterTextValue(XElement filter, params string[] attributeNames)
    {
        foreach (var attributeName in attributeNames)
        {
            var value = filter.Attribute(attributeName)?.Value;
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }

    private static double? ReadNativePivotFilterDoubleValue(XElement filter, params string[] attributeNames)
    {
        foreach (var attributeName in attributeNames)
        {
            if (double.TryParse(filter.Attribute(attributeName)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
        }

        return null;
    }

    private static List<PivotSortModel> ReadPivotSorts(XElement? sortsElement, XNamespace workbookNs)
    {
        if (sortsElement is null)
            return [];

        return sortsElement
            .Elements(workbookNs + "pivotSort")
            .Select(sort => new PivotSortModel(
                string.Equals(sort.Attribute("target")?.Value, "label", StringComparison.OrdinalIgnoreCase)
                    ? PivotSortTarget.Label
                    : PivotSortTarget.Value,
                string.Equals(sort.Attribute("direction")?.Value, "descending", StringComparison.OrdinalIgnoreCase)
                    ? PivotSortDirection.Descending
                    : PivotSortDirection.Ascending,
                XlsxXmlAttributeReader.ReadIntAttribute(sort, "dataField") ?? 0,
                XlsxXmlAttributeReader.ReadIntAttribute(sort, "field") ?? 0))
            .ToList();
    }

    // ECMA-376's CT_Reference "field" attribute is an xsd:unsignedInt; the special sentinel Excel uses to
    // mark "this reference identifies the Values/data axis, not an ordinary row/column field" is -2
    // (mirroring the CT_Field @x="-2" "Σ Values" pseudo-field marker read elsewhere in this file), written
    // in its unsigned wire form.
    private const string PivotFieldDataAxisReferenceValue = "4294967294";

    private static List<PivotSortModel> ReadNativePivotFieldSorts(XElement? pivotFieldsElement, XNamespace workbookNs)
    {
        if (pivotFieldsElement is null)
            return [];

        return pivotFieldsElement
            .Elements(workbookNs + "pivotField")
            .Select((field, index) => (Field: field, Index: index))
            .Select(item =>
            {
                var sortType = item.Field.Attribute("sortType")?.Value;
                if (!string.Equals(sortType, "ascending", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(sortType, "descending", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var direction = string.Equals(sortType, "descending", StringComparison.OrdinalIgnoreCase)
                    ? PivotSortDirection.Descending
                    : PivotSortDirection.Ascending;

                // R75-io-pivottable-layout-4-4: a sort-by-data-field ("Sort by Total Revenue descending")
                // is recorded as sortType PLUS an <autoSortScope> identifying which data field drives the
                // order -- reading sortType alone (as before) silently turned it into a plain Label sort
                // ("sort Product names Z-A"), the opposite of what the user actually configured.
                if (ReadAutoSortScopeDataFieldIndex(item.Field, workbookNs) is { } dataFieldIndex)
                {
                    return new PivotSortModel(PivotSortTarget.Value, direction, DataFieldIndex: dataFieldIndex, FieldIndex: item.Index);
                }

                return new PivotSortModel(PivotSortTarget.Label, direction, FieldIndex: item.Index);
            })
            .Where(sort => sort is not null)
            .Select(sort => sort!)
            .ToList();
    }

    // Reads the data-field index a <pivotField>'s <autoSortScope> identifies, when present. Real Excel
    // marks an auto-sort-by-value scope with a <reference field="4294967294"> (the sentinel above) whose
    // single child <x v="N"/> names which data field -- by its position in <dataFields>, i.e.
    // PivotTableModel.DataFields -- drives the sort. Returns null when the field has no autoSortScope at
    // all, or the autoSortScope's references never identify a data field (an unusual/malformed shape);
    // callers then fall back to treating the sort as a plain Label sort, exactly as before this fix.
    private static int? ReadAutoSortScopeDataFieldIndex(XElement pivotFieldElement, XNamespace workbookNs)
    {
        var references = pivotFieldElement
            .Element(workbookNs + "autoSortScope")?
            .Element(workbookNs + "pivotArea")?
            .Element(workbookNs + "references")?
            .Elements(workbookNs + "reference");
        if (references is null)
            return null;

        foreach (var reference in references)
        {
            if (!string.Equals(reference.Attribute("field")?.Value, PivotFieldDataAxisReferenceValue, StringComparison.Ordinal))
                continue;

            var xElement = reference.Element(workbookNs + "x");
            if (xElement is not null && XlsxXmlAttributeReader.ReadIntAttribute(xElement, "v") is { } dataFieldIndex)
                return dataFieldIndex;
        }

        return null;
    }

}
