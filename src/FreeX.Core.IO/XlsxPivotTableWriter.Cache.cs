using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableWriter
{
    private const string PivotCacheRecordsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";

    private static XDocument ToPivotCacheDefinitionXml(
        PivotCacheModel cache,
        XNamespace workbookNs,
        XNamespace relNs,
        string recordsRelId,
        int recordCount)
    {
        var source = new XElement(workbookNs + "worksheetSource");
        if (!string.IsNullOrWhiteSpace(cache.SourceTableName))
            source.SetAttributeValue("name", cache.SourceTableName);
        if (!string.IsNullOrWhiteSpace(cache.SourceSheetName))
            source.SetAttributeValue("sheet", cache.SourceSheetName);
        if (!string.IsNullOrWhiteSpace(cache.SourceReference))
            source.SetAttributeValue("ref", cache.SourceReference);
        var cacheSource = new XElement(
            workbookNs + "cacheSource",
            new XAttribute("type", cache.SourceType == PivotCacheSourceType.External ? "external" : "worksheet"),
            cache.ConnectionId is { } connectionId ? new XAttribute("connectionId", connectionId.ToString(CultureInfo.InvariantCulture)) : null);
        if (cache.SourceType != PivotCacheSourceType.External)
            cacheSource.Add(source);

        return new XDocument(new XElement(
            workbookNs + "pivotCacheDefinition",
            new XAttribute(XNamespace.Xmlns + "r", relNs),
            new XAttribute(relNs + "id", recordsRelId),
            cache.IsOlap ? new XAttribute("olap", "1") : null,
            new XAttribute("refreshOnLoad", cache.RefreshOnLoad ? "1" : "0"),
            new XAttribute("saveData", cache.SaveData ? "1" : "0"),
            new XAttribute("enableRefresh", cache.EnableRefresh ? "1" : "0"),
            // 'preserveSourceSortFilter' and 'refreshedDateIso' are NOT CT_PivotCacheDefinition attributes
            // in the OOXML schema (refreshedDate is an xsd:double serial date, not an ISO string), so they
            // are persisted in a FreeX extLst extension instead — see FreeXPivotCacheExtension below. This
            // keeps the cache part schema-valid (Excel accepts it) while still round-tripping the flags.
            cache.MissingItemsLimit is { } missingItemsLimit ? new XAttribute("missingItemsLimit", missingItemsLimit.ToString(CultureInfo.InvariantCulture)) : null,
            cache.CreatedVersion is { } createdVersion ? new XAttribute("createdVersion", createdVersion.ToString(CultureInfo.InvariantCulture)) : null,
            cache.MinRefreshableVersion is { } minRefreshableVersion ? new XAttribute("minRefreshableVersion", minRefreshableVersion.ToString(CultureInfo.InvariantCulture)) : null,
            cache.RefreshedVersion is { } refreshedVersion ? new XAttribute("refreshedVersion", refreshedVersion.ToString(CultureInfo.InvariantCulture)) : null,
            !string.IsNullOrWhiteSpace(cache.RefreshedBy) ? new XAttribute("refreshedBy", cache.RefreshedBy) : null,
            new XAttribute("recordCount", recordCount.ToString(CultureInfo.InvariantCulture)),
            cacheSource,
            new XElement(
                workbookNs + "cacheFields",
                new XAttribute("count", cache.Fields.Count.ToString(CultureInfo.InvariantCulture)),
                cache.Fields.Select(field => new XElement(
                    workbookNs + "cacheField",
                    new XAttribute("name", string.IsNullOrWhiteSpace(field.Name) ? "Field" : field.Name),
                    field.NumberFormatId is { } numFmtId ? new XAttribute("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture)) : null,
                    ToPivotCacheSharedItemsXml(field, workbookNs)))),
            FreeXPivotCacheExtension(cache, workbookNs)));
    }

    // FreeX-private namespace + extension URI for pivot flags that have no home in the base OOXML schema.
    private const string FreeXPivotExtensionNamespace = "urn:freex:pivot:2026";
    private const string FreeXPivotCacheExtensionUri = "{FREEX-PIVOT-CACHE-EXT}";
    private const string FreeXPivotTableExtensionUri = "{FREEX-PIVOT-TABLE-EXT}";

    // Persists pivot-cache flags with no base-schema attribute (preserveSourceSortFilter, the ISO refreshed
    // date) inside a schema-valid extLst/ext block. Returns null when nothing needs preserving.
    private static XElement? FreeXPivotCacheExtension(PivotCacheModel cache, XNamespace workbookNs)
    {
        XNamespace freeXNs = FreeXPivotExtensionNamespace;
        var payload = new XElement(freeXNs + "cacheProps");
        if (!cache.PreserveSourceSortFilter)
            payload.SetAttributeValue("preserveSourceSortFilter", "0");
        if (!string.IsNullOrWhiteSpace(cache.RefreshedDateIso))
            payload.SetAttributeValue("refreshedDateIso", cache.RefreshedDateIso);

        if (!payload.HasAttributes)
            return null;

        return new XElement(
            workbookNs + "extLst",
            new XElement(
                workbookNs + "ext",
                new XAttribute("uri", FreeXPivotCacheExtensionUri),
                new XAttribute(XNamespace.Xmlns + "fx", FreeXPivotExtensionNamespace),
                payload));
    }

    private static XElement ToPivotCacheSharedItemsXml(PivotCacheFieldModel field, XNamespace workbookNs) =>
        new(
            workbookNs + "sharedItems",
            field.SharedItemCount is { } count ? new XAttribute("count", count.ToString(CultureInfo.InvariantCulture)) : null,
            field.ContainsBlank ? new XAttribute("containsBlank", "1") : null,
            field.ContainsString ? new XAttribute("containsString", "1") : null,
            field.ContainsNumber ? new XAttribute("containsNumber", "1") : null,
            field.ContainsDate ? new XAttribute("containsDate", "1") : null,
            field.ContainsMixedTypes ? new XAttribute("containsMixedTypes", "1") : null,
            field.ContainsSemiMixedTypes ? new XAttribute("containsSemiMixedTypes", "1") : null,
            field.ContainsNonDate ? new XAttribute("containsNonDate", "1") : null,
            field.ContainsInteger ? new XAttribute("containsInteger", "1") : null,
            field.ContainsLongText ? new XAttribute("longText", "1") : null,
            field.MinValue is { } minValue ? new XAttribute("minValue", minValue.ToString(CultureInfo.InvariantCulture)) : null,
            field.MaxValue is { } maxValue ? new XAttribute("maxValue", maxValue.ToString(CultureInfo.InvariantCulture)) : null,
            !string.IsNullOrWhiteSpace(field.MinDate) ? new XAttribute("minDate", field.MinDate) : null,
            !string.IsNullOrWhiteSpace(field.MaxDate) ? new XAttribute("maxDate", field.MaxDate) : null,
            (field.SharedItems ?? []).Select(item => ToPivotCacheSharedItemXml(item, workbookNs)));

    private static XElement ToPivotCacheSharedItemXml(string item, XNamespace workbookNs)
    {
        if (double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return new XElement(workbookNs + "n", new XAttribute("v", item));
        if (bool.TryParse(item, out var boolean))
            return new XElement(workbookNs + "b", new XAttribute("v", boolean ? "1" : "0"));
        if (DateTime.TryParse(item, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return new XElement(workbookNs + "d", new XAttribute("v", item));
        return new XElement(workbookNs + "s", new XAttribute("v", item));
    }

    private static (XDocument Document, int RecordCount) ToPivotCacheRecordsXml(
        PivotCacheModel cache,
        Workbook workbook,
        XNamespace workbookNs)
    {
        var records = new List<XElement>();
        if (TryGetPivotCacheSourceRange(cache, workbook, out var sourceSheet, out var sourceRange) &&
            sourceRange.RowCount > 1 &&
            cache.Fields.Count > 0)
        {
            var fieldCount = Math.Min(cache.Fields.Count, (int)sourceRange.ColCount);
            for (var row = sourceRange.Start.Row + 1; row <= sourceRange.End.Row; row++)
            {
                var values = new List<XElement>(fieldCount);
                for (var index = 0; index < fieldCount; index++)
                {
                    var col = sourceRange.Start.Col + (uint)index;
                    values.Add(ToPivotCacheRecordValueXml(sourceSheet.GetValue(row, col), workbookNs));
                }

                records.Add(new XElement(workbookNs + "r", values));
            }
        }

        return (
            new XDocument(new XElement(
                workbookNs + "pivotCacheRecords",
                new XAttribute("count", records.Count.ToString(CultureInfo.InvariantCulture)),
                records)),
            records.Count);
    }

    private static bool TryGetPivotCacheSourceRange(
        PivotCacheModel cache,
        Workbook workbook,
        out Sheet sourceSheet,
        out GridRange sourceRange)
    {
        sourceSheet = null!;
        sourceRange = default;
        if (cache.SourceType is PivotCacheSourceType.External or PivotCacheSourceType.Consolidation or PivotCacheSourceType.Scenario ||
            string.IsNullOrWhiteSpace(cache.SourceSheetName) ||
            string.IsNullOrWhiteSpace(cache.SourceReference))
        {
            return false;
        }

        var matchedSheet = workbook.GetSheet(cache.SourceSheetName);
        if (matchedSheet is null)
            return false;

        sourceSheet = matchedSheet;
        try
        {
            sourceRange = GridRange.Parse(NormalizePivotCacheSourceReference(cache.SourceReference), sourceSheet.Id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string NormalizePivotCacheSourceReference(string reference)
    {
        var normalized = reference.Trim();
        var sheetSeparator = normalized.LastIndexOf('!');
        if (sheetSeparator >= 0 && sheetSeparator + 1 < normalized.Length)
            normalized = normalized[(sheetSeparator + 1)..];

        return normalized.Replace("$", "", StringComparison.Ordinal);
    }

    private static XElement ToPivotCacheRecordValueXml(ScalarValue value, XNamespace workbookNs) =>
        value switch
        {
            TextValue text => new XElement(workbookNs + "s", new XAttribute("v", text.Value)),
            NumberValue number => new XElement(workbookNs + "n", new XAttribute("v", number.Value.ToString("G17", CultureInfo.InvariantCulture))),
            DateTimeValue date => new XElement(workbookNs + "d", new XAttribute("v", date.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))),
            BoolValue boolean => new XElement(workbookNs + "b", new XAttribute("v", boolean.Value ? "1" : "0")),
            ErrorValue error => new XElement(workbookNs + "e", new XAttribute("v", error.Code)),
            _ => new XElement(workbookNs + "m")
        };

    private static XDocument ToPivotCacheDefinitionRelsXml(
        XNamespace packageRelNs,
        string cachePath,
        string recordsPath,
        string recordsRelId) =>
        new(new XElement(
            packageRelNs + "Relationships",
            new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", recordsRelId),
                new XAttribute("Type", PivotCacheRecordsRelationshipType),
                new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(cachePath, recordsPath)))));
}
