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
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems,
        XNamespace workbookNs,
        XNamespace relNs,
        string recordsRelId,
        int recordCount,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
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
        // R91-io-external-data-model-5-1: per CT_CacheSource's ST_SourceType (ECMA-376 18.10.1.11),
        // Consolidation and Scenario are their own distinct @type values, not "worksheet" -- silently
        // collapsing them to "worksheet" while emitting an attribute-less <worksheetSource/> child (no
        // @name/@sheet/@ref, since none of those apply to those source kinds) produced a schema-invalid
        // element that also lost the original source classification entirely. Only a true worksheet/
        // table cache gets a <worksheetSource> child; external/consolidation/scenario caches carry no
        // child element FreeX can author (their real child content -- <consolidation>, connection
        // properties -- isn't modeled), so @type + @connectionId is all that is safely preserved here.
        var cacheSourceType = cache.SourceType switch
        {
            PivotCacheSourceType.External => "external",
            PivotCacheSourceType.Consolidation => "consolidation",
            PivotCacheSourceType.Scenario => "scenario",
            _ => "worksheet"
        };
        var cacheSource = new XElement(
            workbookNs + "cacheSource",
            new XAttribute("type", cacheSourceType),
            cache.ConnectionId is { } connectionId ? new XAttribute("connectionId", connectionId.ToString(CultureInfo.InvariantCulture)) : null);
        if (cacheSourceType == "worksheet")
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
                cacheFields.Select(field => ToPivotCacheFieldXml(field, workbookNs, numberFormatIdMap))),
            // R116-io-pivot-calcitem-part: per CT_PivotCacheDefinition's real child sequence (ECMA-376
            // 18.10.1.3: cacheSource, cacheFields, cacheHierarchies, kpis, tupleCache, calculatedItems,
            // calculatedMembers, dimensions, measureGroups, maps, extLst), calculatedItems comes after
            // cacheFields (FreeX writes none of the optional cacheHierarchies/kpis/tupleCache in between)
            // and before extLst. It was previously emitted as a (schema-invalid) child of
            // pivotTableDefinition instead -- see the R116 comment on XlsxPivotTableWriter.
            // ToPivotTableDefinitionXml's call site.
            ToPivotCacheCalculatedItemsXml(calculatedItems, workbookNs),
            FreeXPivotCacheExtension(cache, workbookNs)));
    }

    // FreeX-private ext URI for a calculated item's display Name, which CT_CalculatedItem (ECMA-376
    // 18.10.1.10) has no attribute for (only field/formula attributes plus a required pivotArea child --
    // confirmed via reflection: DocumentFormat.OpenXml.Spreadsheet.CalculatedItem exposes only
    // Field/Formula/PivotArea/ExtensionList). Real Excel derives an item's display text from a new shared-
    // item entry it also adds to the target field's cacheField/sharedItems list -- a much larger, currently
    // unmodeled mechanism. Until that is implemented, preserve the FreeX-authored Name in the item's own
    // (schema-legal) extLst so a FreeX-to-FreeX round trip does not silently lose it; real Excel simply
    // ignores the unrecognized extension.
    private const string FreeXPivotCalculatedItemExtensionUri = "{FREEX-PIVOT-CALCITEM-EXT}";

    private static XElement? ToPivotCacheCalculatedItemsXml(IReadOnlyList<PivotCalculatedItemModel> items, XNamespace workbookNs)
    {
        if (items.Count == 0)
            return null;

        XNamespace freeXNs = FreeXPivotExtensionNamespace;
        return new XElement(
            workbookNs + "calculatedItems",
            new XAttribute("count", items.Count.ToString(CultureInfo.InvariantCulture)),
            items.Select(item => new XElement(
                workbookNs + "calculatedItem",
                new XAttribute("field", item.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("formula", item.Formula),
                // CT_CalculatedItem declares pivotArea as a required child (minOccurs="1") that
                // identifies which field the calculated item targets; without it the part is
                // structurally invalid and real Excel repairs/drops the calculated item on open.
                new XElement(
                    workbookNs + "pivotArea",
                    new XAttribute("type", "normal"),
                    new XAttribute("dataOnly", "0"),
                    new XAttribute("labelOnly", "1"),
                    new XAttribute("outline", "0"),
                    new XAttribute("fieldPosition", "0"),
                    new XElement(
                        workbookNs + "references",
                        new XAttribute("count", "1"),
                        new XElement(
                            workbookNs + "reference",
                            new XAttribute("field", item.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("count", "0"),
                            new XAttribute("selected", "0")))),
                new XElement(
                    workbookNs + "extLst",
                    new XElement(
                        workbookNs + "ext",
                        new XAttribute("uri", FreeXPivotCalculatedItemExtensionUri),
                        new XAttribute(XNamespace.Xmlns + "fx", freeXNs),
                        new XElement(freeXNs + "calculatedItemProps", new XAttribute("name", item.Name)))))));
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

    private static XElement ToPivotCacheFieldXml(
        PivotCacheFieldModel field,
        XNamespace workbookNs,
        IReadOnlyDictionary<int, int> numberFormatIdMap) =>
        new(
            workbookNs + "cacheField",
            new XAttribute("name", string.IsNullOrWhiteSpace(field.Name) ? "Field" : field.Name),
            ToPivotCacheNumberFormatAttribute(field, numberFormatIdMap),
            field.IsDatabaseField ? null : new XAttribute("databaseField", "0"),
            string.IsNullOrWhiteSpace(field.Formula) ? null : new XAttribute("formula", field.Formula),
            ToPivotCacheSharedItemsXml(field, workbookNs),
            ToPivotCacheFieldGroupXml(field, workbookNs));

    // Mirrors ToPivotNumberFormatAttribute (XlsxPivotTableWriter.cs, used by sibling pivotTable
    // dataFields): when the workbook's style/numFmt catalog write remaps a custom numFmtId to avoid an
    // id collision, the SAME remap must be applied here so a cacheField's numFmtId keeps pointing at its
    // original custom format instead of silently referencing whatever unrelated format now occupies its
    // old id in the rebuilt styles.xml.
    private static XAttribute? ToPivotCacheNumberFormatAttribute(
        PivotCacheFieldModel field,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
    {
        if (field.NumberFormatId is not { } numberFormatId)
            return null;

        var mappedId = numberFormatIdMap.TryGetValue(numberFormatId, out var remapped)
            ? remapped
            : numberFormatId;
        return new XAttribute("numFmtId", mappedId.ToString(CultureInfo.InvariantCulture));
    }

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

        // R36-io-pivot-cache-2-2: a date-type groupBy (years/quarters/months/days) serializes its bounds
        // as dateTime startDate/endDate attributes, never startNum/endNum (CT_RangePr, ECMA-376
        // 18.10.1.60). Prefer the preserved date-string bounds when present; fall back to the numeric
        // startNum/endNum otherwise so number-range grouping (and any date field that only carries the
        // legacy numeric bounds) keeps round-tripping exactly as before.
        var isDateGrouping = field.Grouping is PivotFieldGrouping.Year or PivotFieldGrouping.Quarter
            or PivotFieldGrouping.Month or PivotFieldGrouping.Day;

        return new XElement(
            workbookNs + "fieldGroup",
            new XElement(
                workbookNs + "rangePr",
                new XAttribute("groupBy", groupBy),
                isDateGrouping && !string.IsNullOrWhiteSpace(field.GroupStartDate)
                    ? new XAttribute("startDate", field.GroupStartDate)
                    : field.GroupStart is { } groupStart ? new XAttribute("startNum", groupStart.ToString(CultureInfo.InvariantCulture)) : null,
                isDateGrouping && !string.IsNullOrWhiteSpace(field.GroupEndDate)
                    ? new XAttribute("endDate", field.GroupEndDate)
                    : field.GroupEnd is { } groupEnd ? new XAttribute("endNum", groupEnd.ToString(CultureInfo.InvariantCulture)) : null,
                field.GroupInterval is { } groupInterval ? new XAttribute("groupInterval", groupInterval.ToString(CultureInfo.InvariantCulture)) : null),
            // R78-io-pivotcache-5-2: the group's own label list (CT_GroupItems, ECMA-376 18.10.1.36) that
            // the pivotTable definition's pivotField/items index into to render the grouped field's
            // headers. Previously never emitted, leaving those indexes pointing at nothing on reopen.
            ToPivotCacheGroupItemsXml(field.GroupItems, workbookNs));
    }

    private static XElement? ToPivotCacheGroupItemsXml(IReadOnlyList<string>? groupItems, XNamespace workbookNs)
    {
        if (groupItems is null || groupItems.Count == 0)
            return null;

        return new XElement(
            workbookNs + "groupItems",
            new XAttribute("count", groupItems.Count.ToString(CultureInfo.InvariantCulture)),
            groupItems.Select(item => new XElement(workbookNs + "s", new XAttribute("v", item))));
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
        var hasSourceRange = TryGetPivotCacheSourceRange(cache, workbook, out var sourceSheet, out var sourceRange);
        if (!hasSourceRange && TryGetPreservedPivotCacheRecordsXml(cache, workbookNs, out var preserved))
        {
            // R91-io-external-data-model-5-1: External/Consolidation/Scenario cache sources have no
            // live worksheet range to re-derive records from -- previously this fell straight through
            // to the empty-records path below, silently destroying an offline-cached query/
            // consolidation result on every save that goes through this modeled writer (native .fxl
            // round-trip, legacy .xls export). Use the verbatim original records captured at load time
            // instead of authoring an empty <pivotCacheRecords count="0"/>.
            return preserved;
        }

        var records = new List<XElement>();
        if (hasSourceRange &&
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
            var minDate = field.MinDate;
            var maxDate = field.MaxDate;

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
                    case DateTimeValue date:
                        containsDate = true;
                        var iso = date.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                        if (minDate is null || string.CompareOrdinal(iso, minDate) < 0)
                            minDate = iso;
                        if (maxDate is null || string.CompareOrdinal(iso, maxDate) > 0)
                            maxDate = iso;
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
                maxValue == field.MaxValue &&
                minDate == field.MinDate &&
                maxDate == field.MaxDate)
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
                MinDate = minDate,
                MaxDate = maxDate,
            };
        }
    }

    // Passthrough for cache sources TryGetPivotCacheSourceRange can never resolve (External/
    // Consolidation/Scenario): reuses the verbatim <pivotCacheRecords> XML captured at load time
    // (XlsxPivotCacheReader) instead of authoring an empty records part. See
    // R91-io-external-data-model-5-1.
    private static bool TryGetPreservedPivotCacheRecordsXml(
        PivotCacheModel cache,
        XNamespace workbookNs,
        out (XDocument Document, int RecordCount) preserved)
    {
        preserved = default;
        if (string.IsNullOrWhiteSpace(cache.RawRecordsXml))
            return false;

        try
        {
            var document = XDocument.Parse(cache.RawRecordsXml);
            if (document.Root is null)
                return false;

            var recordCount = document.Root.Elements(workbookNs + "r").Count();
            preserved = (document, recordCount);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            // Malformed preserved XML (should not happen for content we captured ourselves) --
            // fall back to the normal empty/regenerated-records path rather than propagating.
            return false;
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
        if (cache.SourceType is PivotCacheSourceType.External or PivotCacheSourceType.Consolidation or PivotCacheSourceType.Scenario)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(cache.SourceSheetName) && !string.IsNullOrWhiteSpace(cache.SourceReference))
        {
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

        // R78-io-pivotcache-5-1: per CT_WorksheetSource (ECMA-376 18.10.2.42), a Table-sourced
        // worksheetSource omits @sheet/@ref when @name is present, so a cache reloaded from a real xlsx
        // (or round-tripped through the FreeX-native format, which copies these fields through unchanged)
        // carries only SourceTableName here. Resolve the source range via the workbook's own ListObject
        // registry instead of giving up, so the records loop below still finds the live data.
        if (!string.IsNullOrWhiteSpace(cache.SourceTableName))
        {
            foreach (var sheet in workbook.Sheets)
            {
                var table = sheet.StructuredTables.FirstOrDefault(t =>
                    string.Equals(t.Name, cache.SourceTableName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.DisplayName, cache.SourceTableName, StringComparison.OrdinalIgnoreCase));
                if (table is null)
                    continue;

                sourceSheet = sheet;
                sourceRange = table.Range;
                return true;
            }
        }

        return false;
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
