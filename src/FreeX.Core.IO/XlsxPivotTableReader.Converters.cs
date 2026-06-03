using System.Linq;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableReader
{
    private const string FreeXPivotExtensionNamespace = "urn:freex:pivot:2026";

    // Reads a boolean from the FreeX tableProps extLst extension on a pivotTableDefinition; null when absent
    // so callers can fall back to a legacy definition-level attribute. Values are "0"/"1" (default true).
    private static bool? ReadFreeXTableBool(XElement root, XNamespace workbookNs, string attributeName)
    {
        XNamespace freeXNs = FreeXPivotExtensionNamespace;
        var props = root.Element(workbookNs + "extLst")?
            .Elements(workbookNs + "ext")
            .Select(ext => ext.Element(freeXNs + "tableProps"))
            .FirstOrDefault(element => element is not null);
        if (props?.Attribute(attributeName) is not { } attribute)
            return null;

        return !string.Equals(attribute.Value, "0", StringComparison.Ordinal);
    }

    private static Dictionary<int, IReadOnlyList<string>> ReadFreeXPivotFieldSelections(
        XElement root,
        XNamespace workbookNs)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var field in ReadFreeXPivotFieldElements(root, workbookNs))
        {
            var index = XlsxXmlAttributeReader.ReadIntAttribute(field, "x");
            if (index is not { } fieldIndex)
                continue;

            var selectedItems = ReadCsvAttribute(field.Attribute("selectedItems")?.Value);
            if (selectedItems is not null)
            {
                result[fieldIndex] = selectedItems;
                continue;
            }

            var selectedItem = field.Attribute("selectedItem")?.Value;
            if (!string.IsNullOrWhiteSpace(selectedItem))
                result[fieldIndex] = [selectedItem.Trim()];
        }

        return result;
    }

    private static Dictionary<int, PivotFieldModel> ReadFreeXPivotFieldGroups(
        XElement root,
        XNamespace workbookNs)
    {
        var result = new Dictionary<int, PivotFieldModel>();
        foreach (var field in ReadFreeXPivotFieldElements(root, workbookNs))
        {
            var index = XlsxXmlAttributeReader.ReadIntAttribute(field, "x");
            if (index is not { } fieldIndex)
                continue;

            var grouping = ReadPivotFieldGrouping(field.Attribute("groupBy")?.Value);
            var groupStart = XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupStart");
            var groupEnd = XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupEnd");
            var groupInterval = XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupInterval");
            if (grouping == PivotFieldGrouping.None &&
                groupStart is null &&
                groupEnd is null &&
                groupInterval is null)
            {
                continue;
            }

            result[fieldIndex] = new PivotFieldModel(
                fieldIndex,
                Grouping: grouping,
                GroupStart: groupStart,
                GroupEnd: groupEnd,
                GroupInterval: groupInterval);
        }

        return result;
    }

    private static IEnumerable<XElement> ReadFreeXPivotFieldElements(
        XElement root,
        XNamespace workbookNs)
    {
        XNamespace freeXNs = FreeXPivotExtensionNamespace;
        return root.Element(workbookNs + "extLst")?
            .Elements(workbookNs + "ext")
            .Elements(freeXNs + "tableProps")
            .Elements(freeXNs + "fields")
            .Elements(freeXNs + "field")
            ?? [];
    }

    private static Dictionary<TKey, TValue> MergeMissing<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> primary,
        IReadOnlyDictionary<TKey, TValue> fallback)
        where TKey : notnull
    {
        var merged = new Dictionary<TKey, TValue>(primary);
        foreach (var pair in fallback)
            merged.TryAdd(pair.Key, pair.Value);

        return merged;
    }

    // True if any pivotField sets the named boolean attribute; null when no field carries it (so callers
    // can fall back to a legacy definition-level attribute).
    private static bool? ReadAnyPivotFieldBool(XElement root, XNamespace workbookNs, string attributeName)
    {
        var pivotFields = root.Element(workbookNs + "pivotFields");
        if (pivotFields is null)
            return null;

        var fieldsWithAttribute = pivotFields
            .Elements(workbookNs + "pivotField")
            .Where(field => field.Attribute(attributeName) is not null)
            .ToList();
        if (fieldsWithAttribute.Count == 0)
            return null;

        return fieldsWithAttribute.Any(field => XlsxXmlAttributeReader.ReadBoolAttribute(field, attributeName));
    }

    private static IReadOnlyList<string>? ReadCsvAttribute(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static PivotFieldGrouping ReadPivotFieldGrouping(string? value, PivotFieldGrouping defaultValue = PivotFieldGrouping.None) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "years" or "year" => PivotFieldGrouping.Year,
            "quarters" or "quarter" => PivotFieldGrouping.Quarter,
            "months" or "month" => PivotFieldGrouping.Month,
            "days" or "day" => PivotFieldGrouping.Day,
            "range" or "numberrange" or "number-range" or "number" => PivotFieldGrouping.NumberRange,
            _ => defaultValue
        };

    // Derives the report layout from the OOXML CT_pivotTableDefinition layout flags (compact / outline /
    // outlineData / gridDropZones). Falls back to the legacy FreeX 'reportLayout' attribute when present
    // (older saves), and finally to the text-based mapping.
    private static PivotReportLayout ReadPivotReportLayout(XElement root)
    {
        if (root.Attribute("compact") is not null ||
            root.Attribute("outline") is not null ||
            root.Attribute("gridDropZones") is not null)
        {
            var compact = XlsxXmlAttributeReader.ReadBoolAttribute(root, "compact", defaultValue: true);
            var outline = XlsxXmlAttributeReader.ReadBoolAttribute(root, "outline");
            if (compact)
                return PivotReportLayout.Compact;
            if (outline)
                return PivotReportLayout.Outline;

            return PivotReportLayout.Tabular;
        }

        return ReadPivotReportLayout(root.Attribute("reportLayout")?.Value);
    }

    private static PivotReportLayout ReadPivotReportLayout(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "compact" or "compactform" or "compact-form" => PivotReportLayout.Compact,
            "outline" or "outlineform" or "outline-form" => PivotReportLayout.Outline,
            _ => PivotReportLayout.Tabular
        };

    private static PivotShowValuesAs ReadPivotShowValuesAs(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "percentofgrandtotal" or "percent-grand-total" => PivotShowValuesAs.PercentOfGrandTotal,
            "percentofrowtotal" or "percent-row-total" => PivotShowValuesAs.PercentOfRowTotal,
            "percentofcolumntotal" or "percentofcoltotal" or "percent-column-total" or "percent-col-total" => PivotShowValuesAs.PercentOfColumnTotal,
            "runningtotalin" or "running-total-in" => PivotShowValuesAs.RunningTotalIn,
            "differencefrom" or "difference-from" => PivotShowValuesAs.DifferenceFrom,
            "percentdifferencefrom" or "percent-difference-from" => PivotShowValuesAs.PercentDifferenceFrom,
            "ranksmallest" or "rank-smallest" => PivotShowValuesAs.RankSmallest,
            "ranklargest" or "rank-largest" => PivotShowValuesAs.RankLargest,
            "index" => PivotShowValuesAs.Index,
            "percentofparentrowtotal" or "percent-parent-row-total" => PivotShowValuesAs.PercentOfParentRowTotal,
            "percentofparentcolumntotal" or "percentofparentcoltotal" or "percent-parent-column-total" or "percent-parent-col-total" => PivotShowValuesAs.PercentOfParentColumnTotal,
            "percentofparenttotal" or "percent-parent-total" => PivotShowValuesAs.PercentOfParentTotal,
            _ => PivotShowValuesAs.None
        };

    private static PivotValueFilterKind ReadPivotValueFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "bottom" => PivotValueFilterKind.Bottom,
            "greaterthan" or "greater_than" => PivotValueFilterKind.GreaterThan,
            "greaterthanorequal" or "greater_than_or_equal" => PivotValueFilterKind.GreaterThanOrEqual,
            "lessthan" or "less_than" => PivotValueFilterKind.LessThan,
            "lessthanorequal" or "less_than_or_equal" => PivotValueFilterKind.LessThanOrEqual,
            "equals" or "equal" => PivotValueFilterKind.Equals,
            "doesnotequal" or "not_equal" => PivotValueFilterKind.DoesNotEqual,
            "between" => PivotValueFilterKind.Between,
            "notbetween" or "not_between" => PivotValueFilterKind.NotBetween,
            "aboveaverage" or "above_average" => PivotValueFilterKind.AboveAverage,
            "belowaverage" or "below_average" => PivotValueFilterKind.BelowAverage,
            _ => PivotValueFilterKind.Top
        };

    private static PivotLabelFilterKind ReadPivotLabelFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "doesnotequal" or "not_equal" => PivotLabelFilterKind.DoesNotEqual,
            "beginswith" or "begins_with" => PivotLabelFilterKind.BeginsWith,
            "endswith" or "ends_with" => PivotLabelFilterKind.EndsWith,
            "contains" => PivotLabelFilterKind.Contains,
            "doesnotcontain" or "does_not_contain" => PivotLabelFilterKind.DoesNotContain,
            "greaterthan" or "greater_than" => PivotLabelFilterKind.GreaterThan,
            "greaterthanorequal" or "greater_than_or_equal" => PivotLabelFilterKind.GreaterThanOrEqual,
            "lessthan" or "less_than" => PivotLabelFilterKind.LessThan,
            "lessthanorequal" or "less_than_or_equal" => PivotLabelFilterKind.LessThanOrEqual,
            "between" => PivotLabelFilterKind.Between,
            _ => PivotLabelFilterKind.Equals
        };
}
