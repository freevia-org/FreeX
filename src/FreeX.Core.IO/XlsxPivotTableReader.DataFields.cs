using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableReader
{
    private static List<PivotDataFieldModel> ReadPivotDataFields(
        XElement? dataFieldsElement,
        XNamespace workbookNs,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyDictionary<int, string> calculatedFieldNamesByIndex,
        IReadOnlyDictionary<int, string> numberFormatCatalog)
    {
        if (dataFieldsElement is null)
            return [];

        return dataFieldsElement
            .Elements(workbookNs + "dataField")
            .Select(field =>
            {
                var fieldIndex = XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1;
                var numberFormatId = XlsxXmlAttributeReader.ReadIntAttribute(field, "numFmtId");
                var calculatedFieldName = field.Attribute("calculatedField")?.Value ??
                    (calculatedFieldNamesByIndex.TryGetValue(fieldIndex, out var indexedCalculatedFieldName) ? indexedCalculatedFieldName : null) ??
                    FindCalculatedFieldName(calculatedFields, field.Attribute("name")?.Value);
                return new PivotDataFieldModel(
                    calculatedFieldName is null ? fieldIndex : -1,
                    field.Attribute("name")?.Value ?? "",
                    field.Attribute("subtotal")?.Value ?? "sum",
                    numberFormatId,
                    calculatedFieldName,
                    // CT_DataField's real OOXML attribute is showDataAs (ST_ShowDataAs), not showValuesAs --
                    // see ReadPivotShowDataAs below. The earlier showValuesAs name/tokens were a FreeX-only
                    // invention real Excel never writes or recognizes, so "Show Values As" silently vanished
                    // on any exchange with a real Excel file (R36-io-pivot-cache-2-1). showDataAs is read
                    // first as the primary (real-Excel) attribute; when absent we fall back to the legacy
                    // FreeX-only showValuesAs attribute so pivots saved by pre-r36 FreeX builds still load
                    // their Show-Values-As setting (R37-meta-1).
                    field.Attribute("showDataAs") is { } showDataAsAttribute
                        ? ReadPivotShowDataAs(showDataAsAttribute.Value)
                        : ReadPivotShowValuesAs(field.Attribute("showValuesAs")?.Value),
                    XlsxXmlAttributeReader.ReadIntAttribute(field, "baseField"),
                    field.Attribute("baseItem")?.Value,
                    numberFormatId is not null && numberFormatCatalog.TryGetValue(numberFormatId.Value, out var formatCode)
                        ? formatCode
                        : null);
            })
            .Where(field => field.SourceFieldIndex >= 0 || field.CalculatedFieldName is not null)
            .ToList();
    }

    // Maps CT_DataField's real ST_ShowDataAs tokens (ECMA-376 18.18.72) to PivotShowValuesAs. "percent"
    // (Excel's "% Of" mode, relative to a single base item with no grand/row/col-total semantics) has no
    // dedicated model slot yet and intentionally falls through to None, same as an absent/"normal" attribute.
    private static PivotShowValuesAs ReadPivotShowDataAs(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "difference" => PivotShowValuesAs.DifferenceFrom,
            "percentdiff" => PivotShowValuesAs.PercentDifferenceFrom,
            "runtotal" => PivotShowValuesAs.RunningTotalIn,
            "percentofrow" => PivotShowValuesAs.PercentOfRowTotal,
            "percentofcol" => PivotShowValuesAs.PercentOfColumnTotal,
            "percentoftotal" => PivotShowValuesAs.PercentOfGrandTotal,
            "index" => PivotShowValuesAs.Index,
            "percentofparent" => PivotShowValuesAs.PercentOfParentTotal,
            "percentofparentrow" => PivotShowValuesAs.PercentOfParentRowTotal,
            "percentofparentcol" => PivotShowValuesAs.PercentOfParentColumnTotal,
            "rankascending" => PivotShowValuesAs.RankSmallest,
            "rankdescending" => PivotShowValuesAs.RankLargest,
            _ => PivotShowValuesAs.None
        };

    private static string? FindCalculatedFieldName(
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        string? fieldName)
    {
        foreach (var calculatedField in calculatedFields)
        {
            if (string.Equals(calculatedField.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                return calculatedField.Name;
        }

        return null;
    }

    private static Dictionary<int, string> ReadPivotCalculatedFieldNamesByIndex(
        XElement? calculatedFieldsElement,
        XNamespace workbookNs,
        PivotCacheModel? pivotCache)
    {
        var result = ReadPivotCacheCalculatedFieldNamesByIndex(pivotCache);
        if (calculatedFieldsElement is null)
            return result;

        foreach (var field in calculatedFieldsElement.Elements(workbookNs + "calculatedField"))
        {
            var index = XlsxXmlAttributeReader.ReadIntAttribute(field, "fld");
            var name = field.Attribute("name")?.Value;
            if (index is null || index < 0 || string.IsNullOrWhiteSpace(name))
                continue;

            result.TryAdd(index.Value, name);
        }

        return result;
    }

    private static Dictionary<int, string> ReadPivotCacheCalculatedFieldNamesByIndex(PivotCacheModel? pivotCache)
    {
        if (pivotCache is null || pivotCache.Fields.Count == 0)
            return [];

        var result = new Dictionary<int, string>();
        for (var index = 0; index < pivotCache.Fields.Count; index++)
        {
            var field = pivotCache.Fields[index];
            if ((field.IsDatabaseField && string.IsNullOrWhiteSpace(field.Formula)) ||
                string.IsNullOrWhiteSpace(field.Name))
            {
                continue;
            }

            result[index] = field.Name;
        }

        return result;
    }

    private static List<PivotCalculatedFieldModel> ReadPivotCalculatedFields(
        XElement? calculatedFieldsElement,
        XNamespace workbookNs,
        PivotCacheModel? pivotCache)
    {
        var fields = ReadPivotCacheCalculatedFields(pivotCache);
        var names = fields
            .Select(field => field.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (calculatedFieldsElement is null)
            return fields;

        fields.AddRange(calculatedFieldsElement
            .Elements(workbookNs + "calculatedField")
            .Select(field => new PivotCalculatedFieldModel(
                field.Attribute("name")?.Value ?? "",
                field.Attribute("formula")?.Value ?? ""))
            .Where(field => !string.IsNullOrWhiteSpace(field.Name) && names.Add(field.Name)));
        return fields;
    }

    private static List<PivotCalculatedFieldModel> ReadPivotCacheCalculatedFields(PivotCacheModel? pivotCache)
    {
        if (pivotCache is null || pivotCache.Fields.Count == 0)
            return [];

        return pivotCache.Fields
            .Where(field => (!field.IsDatabaseField || !string.IsNullOrWhiteSpace(field.Formula)) &&
                            !string.IsNullOrWhiteSpace(field.Name))
            .Select(field => new PivotCalculatedFieldModel(field.Name, field.Formula ?? ""))
            .ToList();
    }

    // R116-io-pivot-calcitem-part: reads a <calculatedItems> element regardless of which part it came
    // from -- the corrected pivotCacheDefinitionN.xml location (XlsxPivotCacheReader.Load), OR the
    // pre-R116 pivotTableDefinitionN.xml location kept ONLY as a backward-compat fallback so a file this
    // codebase itself saved before this fix still round-trips its calculated items in FreeX (real Excel
    // already silently repairs/drops that schema-invalid placement on open, so no external file is lost
    // by no longer looking there once the cache-part list is present). A calculatedItem's display Name
    // has no real-schema attribute home (see ToPivotCacheCalculatedItemsXml); prefer the legacy "name"
    // attribute the old (buggy) writer emitted, falling back to the new per-item extLst FreeX now writes.
    internal static List<PivotCalculatedItemModel> ReadPivotCalculatedItems(XElement? calculatedItemsElement, XNamespace workbookNs)
    {
        if (calculatedItemsElement is null)
            return [];

        return calculatedItemsElement
            .Elements(workbookNs + "calculatedItem")
            .Select(item => new PivotCalculatedItemModel(
                XlsxXmlAttributeReader.ReadIntAttribute(item, "field") ?? -1,
                item.Attribute("name")?.Value
                    ?? XlsxPivotExtensionReader.ReadElement(item, workbookNs, "calculatedItemProps")?.Attribute("name")?.Value
                    ?? "",
                item.Attribute("formula")?.Value ?? ""))
            .Where(item => item.SourceFieldIndex >= 0 && !string.IsNullOrWhiteSpace(item.Name))
            .ToList();
    }
}
