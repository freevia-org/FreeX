using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableWriter
{
    private const string PivotCacheRecordsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";

    private static XDocument ToPivotCacheDefinitionXml(
        PivotCacheModel cache,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        XNamespace workbookNs,
        XNamespace relNs,
        string recordsRelId,
        int recordCount)
    {
        var cacheFields = GetEffectivePivotCacheFields(cache, calculatedFields);
        var source = new XElement(workbookNs + "worksheetSource");
        if (cache.SourceType == PivotCacheSourceType.Table && !string.IsNullOrWhiteSpace(cache.SourceTableName))
        {
            source.SetAttributeValue("name", cache.SourceTableName);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(cache.SourceTableName))
                source.SetAttributeValue("name", cache.SourceTableName);
            if (!string.IsNullOrWhiteSpace(cache.SourceSheetName))
                source.SetAttributeValue("sheet", cache.SourceSheetName);
            if (!string.IsNullOrWhiteSpace(cache.SourceReference))
                source.SetAttributeValue("ref", cache.SourceReference);
        }
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
                new XAttribute("count", cacheFields.Count.ToString(CultureInfo.InvariantCulture)),
                cacheFields.Select(field => ToPivotCacheFieldXml(field, workbookNs))),
            FreeXPivotCacheExtension(cache, workbookNs)));
    }

    private static List<PivotCacheFieldModel> GetEffectivePivotCacheFields(
        PivotCacheModel cache,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields)
    {
        var result = cache.Fields.ToList();
        var names = result
            .Select(field => field.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var calculatedField in calculatedFields)
        {
            if (string.IsNullOrWhiteSpace(calculatedField.Name) ||
                string.IsNullOrWhiteSpace(calculatedField.Formula) ||
                !names.Add(calculatedField.Name))
            {
                continue;
            }

            result.Add(new PivotCacheFieldModel(
                calculatedField.Name,
                Formula: calculatedField.Formula,
                IsDatabaseField: false));
        }

        return result;
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

    private static XElement ToPivotCacheFieldXml(PivotCacheFieldModel field, XNamespace workbookNs) =>
        new(
            workbookNs + "cacheField",
            new XAttribute("name", string.IsNullOrWhiteSpace(field.Name) ? "Field" : field.Name),
            field.NumberFormatId is { } numFmtId ? new XAttribute("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture)) : null,
            field.IsDatabaseField ? null : new XAttribute("databaseField", "0"),
            string.IsNullOrWhiteSpace(field.Formula) ? null : new XAttribute("formula", field.Formula),
            ToPivotCacheSharedItemsXml(field, workbookNs),
            ToPivotCacheFieldGroupXml(field, workbookNs));

    // R30-io-pivot-cache-deep-3: date/number-range grouping (field.Grouping/GroupStart/GroupEnd/
    // GroupInterval) was previously only preserved in a FreeX-private extLst extension, which real Excel
    // and any other OOXML consumer ignores. Emit the native CT_FieldGroup/CT_RangePr element the reader
    // (XlsxPivotCacheReader.ReadPivotCacheFieldGroup) already parses back in, so a fresh workbook's first
    // save keeps the grouping visible in real Excel.
    private static XElement? ToPivotCacheFieldGroupXml(PivotCacheFieldModel field, XNamespace workbookNs)
    {
        var groupBy = field.Grouping switch
        {
            PivotFieldGrouping.Year => "years",
            PivotFieldGrouping.Quarter => "quarters",
            PivotFieldGrouping.Month => "months",
            PivotFieldGrouping.Day => "days",
            PivotFieldGrouping.NumberRange => "range",
            _ => null
        };
        if (groupBy is null)
            return null;

        return new XElement(
            workbookNs + "fieldGroup",
            new XElement(
                workbookNs + "rangePr",
                new XAttribute("groupBy", groupBy),
                field.GroupStart is { } groupStart ? new XAttribute("startNum", groupStart.ToString(CultureInfo.InvariantCulture)) : null,
                field.GroupEnd is { } groupEnd ? new XAttribute("endNum", groupEnd.ToString(CultureInfo.InvariantCulture)) : null,
                field.GroupInterval is { } groupInterval ? new XAttribute("groupInterval", groupInterval.ToString(CultureInfo.InvariantCulture)) : null));
    }

    private static XElement ToPivotCacheSharedItemsXml(PivotCacheFieldModel field, XNamespace workbookNs)
    {
        var items = field.SharedItems ?? [];
        var kinds = field.SharedItemKinds;
        // The emitted "count" attribute must equal the number of child items actually written below.
        // field.SharedItemCount can be stale relative to field.SharedItems: the reader (XlsxPivotCacheReader)
        // filters out <m/> (missing-value) items when populating SharedItems but leaves the raw preserved
        // sharedItems/@count untouched, so re-emitting that raw count here would produce a
        // schema-inconsistent <sharedItems count="N"> with fewer than N children -- Excel then flags the
        // pivot cache part as unreadable. Recomputing from the actual item list keeps count and children
        // in lockstep. Only emit the attribute at all under the SAME condition as before this fix (a
        // preserved, non-null SharedItemCount) so fields that never carried one -- including FreeX-created
        // fields whose type/range metadata was widened without ever gaining an explicit item list --
        // keep round-tripping SharedItemCount as null rather than picking up a synthetic count derived
        // solely from item-list length.
        var count = items.Count;
        return new XElement(
            workbookNs + "sharedItems",
            field.SharedItemCount is not null ? new XAttribute("count", count.ToString(CultureInfo.InvariantCulture)) : null,
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
            items.Select((item, index) =>
                ToPivotCacheSharedItemXml(item, kinds is not null && index < kinds.Count ? kinds[index] : '\0', workbookNs)));
    }

    private static XElement ToPivotCacheSharedItemXml(string item, char kind, XNamespace workbookNs)
    {
        // Use the preserved element kind from the original file when available.
        // Fall back to inference only for fresh items (kind == '\0').
        return kind switch
        {
            's' => new XElement(workbookNs + "s", new XAttribute("v", item)),
            'n' => new XElement(workbookNs + "n", new XAttribute("v", item)),
            'd' => new XElement(workbookNs + "d", new XAttribute("v", item)),
            'b' => new XElement(workbookNs + "b", new XAttribute("v", item)),
            _ => InferPivotCacheSharedItemXml(item, workbookNs)
        };
    }

    // Fallback inference for items created fresh in FreeX (no original element kind available).
    private static XElement InferPivotCacheSharedItemXml(string item, XNamespace workbookNs)
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

    // Cache-field metadata (ContainsNumber/ContainsDate/ContainsString/MinValue/MaxValue) is loaded once
    // from the source xlsx at file-open time (XlsxPivotCacheReader.Load) and is otherwise never updated.
    // ToPivotCacheRecordsXml below always re-reads the LIVE worksheet and writes fresh <r> records that
    // reflect any edits the user made, so without this resync a saved file's cacheField/sharedItems could
    // permanently disagree with its own pivotCacheRecords (e.g. a numeric-only field that gained a text
    // value keeps declaring containsNumber-only with stale min/max). Widen (never narrow) the observed
    // type flags/min-max from the current source data immediately before the definition is serialized.
    private static void ResyncPivotCacheFieldTypeMetadata(PivotCacheModel cache, Workbook workbook)
    {
        if (!TryGetPivotCacheSourceRange(cache, workbook, out var sourceSheet, out var sourceRange) ||
            sourceRange.RowCount <= 1)
        {
            return;
        }

        var fieldCount = Math.Min(cache.Fields.Count, (int)sourceRange.ColCount);
        for (var index = 0; index < fieldCount; index++)
        {
            var field = cache.Fields[index];
            var col = sourceRange.Start.Col + (uint)index;
            var containsString = field.ContainsString;
            var containsNumber = field.ContainsNumber;
            var containsDate = field.ContainsDate;
            var minValue = field.MinValue;
            var maxValue = field.MaxValue;

            // Only ever WIDEN from actually-observed, typed values (Number/Date/String/Bool/Error).
            // Blank cells are deliberately NOT treated as evidence of anything here: many pivot caches
            // loaded from a real file describe source ranges wider/taller than what the in-memory sheet
            // happens to hold live (e.g. before a refresh, or in tests that patch a rich pivot cache
            // definition onto a near-empty placeholder sheet), and a blank read there must never be
            // allowed to override or contradict metadata the cache definition already carries.
            for (var row = sourceRange.Start.Row + 1; row <= sourceRange.End.Row; row++)
            {
                switch (sourceSheet.GetValue(row, col))
                {
                    case NumberValue number:
                        containsNumber = true;
                        if (minValue is null || number.Value < minValue.Value)
                            minValue = number.Value;
                        if (maxValue is null || number.Value > maxValue.Value)
                            maxValue = number.Value;
                        break;
                    case DateTimeValue:
                        containsDate = true;
                        break;
                    case TextValue text when !string.IsNullOrEmpty(text.Value):
                        containsString = true;
                        break;
                    case BoolValue:
                    case ErrorValue:
                        containsString = true;
                        break;
                }
            }

            var typeCount = (containsString ? 1 : 0) + (containsNumber ? 1 : 0) + (containsDate ? 1 : 0);
            var containsMixedTypes = field.ContainsMixedTypes || typeCount > 1;
            if (containsString == field.ContainsString &&
                containsNumber == field.ContainsNumber &&
                containsDate == field.ContainsDate &&
                containsMixedTypes == field.ContainsMixedTypes &&
                minValue == field.MinValue &&
                maxValue == field.MaxValue)
            {
                continue;
            }

            cache.Fields[index] = field with
            {
                ContainsString = containsString,
                ContainsNumber = containsNumber,
                ContainsDate = containsDate,
                ContainsMixedTypes = containsMixedTypes,
                MinValue = minValue,
                MaxValue = maxValue,
            };
        }
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
            NumberValue number => new XElement(workbookNs + "n", new XAttribute("v", XlsxNumberFormatting.ToXmlString(number.Value))),
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
