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

            var items = pivotFields[fieldIndex]
                .Element(workbookNs + "items")?
                .Elements(workbookNs + "item")
                .ToList() ?? [];

            HashSet<int>? hiddenIndexes;
            if (field.SharedItemCount is { } declaredCount && declaredCount > sharedItems.Count)
            {
                // R30-io-pivot-cache-deep-2 / R31-meta-2: XlsxPivotCacheReader.ReadSharedItemValues drops
                // any <m/> (missing/blank) OOXML sharedItems child before this list is built, shifting
                // every later item out of alignment with the raw OOXML index space the pivotField's own
                // item @x attribute is defined against. That misalignment is common (any field with a
                // blank source cell triggers it), so unconditionally declining to resolve a selection here
                // -- as the original fix did -- silently disabled hidden-item filtering for the ordinary
                // case, not just the narrow ambiguous one. Instead, reconstruct the raw-index ->
                // materialized-index mapping from the pivotField's own <items> list: each <item> carries
                // its own "m" (missing) flag independent of its shared-item index, so when every raw index
                // 0..declaredCount-1 is accounted for we can tell exactly how many missing items precede a
                // given real one and thus its true position in the filtered SharedItems list, without ever
                // needing to see the pivot cache's raw sharedItems XML. Only decline (return null) when
                // that reconstruction is not possible (an incomplete/partial <items> list) -- that is the
                // genuine ambiguity case where we truly cannot tell which materialized entry a raw index
                // now lands on.
                hiddenIndexes = TryResolveHiddenIndexesAcrossMissingSharedItems(items, declaredCount, sharedItems.Count);
            }
            else
            {
                hiddenIndexes = items
                    .Where(item => XlsxXmlAttributeReader.ReadBoolAttribute(item, "hidden"))
                    .Select(item => XlsxXmlAttributeReader.ReadIntAttribute(item, "x"))
                    .Where(index => index.HasValue && index.Value >= 0 && index.Value < sharedItems.Count)
                    .Select(index => index!.Value)
                    .ToHashSet();
            }

            if (hiddenIndexes is null || hiddenIndexes.Count == 0)
                continue;

            result[fieldIndex] = sharedItems
                .Where((_, itemIndex) => !hiddenIndexes.Contains(itemIndex))
                .ToList();
        }

        return result;
    }

    /// <summary>
    /// Reconstructs the raw OOXML shared-item index (which includes dropped &lt;m/&gt; blank entries) to
    /// materialized <see cref="PivotCacheFieldModel.SharedItems"/> index mapping purely from the
    /// pivotField's own &lt;items&gt; list, using each &lt;item&gt;'s "m" (missing) flag to identify which
    /// raw indices were blank -- no access to the pivot cache's raw sharedItems XML required. Returns null
    /// when the list does not account for every raw index in 0..declaredCount-1 exactly once (an
    /// incomplete/partial &lt;items&gt; list): in that case which raw indices are missing vs. real cannot
    /// be determined, and resolving would risk hiding the wrong item.
    /// </summary>
    private static HashSet<int>? TryResolveHiddenIndexesAcrossMissingSharedItems(
        List<XElement> items,
        int declaredCount,
        int materializedCount)
    {
        var seenRawIndexes = new HashSet<int>();
        var realRawIndexes = new List<int>();
        var missingRawIndexCount = 0;
        var hiddenRealRawIndexes = new HashSet<int>();
        foreach (var item in items)
        {
            var rawIndex = XlsxXmlAttributeReader.ReadIntAttribute(item, "x");
            if (rawIndex is not { } index)
            {
                // An <item> with no "x" attribute is the trailing default/subtotal marker
                // (<item t="default"/>) that Excel -- and FreeX's own writer,
                // XlsxPivotTableWriter.cs:443-445 -- always appends after enumerating the real
                // per-value items. It has no corresponding raw shared-item index at all, so it is
                // not part of the raw-index space we are reconstructing and must simply be
                // skipped, not treated as an ambiguity that invalidates the whole reconstruction.
                continue;
            }

            if (index < 0 || index >= declaredCount || !seenRawIndexes.Add(index))
                return null;

            if (XlsxXmlAttributeReader.ReadBoolAttribute(item, "m"))
            {
                // This raw index is the blank/missing bucket itself, not a real value -- it has no
                // corresponding entry in SharedItems, so it is never a candidate to hide there even if
                // the item is itself flagged hidden.
                missingRawIndexCount++;
            }
            else
            {
                realRawIndexes.Add(index);
                if (XlsxXmlAttributeReader.ReadBoolAttribute(item, "hidden"))
                    hiddenRealRawIndexes.Add(index);
            }
        }

        // Only trust the reconstruction when every raw index in the declared space is accounted for
        // exactly once and the real/missing split matches the declared counts -- otherwise we cannot be
        // sure which raw indices are missing vs. real, and mapping would risk hiding the wrong item.
        if (seenRawIndexes.Count != declaredCount ||
            realRawIndexes.Count != materializedCount ||
            missingRawIndexCount != declaredCount - materializedCount)
        {
            return null;
        }

        realRawIndexes.Sort();
        var materializedIndexByRawIndex = new Dictionary<int, int>(realRawIndexes.Count);
        for (var rank = 0; rank < realRawIndexes.Count; rank++)
            materializedIndexByRawIndex[realRawIndexes[rank]] = rank;

        var hiddenIndexes = new HashSet<int>();
        foreach (var rawIndex in hiddenRealRawIndexes)
        {
            if (materializedIndexByRawIndex.TryGetValue(rawIndex, out var materializedIndex))
                hiddenIndexes.Add(materializedIndex);
        }

        return hiddenIndexes;
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
                    ReadOptionalBoolAttribute(field, "showDropDowns"),
                    // R75-io-pivottable-layout-4-2: this field's own defaultSubtotal/subtotalTop, kept
                    // separate from PivotTableModel.ShowSubtotals/SubtotalPlacement (previously read only
                    // off the FIRST axis field via FindFirstPivotFieldElement, collapsing every axis
                    // field's own setting into one table-wide value).
                    ReadOptionalBoolAttribute(field, "defaultSubtotal"),
                    ReadOptionalBoolAttribute(field, "subtotalTop") is { } subtotalTop
                        ? subtotalTop ? PivotSubtotalPlacement.Top : PivotSubtotalPlacement.Bottom
                        : null,
                    // R75-io-pivottable-layout-4-3: this field's own compact/outline report form, kept
                    // separate from PivotTableModel.ReportLayout (previously only a table-wide value).
                    ReadPivotFieldReportLayout(field))))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    // Sibling of XlsxPivotTableReader.Converters.cs's ReadPivotReportLayout(XElement root), scoped to a
    // single <pivotField> rather than the <pivotTableDefinition> root. Only axis (row/column) fields ever
    // carry these attributes (XlsxPivotTableWriter.cs only emits them for isAxisField); a field with
    // neither attribute returns null so the table-wide PivotTableModel.ReportLayout is used instead.
    private static PivotReportLayout? ReadPivotFieldReportLayout(XElement field)
    {
        if (field.Attribute("compact") is null && field.Attribute("outline") is null)
            return null;

        if (XlsxXmlAttributeReader.ReadBoolAttribute(field, "compact", defaultValue: true))
            return PivotReportLayout.Compact;

        return XlsxXmlAttributeReader.ReadBoolAttribute(field, "outline")
            ? PivotReportLayout.Outline
            : PivotReportLayout.Tabular;
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
                // R52-io-pivot-layout-3-1: x="-2" is the OOXML placeholder marking where the "Σ Values"
                // pseudo-field sits within this axis (rowFields/colFields) whenever a pivot has 2+ data
                // fields -- it has no corresponding pivot-cache field. PivotTableModel.DataOnRows already
                // conveys whether the Values pseudo-column/row is on rows or columns, so this marker carries
                // no information FreeX's model needs. Treating it as a real SourceFieldIndex corrupts any
                // downstream consumer that indexes per-cache-field arrays/dictionaries by SourceFieldIndex
                // (e.g. PivotTableRefreshService.Writers.cs `headers[rowFields[index].SourceFieldIndex]`),
                // so it must be skipped here rather than modeled as a normal field.
                return index.HasValue && index.Value != -2
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
            metadata?.ShowDropDowns,
            IsUnplacedFilterField: false,
            GroupStartDate: null,
            GroupEndDate: null,
            ShowSubtotals: metadata?.ShowSubtotals,
            SubtotalPlacement: metadata?.SubtotalPlacement,
            ReportLayout: metadata?.ReportLayout);

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
        bool? ShowDropDowns,
        bool? ShowSubtotals,
        PivotSubtotalPlacement? SubtotalPlacement,
        PivotReportLayout? ReportLayout);
}
