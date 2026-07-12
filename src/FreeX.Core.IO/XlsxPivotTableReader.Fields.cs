using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableReader
{
    private static Dictionary<int, IReadOnlyList<string>> ReadNativePivotFieldSelections(
        XElement? pivotFieldsElement,
        PivotCacheModel? pivotCache,
        XNamespace workbookNs)
    {
        if (pivotFieldsElement is null || pivotCache is null)
            return [];

        var result = new Dictionary<int, IReadOnlyList<string>>();
        var pivotFields = pivotFieldsElement.Elements(workbookNs + "pivotField").ToList();
        for (var fieldIndex = 0; fieldIndex < pivotFields.Count && fieldIndex < pivotCache.Fields.Count; fieldIndex++)
        {
            var field = pivotCache.Fields[fieldIndex];
            var sharedItems = field.SharedItems;
            if (sharedItems is null || sharedItems.Count == 0)
                continue;

            // R30-io-pivot-cache-deep-2: XlsxPivotCacheReader.ReadSharedItemValues drops any <m/>
            // (missing/blank) OOXML sharedItems child before this list is built, shifting every later
            // item out of alignment with the raw OOXML index space the pivotField's own item @x attribute
            // is defined against. When the field's declared sharedItems @count (SharedItemCount) is larger
            // than this materialized list, at least one item was dropped and we can no longer tell which
            // materialized entry a given raw index now lands on -- indexing into it here would risk
            // silently hiding/keeping the wrong item. Decline to resolve a selection in that case, mirroring
            // the same guard ReadNativePageFieldSelectedItem already applies for the identical hazard.
            if (field.SharedItemCount is { } declaredCount && declaredCount > sharedItems.Count)
                continue;

            var hiddenIndexes = pivotFields[fieldIndex]
                .Element(workbookNs + "items")?
                .Elements(workbookNs + "item")
                .Where(item => XlsxXmlAttributeReader.ReadBoolAttribute(item, "hidden"))
                .Select(item => XlsxXmlAttributeReader.ReadIntAttribute(item, "x"))
                .Where(index => index.HasValue && index.Value >= 0 && index.Value < sharedItems.Count)
                .Select(index => index!.Value)
                .ToHashSet() ?? [];
            if (hiddenIndexes.Count == 0)
                continue;

            result[fieldIndex] = sharedItems
                .Where((_, itemIndex) => !hiddenIndexes.Contains(itemIndex))
                .ToList();
        }

        return result;
    }

    private static Dictionary<int, PivotFieldModel> ReadNativePivotFieldGroups(XElement? pivotFieldsElement, XNamespace workbookNs)
    {
        if (pivotFieldsElement is null)
            return [];

        var result = new Dictionary<int, PivotFieldModel>();
        var pivotFields = pivotFieldsElement.Elements(workbookNs + "pivotField").ToList();
        for (var fieldIndex = 0; fieldIndex < pivotFields.Count; fieldIndex++)
        {
            var rangePr = pivotFields[fieldIndex]
                .Element(workbookNs + "fieldGroup")?
                .Element(workbookNs + "rangePr");
            if (rangePr is null)
                continue;

            var grouping = ReadPivotFieldGrouping(rangePr.Attribute("groupBy")?.Value);
            if (grouping == PivotFieldGrouping.None && rangePr.Attribute("groupInterval") is not null)
                grouping = PivotFieldGrouping.NumberRange;
            if (grouping == PivotFieldGrouping.None)
                continue;

            result[fieldIndex] = new PivotFieldModel(
                fieldIndex,
                Grouping: grouping,
                GroupStart: XlsxXmlAttributeReader.ReadDoubleAttribute(rangePr, "startNum"),
                GroupEnd: XlsxXmlAttributeReader.ReadDoubleAttribute(rangePr, "endNum"),
                GroupInterval: XlsxXmlAttributeReader.ReadDoubleAttribute(rangePr, "groupInterval"));
        }

        return result;
    }

    private static Dictionary<int, PivotFieldModel> ReadNativePivotCacheFieldGroups(PivotCacheModel? pivotCache)
    {
        if (pivotCache is null)
            return [];

        var result = new Dictionary<int, PivotFieldModel>();
        for (var fieldIndex = 0; fieldIndex < pivotCache.Fields.Count; fieldIndex++)
        {
            var field = pivotCache.Fields[fieldIndex];
            if (field.Grouping == PivotFieldGrouping.None &&
                field.GroupStart is null &&
                field.GroupEnd is null &&
                field.GroupInterval is null)
            {
                continue;
            }

            result[fieldIndex] = new PivotFieldModel(
                fieldIndex,
                Grouping: field.Grouping,
                GroupStart: field.GroupStart,
                GroupEnd: field.GroupEnd,
                GroupInterval: field.GroupInterval);
        }

        return result;
    }

    private static Dictionary<int, PivotFieldNativeMetadata> ReadNativePivotFieldMetadata(
        XElement? pivotFieldsElement,
        XNamespace workbookNs)
    {
        if (pivotFieldsElement is null)
            return [];

        return pivotFieldsElement
            .Elements(workbookNs + "pivotField")
            .Select((field, index) => new KeyValuePair<int, PivotFieldNativeMetadata>(
                index,
                new PivotFieldNativeMetadata(
                    ReadOptionalBoolAttribute(field, "showAll"),
                    ReadOptionalBoolAttribute(field, "includeNewItemsInFilter"),
                    ReadOptionalBoolAttribute(field, "multipleItemSelectionAllowed"),
                    ReadOptionalBoolAttribute(field, "dragToRow"),
                    ReadOptionalBoolAttribute(field, "dragToCol"),
                    ReadOptionalBoolAttribute(field, "dragToPage"),
                    ReadOptionalBoolAttribute(field, "dragToData"),
                    ReadOptionalBoolAttribute(field, "showDropDowns"))))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static List<PivotFieldModel> ReadPivotFieldIndexes(
        XElement? fieldsElement,
        XNamespace workbookNs,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? nativeFieldSelections = null,
        IReadOnlyDictionary<int, PivotFieldModel>? nativeFieldGroups = null,
        IReadOnlyDictionary<int, PivotFieldNativeMetadata>? nativeFieldMetadata = null)
    {
        if (fieldsElement is null)
            return [];

        return fieldsElement
            .Elements(workbookNs + "field")
            .Select(field =>
            {
                var index = XlsxXmlAttributeReader.ReadIntAttribute(field, "x");
                return index.HasValue
                    ? CreatePivotFieldModel(
                        index.Value,
                        field.Attribute("name")?.Value,
                        ReadCsvAttribute(field.Attribute("selectedItems")?.Value) ?? ReadNativePivotFieldSelection(nativeFieldSelections, index.Value),
                        ReadPivotFieldGrouping(field.Attribute("groupBy")?.Value, ReadNativePivotFieldGroup(nativeFieldGroups, index.Value)?.Grouping ?? PivotFieldGrouping.None),
                        XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupStart") ?? ReadNativePivotFieldGroup(nativeFieldGroups, index.Value)?.GroupStart,
                        XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupEnd") ?? ReadNativePivotFieldGroup(nativeFieldGroups, index.Value)?.GroupEnd,
                        XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupInterval") ?? ReadNativePivotFieldGroup(nativeFieldGroups, index.Value)?.GroupInterval,
                        ReadNativePivotFieldMetadata(nativeFieldMetadata, index.Value))
                    : null;
            })
            .Where(field => field is not null)
            .Select(field => field!)
            .ToList();
    }

    private static List<PivotFieldModel> ReadPivotPageFields(
        XElement? fieldsElement,
        PivotCacheModel? pivotCache,
        XNamespace workbookNs,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? nativeFieldSelections = null,
        IReadOnlyDictionary<int, PivotFieldModel>? nativeFieldGroups = null,
        IReadOnlyDictionary<int, PivotFieldNativeMetadata>? nativeFieldMetadata = null)
    {
        if (fieldsElement is null)
            return [];

        var pageFields = fieldsElement
            .Elements(workbookNs + "pageField")
            .Select(field =>
            {
                var fieldIndex = XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1;
                return CreatePivotFieldModel(
                    fieldIndex,
                    field.Attribute("name")?.Value ?? ReadNativePageFieldSelectedItem(field, pivotCache, fieldIndex),
                    ReadCsvAttribute(field.Attribute("selectedItems")?.Value) ?? ReadNativePivotFieldSelection(nativeFieldSelections, fieldIndex),
                    ReadPivotFieldGrouping(field.Attribute("groupBy")?.Value, ReadNativePivotFieldGroup(nativeFieldGroups, fieldIndex)?.Grouping ?? PivotFieldGrouping.None),
                    XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupStart") ?? ReadNativePivotFieldGroup(nativeFieldGroups, fieldIndex)?.GroupStart,
                    XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupEnd") ?? ReadNativePivotFieldGroup(nativeFieldGroups, fieldIndex)?.GroupEnd,
                    XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupInterval") ?? ReadNativePivotFieldGroup(nativeFieldGroups, fieldIndex)?.GroupInterval,
                    ReadNativePivotFieldMetadata(nativeFieldMetadata, fieldIndex));
            })
            .Where(field => field.SourceFieldIndex >= 0)
            .ToList();
        if (pageFields.Count > 0)
            return pageFields;

        return ReadPivotFieldIndexes(fieldsElement, workbookNs, nativeFieldSelections, nativeFieldGroups, nativeFieldMetadata);
    }

    private static string? ReadNativePageFieldSelectedItem(
        XElement pageField,
        PivotCacheModel? pivotCache,
        int fieldIndex)
    {
        if (pivotCache is null ||
            fieldIndex < 0 ||
            fieldIndex >= pivotCache.Fields.Count ||
            pivotCache.Fields[fieldIndex].SharedItems is not { Count: > 0 } sharedItems)
        {
            return null;
        }

        // R26-io-pivot-deep-1: XlsxPivotCacheReader.ReadSharedItemValues drops any <m/> (missing/blank)
        // OOXML sharedItems child before this list is built, shifting every later item out of alignment
        // with the raw OOXML index space the pageField's own @item attribute is defined against. When the
        // field's declared sharedItems @count (SharedItemCount) is larger than this materialized list,
        // at least one item was dropped and we can no longer tell which (if any) materialized entry the
        // raw index now lands on -- indexing into it here would risk silently returning a caption for the
        // WRONG item. Decline to resolve a name in that case (the same safe "no name" outcome the
        // out-of-range branch below already produces) rather than ever returning a caption Excel did not
        // intend.
        var field = pivotCache.Fields[fieldIndex];
        if (field.SharedItemCount is { } declaredCount && declaredCount > sharedItems.Count)
            return null;

        var itemIndex = XlsxXmlAttributeReader.ReadIntAttribute(pageField, "item");
        return itemIndex is >= 0 && itemIndex.Value < sharedItems.Count
            ? sharedItems[itemIndex.Value]
            : null;
    }

    private static PivotFieldModel CreatePivotFieldModel(
        int sourceFieldIndex,
        string? selectedItem,
        IReadOnlyList<string>? selectedItems,
        PivotFieldGrouping grouping,
        double? groupStart,
        double? groupEnd,
        double? groupInterval,
        PivotFieldNativeMetadata? metadata) =>
        new(
            sourceFieldIndex,
            selectedItem,
            selectedItems,
            grouping,
            groupStart,
            groupEnd,
            groupInterval,
            metadata?.ShowAll,
            metadata?.IncludeNewItemsInFilter,
            metadata?.MultipleItemSelectionAllowed,
            metadata?.DragToRow,
            metadata?.DragToColumn,
            metadata?.DragToPage,
            metadata?.DragToData,
            metadata?.ShowDropDowns);

    private static IReadOnlyList<string>? ReadNativePivotFieldSelection(
        IReadOnlyDictionary<int, IReadOnlyList<string>>? nativeFieldSelections,
        int fieldIndex) =>
        nativeFieldSelections is not null && nativeFieldSelections.TryGetValue(fieldIndex, out var selectedItems)
            ? selectedItems
            : null;

    private static PivotFieldModel? ReadNativePivotFieldGroup(
        IReadOnlyDictionary<int, PivotFieldModel>? nativeFieldGroups,
        int fieldIndex) =>
        nativeFieldGroups is not null && nativeFieldGroups.TryGetValue(fieldIndex, out var field)
            ? field
            : null;

    private static PivotFieldNativeMetadata? ReadNativePivotFieldMetadata(
        IReadOnlyDictionary<int, PivotFieldNativeMetadata>? metadataByField,
        int fieldIndex) =>
        metadataByField is not null && metadataByField.TryGetValue(fieldIndex, out var metadata)
            ? metadata
            : null;

    private static bool? ReadOptionalBoolAttribute(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        if (value is null)
            return null;
        return value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PivotFieldNativeMetadata(
        bool? ShowAll,
        bool? IncludeNewItemsInFilter,
        bool? MultipleItemSelectionAllowed,
        bool? DragToRow,
        bool? DragToColumn,
        bool? DragToPage,
        bool? DragToData,
        bool? ShowDropDowns);
}
