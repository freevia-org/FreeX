using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Shared authoring of a pivot slicer cache's native
/// <c>&lt;data&gt;&lt;tabular pivotCacheId="N"&gt;&lt;items&gt;&lt;i x=".." s="1"/&gt;...&gt;</c> selection
/// list — the ONLY form real Excel (and FreeX's own reload, via
/// <c>XlsxSlicerTimelineMetadataReader.ReadSlicerCacheItems</c> → <see cref="SlicerModel.CacheItems"/>)
/// draws a slicer's item/button tiles from. Extracted so BOTH code paths that author a fresh pivot slicer
/// cache use identical logic:
/// <list type="bullet">
/// <item>the no-source-package writer (<see cref="XlsxSlicerTimelineWriter"/>) on a fresh save, and</item>
/// <item>the source-preserved rewriter's brand-new-control authoring
///   (<see cref="XlsxSlicerTimelineStateRewriter"/>.AppendNewControls), so a pivot slicer inserted into an
///   already-loaded workbook (via AddSlicerCommand) gets the same native item list — instead of rendering
///   with zero item buttons on reload (R44-io-pivot-filter-page-3-2), including the required
///   <c>pivotCacheId</c> attribute (R83-io-slicer-tabular-pivotcacheid).</item>
/// </list>
/// </summary>
internal static class XlsxPivotSlicerCacheData
{
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    // P14 (R44-io-pivot-filter-page-3-2): builds the native <data><tabular><items><i x="N" s="1"/>...>
    // list a pivot slicer cache needs to render item tiles (mirrors
    // XlsxSlicerTimelineMetadataReader.ReadSlicerCacheItems's read shape exactly -- x is the 0-based
    // index into the pivot cache field's shared items, s="1" marks a selected item, and both the writers
    // and that reader agree an absent @s means "not selected"). Resolves the SPECIFIC owning
    // pivot cache + field (bound cache first, then a name-only fallback scan; see
    // ResolveSlicerSharedItemsField), and stamps the tabular element's required pivotCacheId from that
    // cache's id (R83-io-slicer-tabular-pivotcacheid). Returns null when the field can't be resolved
    // (e.g. a table slicer with no bound pivot cache field, or a slicer authored against a cache FreeX
    // doesn't model shared items for yet) so the cache definition is left exactly as it would be without
    // this element in that case.
    public static XElement? BuildPivotSlicerCacheDataElement(Workbook workbook, SlicerModel slicer)
    {
        if (ResolveSlicerSharedItemsField(workbook, slicer) is not { } binding ||
            binding.Field.SharedItems is not { Count: > 0 } sharedItems)
        {
            return null;
        }

        var ownerCache = binding.Cache;

        // No explicit selection recorded on the model (slicer.SelectedItems empty) means the
        // unfiltered "(All items selected)" state -- every tile starts selected, matching how Excel
        // itself initializes a freshly inserted slicer's cache.
        var selectedItems = slicer.SelectedItems.Count > 0
            ? new HashSet<string>(slicer.SelectedItems, StringComparer.OrdinalIgnoreCase)
            : null;

        var items = new List<XElement>(sharedItems.Count);
        for (var index = 0; index < sharedItems.Count; index++)
        {
            var isSelected = selectedItems is null || selectedItems.Contains(sharedItems[index]);
            items.Add(new XElement(
                SlicerNs + "i",
                new XAttribute("x", index.ToString(CultureInfo.InvariantCulture)),
                isSelected ? new XAttribute("s", "1") : null));
        }

        return new XElement(
            SlicerNs + "data",
            new XElement(
                SlicerNs + "tabular",
                // R83-io-slicer-tabular-pivotcacheid: CT_TabularSlicerCache (the x14-namespace slicer
                // cache type, ECMA-376 / [MS-XLSX] §2.3.2.1.2) requires a pivotCacheId attribute --
                // without it OpenXmlValidator(Microsoft365) reports "The required attribute
                // 'pivotCacheId' is missing" and real Excel can repair (silently drop) the slicer cache
                // on open. Emit the OWNING pivot cache's own CacheId: it is the same stable, positive
                // identifier the workbook's <pivotCache cacheId="N"> already uses for this cache, so it
                // satisfies both the schema's unsignedInt type and the spec rule that a slicer's
                // pivotCacheId "uniquely identifies this PivotCache" and MUST NOT be 0. (FreeX does not
                // emit the optional workbook-level x14:pivotCaches list, so there is no separate x14
                // numbering this must instead index into.)
                new XAttribute("pivotCacheId", ownerCache.CacheId.ToString(CultureInfo.InvariantCulture)),
                new XElement(
                    SlicerNs + "items",
                    new XAttribute("count", items.Count.ToString(CultureInfo.InvariantCulture)),
                    items)));
    }

