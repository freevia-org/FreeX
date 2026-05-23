using Freexcel.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxPivotCacheReader
{
    public static List<PivotCacheModel> Load(
        ZipArchive archive,
        XDocument workbookXml,
        IReadOnlyDictionary<string, string> workbookRels,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        var result = new List<PivotCacheModel>();
        foreach (var pivotCacheElement in workbookXml.Root?
                     .Element(workbookNs + "pivotCaches")?
                     .Elements(workbookNs + "pivotCache") ?? [])
        {
            var cacheId = XlsxXmlAttributeReader.ReadIntAttribute(pivotCacheElement, "cacheId") ?? 0;
            var relId = pivotCacheElement.Attribute(relNs + "id")?.Value;
            if (cacheId <= 0 || string.IsNullOrWhiteSpace(relId) || !workbookRels.TryGetValue(relId, out var cachePath))
                continue;

            var cacheEntry = archive.GetEntry(cachePath);
            if (cacheEntry is null)
                continue;

            var cacheXml = LoadXml(cacheEntry);
            var root = cacheXml.Root;
            if (root is null)
                continue;

            var cacheSource = root.Element(workbookNs + "cacheSource");
            var worksheetSource = cacheSource?.Element(workbookNs + "worksheetSource");
            var cache = new PivotCacheModel
            {
                CacheId = cacheId,
                SourceType = GetSourceType(cacheSource, worksheetSource),
                SourceSheetName = worksheetSource?.Attribute("sheet")?.Value,
                SourceReference = worksheetSource?.Attribute("ref")?.Value,
                SourceTableName = worksheetSource?.Attribute("name")?.Value,
                ConnectionId = cacheSource is null ? null : XlsxXmlAttributeReader.ReadIntAttribute(cacheSource, "connectionId"),
                IsOlap = XlsxXmlAttributeReader.ReadBoolAttribute(root, "olap"),
                PackagePart = cachePath,
                RefreshOnLoad = XlsxXmlAttributeReader.ReadBoolAttribute(root, "refreshOnLoad", defaultValue: true),
                SaveData = XlsxXmlAttributeReader.ReadBoolAttribute(root, "saveData", defaultValue: true),
                EnableRefresh = XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableRefresh", defaultValue: true),
                RefreshedVersion = XlsxXmlAttributeReader.ReadIntAttribute(root, "refreshedVersion"),
                RefreshedBy = root.Attribute("refreshedBy")?.Value
            };

            foreach (var field in root
                         .Element(workbookNs + "cacheFields")?
                         .Elements(workbookNs + "cacheField") ?? [])
            {
                var sharedItems = field.Element(workbookNs + "sharedItems");
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
                    sharedItems is null ? null : ReadSharedItemValues(sharedItems, workbookNs)));
            }

            result.Add(cache);
        }

        return result;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
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

}

