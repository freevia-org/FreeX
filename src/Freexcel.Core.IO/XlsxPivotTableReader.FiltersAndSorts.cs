using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
                var kind = ReadNativePivotValueFilterKind(filter.Attribute("type")?.Value);
                if (kind is null)
                    return null;

                return new PivotValueFilterModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "iMeasureFld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "dataField") ?? 0,
                    kind.Value,
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "count") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "val") ?? (kind.Value is PivotValueFilterKind.Top or PivotValueFilterKind.Bottom ? 10 : 0),
                    ReadNativePivotFilterDoubleValue(filter, "stringValue1", "value1", "val"),
                    ReadNativePivotFilterDoubleValue(filter, "stringValue2", "value2"),
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "fld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "field"));
            })
            .Where(filter => filter is not null)
            .Select(filter => filter!)
            .ToList();
    }

    private static List<PivotLabelFilterModel> ReadNativePivotLabelFilters(XElement? filtersElement, XNamespace workbookNs)
    {
        if (filtersElement is null)
            return [];

        return filtersElement
            .Elements(workbookNs + "filter")
            .Select(filter =>
            {
                var kind = ReadNativePivotLabelFilterKind(filter.Attribute("type")?.Value);
                var value = ReadNativePivotFilterTextValue(filter, "stringValue1", "value1", "val");
                if (kind is null || string.IsNullOrEmpty(value))
                    return null;

                return new PivotLabelFilterModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "fld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "field") ?? -1,
                    kind.Value,
                    value,
                    ReadNativePivotFilterTextValue(filter, "stringValue2", "value2"));
            })
            .Where(filter => filter is not null && filter.SourceFieldIndex >= 0)
            .Select(filter => filter!)
            .ToList();
    }

    private static PivotValueFilterKind? ReadNativePivotValueFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "count" or "topcount" or "top" => PivotValueFilterKind.Top,
            "bottomcount" or "bottom" => PivotValueFilterKind.Bottom,
            "valueequal" or "valueequals" => PivotValueFilterKind.Equals,
            "valuenotequal" or "valuedoesnotequal" => PivotValueFilterKind.DoesNotEqual,
            "valuegreaterthan" => PivotValueFilterKind.GreaterThan,
            "valuegreaterthanorequal" => PivotValueFilterKind.GreaterThanOrEqual,
            "valuelessthan" => PivotValueFilterKind.LessThan,
            "valuelessthanorequal" => PivotValueFilterKind.LessThanOrEqual,
            "valuebetween" => PivotValueFilterKind.Between,
            "valuenotbetween" => PivotValueFilterKind.NotBetween,
            _ => null
        };

    private static PivotLabelFilterKind? ReadNativePivotLabelFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "captionequal" or "captionequals" => PivotLabelFilterKind.Equals,
            "captionnotequal" or "captiondoesnotequal" => PivotLabelFilterKind.DoesNotEqual,
            "captionbeginswith" => PivotLabelFilterKind.BeginsWith,
            "captionendswith" => PivotLabelFilterKind.EndsWith,
            "captioncontains" => PivotLabelFilterKind.Contains,
            "captionnotcontains" or "captiondoesnotcontain" => PivotLabelFilterKind.DoesNotContain,
            "captiongreaterthan" => PivotLabelFilterKind.GreaterThan,
            "captiongreaterthanorequal" => PivotLabelFilterKind.GreaterThanOrEqual,
            "captionlessthan" => PivotLabelFilterKind.LessThan,
            "captionlessthanorequal" => PivotLabelFilterKind.LessThanOrEqual,
            "captionbetween" => PivotLabelFilterKind.Between,
            _ => null
        };

    private static string? ReadNativePivotFilterTextValue(XElement filter, params string[] attributeNames) =>
        attributeNames
            .Select(name => filter.Attribute(name)?.Value)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

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

                return new PivotSortModel(
                    PivotSortTarget.Label,
                    string.Equals(sortType, "descending", StringComparison.OrdinalIgnoreCase)
                        ? PivotSortDirection.Descending
                        : PivotSortDirection.Ascending,
                    FieldIndex: item.Index);
            })
            .Where(sort => sort is not null)
            .Select(sort => sort!)
            .ToList();
    }

}