    // R58-io-slicer-timeline-6-1: resolve the SPECIFIC pivot cache this slicer is bound to (via
    // SourcePivotTableName -> PivotTableModel.CacheId -> PivotCacheModel) before falling back to a
    // name-only scan across every cache in the workbook. Two independent pivot tables can each carry a
    // field with the same name but different shared-item lists; a name-only scan in collection order
    // would pick whichever cache happens to come first, authoring the wrong item/selection list for a
    // freshly inserted slicer whenever its bound cache isn't first.
    // Returns BOTH the resolved field and the specific PivotCacheModel it lives in: the caller needs the
    // owning cache's id to stamp the tabular slicer cache's required pivotCacheId attribute
    // (R83-io-slicer-tabular-pivotcacheid), and only this resolution knows which cache the field's shared
    // items actually came from (bound cache first, then a name-only fallback scan).
    //
    // R120: the name-only fallback scan is reserved STRICTLY for the case where boundCache itself could
    // not be resolved at all (SourcePivotTableName absent/stale on the slicer model). Once boundCache
    // resolves successfully, it is authoritative for this slicer -- even when its own field carries no
    // enumerated SharedItems (the standard OOXML shape for a purely numeric pivot field, where Excel
    // writes only containsNumber/minValue/maxValue on <sharedItems> and omits per-value <n> children).
    // Widening the search to every OTHER cache in that case would return an unrelated cache/field pair
    // that merely happens to share the field NAME, and the caller would then stamp that wrong cache's
    // CacheId as the tabular element's pivotCacheId while the sibling <x14:pivotTables> element still
    // (correctly) names the originally-bound pivot table -- an internally self-contradictory
    // slicerCacheDefinition that real Excel can flag for repair on open.
    public static (PivotCacheModel Cache, PivotCacheFieldModel Field)? ResolveSlicerSharedItemsField(
        Workbook workbook,
        SlicerModel slicer)
    {
        var sourceFieldName = slicer.SourceFieldName;
        if (string.IsNullOrWhiteSpace(sourceFieldName))
            return null;

        var boundCache = ResolveSlicerBoundPivotCache(workbook, slicer.SourcePivotTableName);
        if (boundCache is not null)
        {
            var boundField = boundCache.Fields.FirstOrDefault(field =>
                string.Equals(field.Name, sourceFieldName, StringComparison.OrdinalIgnoreCase) &&
                field.SharedItems is { Count: > 0 });

            // Whether or not a shared-items-bearing field was found, boundCache is the slicer's actual
            // binding: never fall through to the cross-cache scan below once it has resolved. Returning
            // null here (rather than scanning) is what BuildPivotSlicerCacheDataElement already treats as
            // "no native <data> element to author" -- correct for a numeric field with no enumerated
            // items, matching what happens when no cache anywhere has shared items for this name.
            return boundField is not null ? (boundCache, boundField) : null;
        }

        foreach (var cache in workbook.PivotCaches)
        {
            foreach (var field in cache.Fields)
            {
                if (string.Equals(field.Name, sourceFieldName, StringComparison.OrdinalIgnoreCase) &&
                    field.SharedItems is { Count: > 0 })
                {
                    return (cache, field);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the specific <see cref="PivotCacheModel"/> backing <paramref name="sourcePivotTableName"/>,
    /// mirroring the slicer/timeline writers' name-based pivot table lookup: find the
    /// <see cref="PivotTableModel"/> with that name across every sheet, then match its
    /// <see cref="PivotTableModel.CacheId"/> to a <see cref="PivotCacheModel"/> in
    /// <see cref="Workbook.PivotCaches"/>. Returns <see langword="null"/> when the name is absent or
    /// unresolvable so callers can fall back to the legacy name-only scan.
    /// </summary>
    public static PivotCacheModel? ResolveSlicerBoundPivotCache(Workbook workbook, string? sourcePivotTableName)
    {
        if (string.IsNullOrWhiteSpace(sourcePivotTableName))
            return null;

        PivotTableModel? boundPivotTable = null;
        foreach (var sheet in workbook.Sheets)
        {
            boundPivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
                string.Equals(pivot.Name, sourcePivotTableName, StringComparison.OrdinalIgnoreCase));
            if (boundPivotTable is not null)
                break;
        }

        if (boundPivotTable is null)
            return null;

        return workbook.PivotCaches.FirstOrDefault(cache => cache.CacheId == boundPivotTable.CacheId);
    }
}
