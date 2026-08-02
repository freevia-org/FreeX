using FreeX.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxPivotCacheReader
{
    private const string PivotCacheRecordsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";

    public static List<PivotCacheModel> Load(
        ZipArchive archive,
        XDocument workbookXml,
        IReadOnlyDictionary<string, string> workbookRels,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        var result = new List<PivotCacheModel>();
        var seenCacheIds = new HashSet<int>();
        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pivotCacheElement in workbookXml.Root?
                     .Element(workbookNs + "pivotCaches")?
                     .Elements(workbookNs + "pivotCache") ?? [])
        {
            var cacheIdAttribute = XlsxXmlAttributeReader.ReadIntAttribute(pivotCacheElement, "cacheId");
            var relId = pivotCacheElement.Attribute(relNs + "id")?.Value.Trim();
            if (cacheIdAttribute is null or < 0 ||
                string.IsNullOrWhiteSpace(relId) ||
                !seenCacheIds.Add(cacheIdAttribute.Value) ||
                !seenRelationshipIds.Add(relId) ||
                !workbookRels.TryGetValue(relId, out var cachePath))
            {
                continue;
            }

            var cacheEntry = archive.GetEntry(cachePath);
            if (cacheEntry is null)
                continue;

            var cacheXml = LoadXml(cacheEntry);
            var root = cacheXml.Root;
            if (root is null)
                continue;

            var cacheSource = root.Element(workbookNs + "cacheSource");
            var worksheetSource = cacheSource?.Element(workbookNs + "worksheetSource");
            // FreeX persists preserveSourceSortFilter / the ISO refreshed date in an extLst extension
            // (they have no base-schema attribute home). Prefer the extension, then any legacy attribute.
            var freeXProps = ReadFreeXCacheProps(root, workbookNs);
            var sourceType = GetSourceType(cacheSource, worksheetSource);
            var cache = new PivotCacheModel
            {
                CacheId = cacheIdAttribute.Value,
                SourceType = sourceType,
                SourceSheetName = worksheetSource?.Attribute("sheet")?.Value,
                SourceReference = worksheetSource?.Attribute("ref")?.Value,
                SourceTableName = worksheetSource?.Attribute("name")?.Value,
                ConnectionId = cacheSource is null ? null : XlsxXmlAttributeReader.ReadIntAttribute(cacheSource, "connectionId"),
                IsOlap = XlsxXmlAttributeReader.ReadBoolAttribute(root, "olap"),
                PackagePart = cachePath,
                RefreshOnLoad = XlsxXmlAttributeReader.ReadBoolAttribute(root, "refreshOnLoad", defaultValue: false),
                SaveData = XlsxXmlAttributeReader.ReadBoolAttribute(root, "saveData", defaultValue: true),
                EnableRefresh = XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableRefresh", defaultValue: true),
                PreserveSourceSortFilter = freeXProps?.Attribute("preserveSourceSortFilter") is { } preserveAttr
                    ? !string.Equals(preserveAttr.Value, "0", StringComparison.Ordinal)
                    : XlsxXmlAttributeReader.ReadBoolAttribute(root, "preserveSourceSortFilter", defaultValue: true),
                MissingItemsLimit = XlsxXmlAttributeReader.ReadIntAttribute(root, "missingItemsLimit"),
                RecordCount = XlsxXmlAttributeReader.ReadIntAttribute(root, "recordCount"),
                CreatedVersion = XlsxXmlAttributeReader.ReadIntAttribute(root, "createdVersion"),
                MinRefreshableVersion = XlsxXmlAttributeReader.ReadIntAttribute(root, "minRefreshableVersion"),
                RefreshedVersion = XlsxXmlAttributeReader.ReadIntAttribute(root, "refreshedVersion"),
                RefreshedBy = root.Attribute("refreshedBy")?.Value,
                RefreshedDateIso = freeXProps?.Attribute("refreshedDateIso")?.Value ?? root.Attribute("refreshedDateIso")?.Value,
                // External/Consolidation/Scenario caches have no live worksheet range the writer can
                // regenerate <r> records from on re-save, so capture the original cached rows verbatim
                // here to hand back as passthrough (R91-io-external-data-model-5-1).
                RawRecordsXml = sourceType is PivotCacheSourceType.External or PivotCacheSourceType.Consolidation or PivotCacheSourceType.Scenario
                    ? TryReadRawPivotCacheRecordsXml(archive, cachePath)
                    : null
            };

            // R116-io-pivot-calcitem-part: calculatedItems is a real child of CT_PivotCacheDefinition
            // (ECMA-376 18.10.1.3), not CT_pivotTableDefinition -- read it from this cache's own root
            // element so every pivot table built on this cache sees the same calculated items, matching
            // real Excel and the corrected write side (XlsxPivotTableWriter.Cache.cs).
            cache.CalculatedItems.AddRange(XlsxPivotTableReader.ReadPivotCalculatedItems(root.Element(workbookNs + "calculatedItems"), workbookNs));

            foreach (var field in root
                         .Element(workbookNs + "cacheFields")?
                         .Elements(workbookNs + "cacheField") ?? [])
            {
                var sharedItems = field.Element(workbookNs + "sharedItems");
                var fieldGroup = ReadPivotCacheFieldGroup(field, workbookNs);
                cache.Fields.Add(new PivotCacheFieldModel(
                    field.Attribute("name")?.Value ?? "",
                    XlsxXmlAttributeReader.ReadIntAttribute(field, "numFmtId"),
                    sharedItems is null ? null : XlsxXmlAttributeReader.ReadIntAttribute(sharedItems, "count"),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "containsBlank"),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "containsString") || (sharedItems?.Elements(workbookNs + "s").Any() ?? false),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "containsNumber") || (sharedItems?.Elements(workbookNs + "n").Any() ?? false),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "containsDate") || (sharedItems?.Elements(workbookNs + "d").Any() ?? false),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "containsMixedTypes"),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "containsSemiMixedTypes"),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "containsNonDate"),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "containsInteger"),
                    XlsxXmlAttributeReader.ReadBoolAttribute(sharedItems, "longText"),
                    sharedItems is null ? null : XlsxXmlAttributeReader.ReadDoubleAttribute(sharedItems, "minValue"),
                    sharedItems is null ? null : XlsxXmlAttributeReader.ReadDoubleAttribute(sharedItems, "maxValue"),
                    sharedItems?.Attribute("minDate")?.Value,
                    sharedItems?.Attribute("maxDate")?.Value,
                    sharedItems is null ? null : ReadSharedItemValues(sharedItems, workbookNs),
                    sharedItems is null ? null : ReadSharedItemKinds(sharedItems, workbookNs),
                    field.Attribute("formula")?.Value,
                    XlsxXmlAttributeReader.ReadBoolAttribute(field, "databaseField", defaultValue: true),
                    fieldGroup.Grouping,
                    fieldGroup.GroupStart,
                    fieldGroup.GroupEnd,
                    fieldGroup.GroupInterval,
                    fieldGroup.GroupStartDate,
                    fieldGroup.GroupEndDate,
                    fieldGroup.GroupItems));
            }

            result.Add(cache);
        }

        return result;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
    }

    // Reads the raw pivotCacheRecordsN.xml part referenced by a pivotCacheDefinition part, verbatim,
    // so callers with no live worksheet range to re-derive records from (External/Consolidation/
    // Scenario cache sources) can preserve the original cached rows as passthrough on re-save.
    private static string? TryReadRawPivotCacheRecordsXml(ZipArchive archive, string cacheDefinitionPath)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(cacheDefinitionPath);
        var relationships = OpcRelationships.LoadTargets(archive, relsPath, ignoreMalformed: true);
        var recordsTarget = OpcRelationships.FirstTargetByType(relationships, PivotCacheRecordsRelationshipType);
        if (string.IsNullOrWhiteSpace(recordsTarget))
            return null;

        var recordsPath = XlsxPackagePath.ResolveRelationshipTarget(cacheDefinitionPath, recordsTarget);
        var recordsEntry = archive.GetEntry(recordsPath);
        if (recordsEntry is null)
            return null;

        using var stream = recordsEntry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<string> ReadSharedItemValues(XElement sharedItems, XNamespace workbookNs) =>
        sharedItems
            .Elements()
            .Where(element => element.Name == workbookNs + "s" ||
                              element.Name == workbookNs + "n" ||
                              element.Name == workbookNs + "d" ||
                              element.Name == workbookNs + "b" ||
                              element.Name == workbookNs + "m")
            .Select(element => element.Attribute("v")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();

    private static IReadOnlyList<char>? ReadSharedItemKinds(XElement sharedItems, XNamespace workbookNs)
    {
        // Mirrors the filter in ReadSharedItemValues: only items that have a non-blank "v" attribute.
        // <m> (missing) items have no "v" attribute and are excluded from SharedItems, so they are
        // excluded here too to keep indices aligned.
        var kinds = sharedItems
            .Elements()
            .Where(element => element.Name == workbookNs + "s" ||
                              element.Name == workbookNs + "n" ||
                              element.Name == workbookNs + "d" ||
                              element.Name == workbookNs + "b" ||
                              element.Name == workbookNs + "m")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("v")?.Value))
            .Select(element => element.Name.LocalName[0])
            .ToList();

        // Only return the list when there are items so we can avoid null-check overhead for
        // fields with no shared items (e.g. calculated fields, external sources).
        return kinds.Count > 0 ? kinds : null;
    }

    private static PivotFieldModel ReadPivotCacheFieldGroup(XElement cacheField, XNamespace workbookNs)
    {
        var fieldGroup = cacheField.Element(workbookNs + "fieldGroup");
        var rangePr = fieldGroup?.Element(workbookNs + "rangePr");
        if (rangePr is null)
            return new PivotFieldModel(-1);

        var grouping = XlsxPivotTableReader.ReadPivotFieldGrouping(rangePr.Attribute("groupBy")?.Value);
        if (grouping == PivotFieldGrouping.None && rangePr.Attribute("groupInterval") is not null)
            grouping = PivotFieldGrouping.NumberRange;
        if (grouping == PivotFieldGrouping.None)
            return new PivotFieldModel(-1);

        return new PivotFieldModel(
            -1,
            Grouping: grouping,
            GroupStart: XlsxXmlAttributeReader.ReadDoubleAttribute(rangePr, "startNum"),
            GroupEnd: XlsxXmlAttributeReader.ReadDoubleAttribute(rangePr, "endNum"),
            GroupInterval: XlsxXmlAttributeReader.ReadDoubleAttribute(rangePr, "groupInterval"),
            // A date-type groupBy (years/quarters/months/days) serializes its bounds as dateTime
            // startDate/endDate attributes instead of the numeric startNum/endNum (CT_RangePr,
            // ECMA-376 18.10.1.60); real Excel omits startNum/endNum entirely in that case, so this was
            // previously silently dropped on load (R36-io-pivot-cache-2-2).
            GroupStartDate: rangePr.Attribute("startDate")?.Value,
            GroupEndDate: rangePr.Attribute("endDate")?.Value,
            // The group's own label list (CT_GroupItems, ECMA-376 18.10.1.36) that the pivotTable
            // definition's pivotField/items index into; previously never read (R78-io-pivotcache-5-2).
            GroupItems: ReadGroupItemValues(fieldGroup?.Element(workbookNs + "groupItems"), workbookNs));
    }

    // Reads a CT_GroupItems element's child item values (typically all <s v="..."/> label text, but the
    // schema allows the same choice group as sharedItems). Returns null when absent/empty so a field with
    // no group carries no allocation, mirroring ReadSharedItemValues/ReadSharedItemKinds above.
    private static IReadOnlyList<string>? ReadGroupItemValues(XElement? groupItems, XNamespace workbookNs)
    {
        if (groupItems is null)
            return null;

        var values = groupItems
            .Elements()
            .Where(element => element.Name == workbookNs + "s" ||
                              element.Name == workbookNs + "n" ||
                              element.Name == workbookNs + "d" ||
                              element.Name == workbookNs + "b")
            .Select(element => element.Attribute("v")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();

        return values.Count > 0 ? values : null;
    }

    private static PivotCacheSourceType GetSourceType(XElement? cacheSource, XElement? worksheetSource)
    {
        var sourceType = cacheSource?.Attribute("type")?.Value;
        if (string.Equals(sourceType, "external", StringComparison.OrdinalIgnoreCase))
            return PivotCacheSourceType.External;
        if (string.Equals(sourceType, "consolidation", StringComparison.OrdinalIgnoreCase))
            return PivotCacheSourceType.Consolidation;
        if (string.Equals(sourceType, "scenario", StringComparison.OrdinalIgnoreCase))
            return PivotCacheSourceType.Scenario;
        if (worksheetSource is null)
            return PivotCacheSourceType.Unknown;
        if (!string.IsNullOrWhiteSpace(worksheetSource.Attribute("name")?.Value))
            return PivotCacheSourceType.Table;
        if (!string.IsNullOrWhiteSpace(worksheetSource.Attribute("ref")?.Value))
            return PivotCacheSourceType.WorksheetRange;
        return PivotCacheSourceType.Unknown;
    }

    // Returns the FreeX cacheProps element from the pivotCacheDefinition extLst, or null when absent.
    private static XElement? ReadFreeXCacheProps(XElement root, XNamespace workbookNs) =>
        XlsxPivotExtensionReader.ReadElement(root, workbookNs, "cacheProps");
}

