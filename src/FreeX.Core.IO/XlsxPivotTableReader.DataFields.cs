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
                    calculatedFields.FirstOrDefault(calculated => string.Equals(calculated.Name, field.Attribute("name")?.Value, StringComparison.OrdinalIgnoreCase))?.Name;
                return new PivotDataFieldModel(
                    calculatedFieldName is null ? fieldIndex : -1,
                    field.Attribute("name")?.Value ?? "",
                    field.Attribute("subtotal")?.Value ?? "sum",
                    numberFormatId,
                    calculatedFieldName,
                    ReadPivotShowValuesAs(field.Attribute("showValuesAs")?.Value),
                    XlsxXmlAttributeReader.ReadIntAttribute(field, "baseField"),
                    field.Attribute("baseItem")?.Value,
                    numberFormatId is not null && numberFormatCatalog.TryGetValue(numberFormatId.Value, out var formatCode)
                        ? formatCode
                        : null);
            })
            .Where(field => field.SourceFieldIndex >= 0 || field.CalculatedFieldName is not null)
            .ToList();
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

    private static List<PivotCalculatedItemModel> ReadPivotCalculatedItems(XElement? calculatedItemsElement, XNamespace workbookNs)
    {
        if (calculatedItemsElement is null)
            return [];

        return calculatedItemsElement
            .Elements(workbookNs + "calculatedItem")
            .Select(item => new PivotCalculatedItemModel(
                XlsxXmlAttributeReader.ReadIntAttribute(item, "field") ?? -1,
                item.Attribute("name")?.Value ?? "",
                item.Attribute("formula")?.Value ?? ""))
            .Where(item => item.SourceFieldIndex >= 0 && !string.IsNullOrWhiteSpace(item.Name))
            .ToList();
    }
}
