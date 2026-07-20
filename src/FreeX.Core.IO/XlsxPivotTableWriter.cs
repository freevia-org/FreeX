using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableWriter
{
    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRelsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var cachePartById = new Dictionary<int, string>();
        var cacheById = workbook.PivotCaches
            .Where(cache => cache.CacheId > 0)
            .ToDictionary(cache => cache.CacheId);
        var calculatedFieldsByCacheId = GetCalculatedFieldsByCacheId(workbook);
        var calculatedFieldIndexesByCacheId = new Dictionary<int, IReadOnlyDictionary<string, int>>();
        var pivotCacheElements = new List<XElement>();
        var cacheIndex = 1;
        foreach (var cache in workbook.PivotCaches.OrderBy(cache => cache.CacheId))
        {
            if (cache.CacheId <= 0)
                continue;

            var cacheOrdinal = cacheIndex++;
            var cachePath = $"xl/pivotCache/pivotCacheDefinition{cacheOrdinal}.xml";
            var recordsPath = $"xl/pivotCache/pivotCacheRecords{cacheOrdinal}.xml";
            var recordsRelId = $"rIdPivotCacheRecords{cacheOrdinal}";
            var calculatedFields = calculatedFieldsByCacheId.TryGetValue(cache.CacheId, out var cacheCalculatedFields)
                ? cacheCalculatedFields
                : [];
            calculatedFieldIndexesByCacheId[cache.CacheId] = CreateCalculatedFieldIndexMap(cache, calculatedFields);
            var cacheRecords = ToPivotCacheRecordsXml(cache, workbook, workbookNs);
            // Resync cache.Fields' type/range metadata against the live source data (which the records
            // above were just generated from) so the saved cache definition doesn't contradict its own
            // records when the underlying cells were edited since the cache was loaded/created.
            ResyncPivotCacheFieldTypeMetadata(cache, workbook);
            XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, ToPivotCacheDefinitionXml(cache, calculatedFields, workbookNs, relNs, recordsRelId, cacheRecords.RecordCount, numberFormatIdMap));
            XlsxPackageXmlEditor.ReplaceXml(archive, recordsPath, cacheRecords.Document);
            XlsxPackageXmlEditor.ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(cachePath), ToPivotCacheDefinitionRelsXml(packageRelNs, cachePath, recordsPath, recordsRelId));
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{cachePath}", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml");
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{recordsPath}", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml");

            var cacheRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                workbookRelsXml,
                packageRelNs,
                "xl/workbook.xml",
                cachePath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition");
            pivotCacheElements.Add(new XElement(
                workbookNs + "pivotCache",
                new XAttribute("cacheId", cache.CacheId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute(relNs + "id", cacheRelId)));
            cachePartById[cache.CacheId] = cachePath;
        }

        var workbookRoot = workbookXml.Root;
        if (workbookRoot is not null && pivotCacheElements.Count > 0)
        {
            workbookRoot.Elements(workbookNs + "pivotCaches").Remove();
            // CT_PivotCaches has no 'count' attribute in the OOXML schema (unlike most other collection
            // wrappers); emitting one makes the workbook schema-invalid and Excel rejects it.
            var pivotCachesElement = new XElement(
                workbookNs + "pivotCaches",
                pivotCacheElements);
            // Per CT_Workbook, pivotCaches comes after sheets/definedNames/calcPr/customWorkbookViews and
            // before smartTagPr/webPublishing/extLst. Inserting it before <sheets> (as a naive
            // AddBeforeSelf would) is schema-invalid and makes Excel reject the workbook.
            InsertWorkbookPivotCaches(workbookRoot, workbookNs, pivotCachesElement);
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/_rels/workbook.xml.rels", workbookRelsXml);

        var relTargets = workbookRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        var pivotIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.PivotTables.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            WriteWorksheetPivotTables(archive, worksheetPath, sheet, cachePartById, cacheById, calculatedFieldIndexesByCacheId, numberFormatIdMap, ref pivotIndex, workbookNs, relNs, packageRelNs);
        }
    }

    private static Dictionary<int, IReadOnlyList<PivotCalculatedFieldModel>> GetCalculatedFieldsByCacheId(Workbook workbook)
    {
        var result = new Dictionary<int, List<PivotCalculatedFieldModel>>();
        foreach (var pivot in workbook.Sheets.SelectMany(sheet => sheet.PivotTables))
        {
            if (pivot.CacheId <= 0 || pivot.CalculatedFields.Count == 0)
                continue;

            if (!result.TryGetValue(pivot.CacheId, out var fields))
            {
                fields = [];
                result[pivot.CacheId] = fields;
            }

            foreach (var field in pivot.CalculatedFields)
            {
                if (string.IsNullOrWhiteSpace(field.Name) ||
                    string.IsNullOrWhiteSpace(field.Formula) ||
                    fields.Any(existing => string.Equals(existing.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                fields.Add(field);
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<PivotCalculatedFieldModel>)pair.Value);
    }

    // CT_Workbook elements that must come after <pivotCaches>. The element is inserted immediately
    // before the first of these present; otherwise it is appended after the existing leading elements.
    private static readonly HashSet<string> WorkbookElementsAfterPivotCaches = new(StringComparer.Ordinal)
    {
        "smartTagPr", "smartTagTypes", "webPublishing", "fileRecoveryPr", "webPublishObjects", "extLst",
    };

    internal static void InsertWorkbookPivotCaches(XElement workbookRoot, XNamespace workbookNs, XElement pivotCaches)
    {
        XElement? anchor = null;
        foreach (var element in workbookRoot.Elements())
        {
            if (WorkbookElementsAfterPivotCaches.Contains(element.Name.LocalName))
            {
                anchor = element;
                break;
            }
        }

        if (anchor is not null)
        {
            anchor.AddBeforeSelf(pivotCaches);
            return;
        }

        workbookRoot.Add(pivotCaches);
    }

    private static void WriteWorksheetPivotTables(
        ZipArchive archive,
        string worksheetPath,
        Sheet sheet,
        IReadOnlyDictionary<int, string> cachePartById,
        IReadOnlyDictionary<int, PivotCacheModel> cacheById,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> calculatedFieldIndexesByCacheId,
        IReadOnlyDictionary<int, int> numberFormatIdMap,
        ref int pivotIndex,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));

        var wroteAnyPivot = false;
        foreach (var pivot in sheet.PivotTables)
        {
            if (!cachePartById.TryGetValue(pivot.CacheId, out var cachePath))
                continue;

            var calculatedFieldIndexes = calculatedFieldIndexesByCacheId.TryGetValue(pivot.CacheId, out var indexes)
                ? indexes
                : cacheById.TryGetValue(pivot.CacheId, out var cache)
                    ? CreateCalculatedFieldIndexMap(cache, pivot.CalculatedFields)
                    : CreateCalculatedFieldIndexMap(pivot);
            var pivotPath = $"xl/pivotTables/pivotTable{pivotIndex++}.xml";
            var cacheRelId = "rIdPivotCache";
            cacheById.TryGetValue(pivot.CacheId, out var pivotCache);
            XlsxPackageXmlEditor.ReplaceXml(archive, pivotPath, ToPivotTableDefinitionXml(pivot, pivotCache, calculatedFieldIndexes, workbookNs, cacheRelId, numberFormatIdMap));
            XlsxPackageXmlEditor.ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(pivotPath), new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", cacheRelId),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition"),
                        new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(pivotPath, cachePath))))));
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{pivotPath}", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml");

            // A pivot table is linked to its host worksheet purely through a worksheet-rels pivotTable
            // relationship. CT_Worksheet has no element that references a pivotTableDefinition, so adding
            // one to the worksheet XML (as earlier revisions did) produced schema-invalid output that
            // Excel rejects.
            XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                worksheetRelsXml,
                packageRelNs,
                worksheetPath,
                pivotPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable");
            wroteAnyPivot = true;
        }

        if (!wroteAnyPivot)
            return;

        // Drop any stale worksheet-embedded pivot reference from older saves, then persist the updated rels.
        worksheetXml.Root?.Elements(workbookNs + "pivotTableDefinition").Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static XDocument ToPivotTableDefinitionXml(
        PivotTableModel pivot,
        PivotCacheModel? pivotCache,
        IReadOnlyDictionary<string, int> calculatedFieldIndexes,
        XNamespace workbookNs,
        string cacheRelId,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
    {
        return new XDocument(new XElement(
            workbookNs + "pivotTableDefinition",
            new XAttribute("name", string.IsNullOrWhiteSpace(pivot.Name) ? "PivotTable" : pivot.Name),
            new XAttribute("cacheId", pivot.CacheId.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("dataOnRows", pivot.DataOnRows ? "1" : "0"),
            new XAttribute("applyNumberFormats", pivot.ApplyNumberFormats ? "1" : "0"),
            new XAttribute("applyBorderFormats", pivot.ApplyBorderFormats ? "1" : "0"),
            new XAttribute("applyFontFormats", pivot.ApplyFontFormats ? "1" : "0"),
            new XAttribute("applyPatternFormats", pivot.ApplyPatternFormats ? "1" : "0"),
            new XAttribute("applyWidthHeightFormats", pivot.AutofitColumnsOnUpdate ? "1" : "0"),
            new XAttribute("preserveFormatting", pivot.PreserveFormattingOnUpdate ? "1" : "0"),
            ToOptionalIntAttribute("createdVersion", pivot.CreatedVersion),
            new XAttribute("updatedVersion", (pivot.UpdatedVersion ?? 8).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("minRefreshableVersion", (pivot.MinRefreshableVersion ?? 3).ToString(CultureInfo.InvariantCulture)),
            // CT_pivotTableDefinition declares grand-total visibility as 'rowGrandTotals' / 'colGrandTotals'.
            // There is no 'showGrandTotals' / 'showRowGrandTotals' / 'showColumnGrandTotals' attribute; the
            // earlier names were schema-invalid and Excel rejected them.
            new XAttribute("rowGrandTotals", pivot.ShowRowGrandTotals ? "1" : "0"),
            new XAttribute("colGrandTotals", pivot.ShowColumnGrandTotals ? "1" : "0"),
            // repeatItemLabels / blankLineAfterItems are NOT pivotTableDefinition attributes in OOXML; they
            // live on each CT_PivotField (as repeatItemLabels / insertBlankRow). They are emitted per-field
            // in ToPivotFieldsXml below; emitting them on the definition is schema-invalid.
            new XAttribute("showHeaders", pivot.ShowFieldHeaders ? "1" : "0"),
            new XAttribute("showDataTips", pivot.ShowContextualTooltips ? "1" : "0"),
            new XAttribute("showMemberPropertyTips", pivot.ShowPropertiesInTooltips ? "1" : "0"),
            // showDropZones (CT_pivotTableDefinition, default true) is an unrelated flag from
            // gridDropZones/Classic PivotTable Layout below; FreeX does not model it separately, so it is
            // left at its schema default (omitted) rather than being conflated with ShowClassicLayout.
            new XAttribute("fieldListSortAscending", pivot.FieldListSortAscending ? "1" : "0"),
            new XAttribute("mergeItem", pivot.MergeAndCenterLabels ? "1" : "0"),
            new XAttribute("showEmptyRow", pivot.ShowItemsWithNoDataOnRows ? "1" : "0"),
            new XAttribute("showEmptyCol", pivot.ShowItemsWithNoDataOnColumns ? "1" : "0"),
            new XAttribute("pageOverThenDown", pivot.PageOverThenDown ? "1" : "0"),
            new XAttribute("pageWrap", Math.Max(0, pivot.PageWrap).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("showDrill", pivot.ShowExpandCollapseButtons ? "1" : "0"),
            new XAttribute("enableDrill", pivot.EnableDrill ? "1" : "0"),
            new XAttribute("asteriskTotals", pivot.AsteriskTotals ? "1" : "0"),
            new XAttribute("multipleFieldFilters", pivot.MultipleFieldFilters ? "1" : "0"),
            // OOXML spells these as disableFieldList (inverse of EnableFieldDialog) and editData (the
            // EnableDataValueEditing flag). The earlier enableFieldDialog / enableDataValueEditing names
            // are not declared on CT_pivotTableDefinition and made Excel reject the workbook.
            new XAttribute("disableFieldList", pivot.EnableFieldDialog ? "0" : "1"),
            new XAttribute("enableFieldProperties", pivot.EnableFieldProperties ? "1" : "0"),
            new XAttribute("editData", pivot.EnableDataValueEditing ? "1" : "0"),
            new XAttribute("itemPrintTitles", pivot.PrintTitles ? "1" : "0"),
            new XAttribute("fieldPrintTitles", pivot.PrintTitles ? "1" : "0"),
            new XAttribute("printDrill", pivot.PrintExpandCollapseButtons ? "1" : "0"),
            new XAttribute("indent", Math.Clamp(pivot.CompactRowLabelIndent, 0, 15).ToString(CultureInfo.InvariantCulture)),
            // dataCaption is a REQUIRED attribute on CT_pivotTableDefinition; Excel defaults it to
            // "Values" when unspecified. Omitting it produces schema-invalid OOXML.
            new XAttribute("dataCaption", string.IsNullOrWhiteSpace(pivot.DataCaption) ? "Values" : pivot.DataCaption),
            OptionalAttribute("grandTotalCaption", pivot.GrandTotalCaption),
            OptionalAttribute("missingCaption", pivot.MissingCaption),
            OptionalAttribute("errorCaption", pivot.ErrorCaption),
            // OOXML has no single 'reportLayout' attribute; the compact/outline/tabular form is expressed
            // through compact / compactData / outline / outlineData on CT_pivotTableDefinition. gridDropZones
            // is a SEPARATE, orthogonal flag -- it is Excel's "Classic PivotTable Layout (enables dragging of
            // fields in the grid)" checkbox, not derived from the report-layout form -- so it must be driven
            // purely by ShowClassicLayout, not hardcoded per layout.
            PivotReportLayoutAttributes(pivot.ReportLayout).Where(a => a.Name.LocalName != "gridDropZones"),
            new XAttribute("gridDropZones", pivot.ShowClassicLayout ? "1" : "0"),
            new XElement(
                workbookNs + "location",
                new XAttribute("ref", (pivot.LastRenderedRange ?? pivot.TargetRange).ToString()),
                new XAttribute("firstDataCol", Math.Max(0, pivot.FirstDataColumn).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("firstDataRow", Math.Max(0, pivot.FirstDataRow).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("firstHeaderRow", Math.Max(0, pivot.FirstHeaderRow).ToString(CultureInfo.InvariantCulture))),
            ToPivotFieldsXml(pivot, pivotCache, calculatedFieldIndexes, workbookNs),
            ToPivotFieldCollectionXml("rowFields", pivot.RowFields, workbookNs),
            ToPivotFieldCollectionXml("colFields", pivot.ColumnFields, workbookNs),
            ToPivotPageFieldsXml(pivot.PageFields, pivotCache, workbookNs),
            ToPivotDataFieldsXml(pivot.DataFields, calculatedFieldIndexes, workbookNs, numberFormatIdMap),
            ToPivotCalculatedItemsXml(pivot.CalculatedItems, workbookNs),
            ToPivotValueFiltersXml(pivot.ValueFilters, workbookNs),
            ToPivotLabelFiltersXml(pivot.LabelFilters, workbookNs),
            ToPivotSortsXml(pivot.Sorts, workbookNs),
            new XElement(workbookNs + "pivotTableStyleInfo",
                new XAttribute("name", string.IsNullOrWhiteSpace(pivot.StyleName) ? "PivotStyleLight16" : pivot.StyleName),
                new XAttribute("showRowHeaders", pivot.ShowRowHeaders ? "1" : "0"),
                new XAttribute("showColHeaders", pivot.ShowColumnHeaders ? "1" : "0"),
                new XAttribute("showRowStripes", pivot.ShowRowStripes ? "1" : "0"),
                new XAttribute("showColStripes", pivot.ShowColumnStripes ? "1" : "0"),
                new XAttribute("showLastColumn", "1")),
            FreeXPivotTableExtension(pivot, workbookNs)));
    }

    // Persists pivot-table flags with no base-schema attribute (repeatItemLabels only exists in the x14
    // extension) and FreeX-authored field selection/grouping state inside a schema-valid extLst/ext block.
    // Returns null when nothing needs preserving.
    private static XElement? FreeXPivotTableExtension(PivotTableModel pivot, XNamespace workbookNs)
    {
        XNamespace freeXNs = FreeXPivotExtensionNamespace;
        var tableProps = new XElement(freeXNs + "tableProps");
        if (!pivot.RepeatItemLabels)
            tableProps.SetAttributeValue("repeatItemLabels", "0");
        if (!string.IsNullOrWhiteSpace(pivot.AltTextTitle))
            tableProps.SetAttributeValue("altTextTitle", pivot.AltTextTitle);
        if (!string.IsNullOrWhiteSpace(pivot.AltTextDescription))
            tableProps.SetAttributeValue("altTextDescription", pivot.AltTextDescription);

        var fields = new XElement(
            freeXNs + "fields",
            FreeXPivotFieldExtensionElements("row", pivot.RowFields, freeXNs),
            FreeXPivotFieldExtensionElements("column", pivot.ColumnFields, freeXNs),
            FreeXPivotFieldExtensionElements("page", pivot.PageFields, freeXNs));

        if (!tableProps.HasAttributes && !fields.HasElements)
            return null;
        if (fields.HasElements)
            tableProps.Add(fields);

        return new XElement(
            workbookNs + "extLst",
            new XElement(
                workbookNs + "ext",
                new XAttribute("uri", FreeXPivotTableExtensionUri),
                new XAttribute(XNamespace.Xmlns + "fx", FreeXPivotExtensionNamespace),
                tableProps));
    }

    private static IEnumerable<XElement> FreeXPivotFieldExtensionElements(
        string axis,
        IReadOnlyList<PivotFieldModel> fields,
        XNamespace freeXNs) =>
        fields
            .Where(field =>
                !string.IsNullOrWhiteSpace(field.SelectedItem) ||
                field.SelectedItems is { Count: > 0 } ||
                field.Grouping != PivotFieldGrouping.None ||
                field.GroupStart is not null ||
                field.GroupEnd is not null ||
                field.GroupInterval is not null)
            .Select(field => new XElement(
                freeXNs + "field",
                new XAttribute("axis", axis),
                new XAttribute("x", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                OptionalAttribute("selectedItem", field.SelectedItem),
                field.SelectedItems is { Count: > 0 } ? new XAttribute("selectedItems", string.Join(",", field.SelectedItems)) : null,
                field.Grouping == PivotFieldGrouping.None ? null : new XAttribute("groupBy", ToPivotFieldGroupingText(field.Grouping)),
                field.GroupStart is null ? null : new XAttribute("groupStart", FormatInvariant(field.GroupStart.Value)),
                field.GroupEnd is null ? null : new XAttribute("groupEnd", FormatInvariant(field.GroupEnd.Value)),
                field.GroupInterval is null ? null : new XAttribute("groupInterval", FormatInvariant(field.GroupInterval.Value))));

    private static XElement ToPivotFieldsXml(
        PivotTableModel pivot,
        PivotCacheModel? pivotCache,
        IReadOnlyDictionary<string, int> calculatedFieldIndexes,
        XNamespace workbookNs)
    {
        var maxFieldIndex = pivot.RowFields
            .Concat(pivot.ColumnFields)
            .Concat(pivot.PageFields)
            .Select(field => field.SourceFieldIndex)
            .Concat(pivot.DataFields.Select(field => ResolvePivotDataFieldIndex(field, calculatedFieldIndexes)))
            .Concat(calculatedFieldIndexes.Values)
            .DefaultIfEmpty(-1)
            .Max();
        var dataFieldIndexes = pivot.DataFields
            .Select(field => ResolvePivotDataFieldIndex(field, calculatedFieldIndexes))
            .Where(index => index >= 0)
            .ToHashSet();

        return new XElement(
            workbookNs + "pivotFields",
            new XAttribute("count", Math.Max(0, maxFieldIndex + 1).ToString(CultureInfo.InvariantCulture)),
            Enumerable.Range(0, Math.Max(0, maxFieldIndex + 1)).Select(index =>
            {
                var metadataField = FindPivotField(pivot, index);
                var isAxisField =
                    pivot.RowFields.Any(field => field.SourceFieldIndex == index) ||
                    pivot.ColumnFields.Any(field => field.SourceFieldIndex == index);
                var axisValue =
                    pivot.RowFields.Any(field => field.SourceFieldIndex == index) ? "axisRow" :
                    pivot.ColumnFields.Any(field => field.SourceFieldIndex == index) ? "axisCol" :
                    pivot.PageFields.Any(field => field.SourceFieldIndex == index) ? "axisPage" :
                    null;
                return new XElement(
                    workbookNs + "pivotField",
                    axisValue is not null ? new XAttribute("axis", axisValue) : null,
                    dataFieldIndexes.Contains(index) ? new XAttribute("dataField", "1") : null,
                    // insertBlankRow is the per-field flag for a blank line after items in OOXML
                    // (CT_PivotField); it is not a pivotTableDefinition attribute. repeatItemLabels has no
                    // home in the base spreadsheetml schema (it only exists in the x14 extension), so it is
                    // intentionally not emitted to keep output schema-valid; the modeled value is preserved
                    // and round-trips through the native JSON adapter.
                    isAxisField && pivot.BlankLineAfterItems ? new XAttribute("insertBlankRow", "1") : null,
                    // R52-io-pivot-layout-3-4: CT_PivotField's own compact/outline attributes (both default
                    // true when omitted) are what a real Excel client actually applies when rendering this
                    // field's header form -- the table-level compact/outline/outlineData/compactData
                    // attributes (PivotReportLayoutAttributes, written on the root <pivotTableDefinition>
                    // above) are only the defaults Excel seeds onto newly-added fields, not a live override of
                    // an existing field's own attributes. Without emitting these here, every axis field keeps
                    // the schema default (Compact form) regardless of the table's actual ReportLayout choice.
                    isAxisField ? new XAttribute("compact", pivot.ReportLayout == PivotReportLayout.Compact ? "1" : "0") : null,
                    isAxisField ? new XAttribute("outline", pivot.ReportLayout == PivotReportLayout.Tabular ? "0" : "1") : null,
                    pivot.ShowSubtotals ? new XAttribute("defaultSubtotal", "1") : null,
                    pivot.ShowSubtotals && pivot.SubtotalPlacement == PivotSubtotalPlacement.Top ? new XAttribute("subtotalTop", "1") : null,
                    new XAttribute("showAll", metadataField?.ShowAll == true ? "1" : "0"),
                    ToOptionalBoolAttribute("includeNewItemsInFilter", metadataField?.IncludeNewItemsInFilter),
                    ToOptionalBoolAttribute("multipleItemSelectionAllowed", metadataField?.MultipleItemSelectionAllowed),
                    ToOptionalBoolAttribute("dragToRow", metadataField?.DragToRow),
                    ToOptionalBoolAttribute("dragToCol", metadataField?.DragToColumn),
                    ToOptionalBoolAttribute("dragToPage", metadataField?.DragToPage),
                    ToOptionalBoolAttribute("dragToData", metadataField?.DragToData),
                    ToOptionalBoolAttribute("showDropDowns", metadataField?.ShowDropDowns),
                    ToPivotFieldItemsXml(metadataField, pivotCache, index, workbookNs));
            }));
    }

    // R54-io-pivot-filter-3-3: a manual per-item filter (unchecked values in a field's filter dropdown --
    // PivotFieldModel.SelectedItems) was previously dropped entirely for a brand-new pivot table's first
    // save: this always emitted a single placeholder <items count="1"><item t="default"/></items>
    // regardless of SelectedItems, never reading it or enumerating one <item> per pivot-cache shared item.
    // Emit one <item x="i" hidden="1"?/> per shared item (matching real Excel's shape -- and
    // XlsxFileAdapter.SavePostProcessing.cs's RewritePreservedPivotFieldItemFilters/
    // ResolvePreservedRawToMaterializedIndexMap, which already reads this exact shape back on the
    // preserved-part path) whenever the model records an explicit selection for this field; otherwise
    // keep the schema-minimum placeholder (no explicit selection means "everything visible", the default).
    private static XElement ToPivotFieldItemsXml(
        PivotFieldModel? metadataField,
        PivotCacheModel? pivotCache,
        int fieldIndex,
        XNamespace workbookNs)
    {
        if (metadataField?.SelectedItems is { } selectedItems &&
            pivotCache is not null &&
            fieldIndex >= 0 &&
            fieldIndex < pivotCache.Fields.Count &&
            pivotCache.Fields[fieldIndex].SharedItems is { Count: > 0 } sharedItems)
        {
            var selectedSet = new HashSet<string>(selectedItems, StringComparer.Ordinal);
            var items = new List<XElement>(sharedItems.Count + 1);
            for (var rawIndex = 0; rawIndex < sharedItems.Count; rawIndex++)
            {
                var isHidden = !selectedSet.Contains(sharedItems[rawIndex]);
                items.Add(new XElement(
                    workbookNs + "item",
                    new XAttribute("x", rawIndex.ToString(CultureInfo.InvariantCulture)),
                    isHidden ? new XAttribute("hidden", "1") : null));
            }

            items.Add(new XElement(workbookNs + "item", new XAttribute("t", "default")));

            return new XElement(
                workbookNs + "items",
                new XAttribute("count", items.Count.ToString(CultureInfo.InvariantCulture)),
                items);
        }

        return new XElement(
            workbookNs + "items",
            new XAttribute("count", "1"),
            new XElement(workbookNs + "item", new XAttribute("t", "default")));
    }

    private static PivotFieldModel? FindPivotField(PivotTableModel pivot, int sourceFieldIndex) =>
        pivot.RowFields
            .Concat(pivot.ColumnFields)
            .Concat(pivot.PageFields)
            .LastOrDefault(field => field.SourceFieldIndex == sourceFieldIndex);

    private static XElement? ToPivotFieldCollectionXml(string elementName, IReadOnlyList<PivotFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + elementName,
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "field",
                    new XAttribute("x", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)))));

    private static XElement? ToPivotPageFieldsXml(
        IReadOnlyList<PivotFieldModel> fields,
        PivotCacheModel? pivotCache,
        XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + "pageFields",
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "pageField",
                    new XAttribute("fld", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    ToPivotPageFieldItemAttribute(field, pivotCache),
                    ToPivotPageFieldNameAttribute(field, pivotCache))));

    private static XAttribute? ToPivotPageFieldItemAttribute(PivotFieldModel field, PivotCacheModel? pivotCache)
    {
        var itemIndex = ResolvePivotPageFieldSelectedItemIndex(field, pivotCache);
        return itemIndex is null
            ? null
            : new XAttribute("item", itemIndex.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static XAttribute? ToPivotPageFieldNameAttribute(PivotFieldModel field, PivotCacheModel? pivotCache) =>
        string.IsNullOrWhiteSpace(field.SelectedItem) || ResolvePivotPageFieldSelectedItemIndex(field, pivotCache) is not null
            ? null
            : new XAttribute("name", field.SelectedItem);

    private static int? ResolvePivotPageFieldSelectedItemIndex(PivotFieldModel field, PivotCacheModel? pivotCache)
    {
        if (string.IsNullOrWhiteSpace(field.SelectedItem) ||
            pivotCache is null ||
            field.SourceFieldIndex < 0 ||
            field.SourceFieldIndex >= pivotCache.Fields.Count ||
            pivotCache.Fields[field.SourceFieldIndex].SharedItems is not { Count: > 0 } sharedItems)
        {
            return null;
        }

        for (var index = 0; index < sharedItems.Count; index++)
        {
            if (string.Equals(sharedItems[index], field.SelectedItem, StringComparison.Ordinal))
                return index;
        }

        return null;
    }

    private static XElement? ToPivotDataFieldsXml(
        IReadOnlyList<PivotDataFieldModel> fields,
        IReadOnlyDictionary<string, int> calculatedFieldIndexes,
        XNamespace workbookNs,
        IReadOnlyDictionary<int, int> numberFormatIdMap) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + "dataFields",
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "dataField",
                    new XAttribute("name", string.IsNullOrWhiteSpace(field.Name) ? "Values" : field.Name),
                    new XAttribute("fld", ResolvePivotDataFieldIndex(field, calculatedFieldIndexes).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("subtotal", string.IsNullOrWhiteSpace(field.SummaryFunction) ? "sum" : field.SummaryFunction),
                    // CT_DataField's real OOXML attribute is showDataAs (ST_ShowDataAs), not showValuesAs --
                    // see ToPivotShowValuesAsText (R36-io-pivot-cache-2-1).
                    field.ShowValuesAs == PivotShowValuesAs.None ? null : new XAttribute("showDataAs", ToPivotShowValuesAsText(field.ShowValuesAs)),
                    field.BaseFieldIndex is { } baseField ? new XAttribute("baseField", baseField.ToString(CultureInfo.InvariantCulture)) : null,
                    string.IsNullOrWhiteSpace(field.BaseItem) ? null : new XAttribute("baseItem", field.BaseItem),
                    ToPivotNumberFormatAttribute(field, numberFormatIdMap))));

    private static XAttribute? ToPivotNumberFormatAttribute(
        PivotDataFieldModel field,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
    {
        if (field.NumberFormatId is not { } numberFormatId)
            return null;

        var mappedId = numberFormatIdMap.TryGetValue(numberFormatId, out var remapped)
            ? remapped
            : numberFormatId;
        return new XAttribute("numFmtId", mappedId.ToString(CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, int> CreateCalculatedFieldIndexMap(
        PivotCacheModel cache,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields)
    {
        var fields = GetEffectivePivotCacheFields(cache, calculatedFields);
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if ((field.IsDatabaseField && string.IsNullOrWhiteSpace(field.Formula)) ||
                string.IsNullOrWhiteSpace(field.Name))
            {
                continue;
            }

            result.TryAdd(field.Name.Trim(), index);
        }

        return result;
    }

    private static Dictionary<string, int> CreateCalculatedFieldIndexMap(PivotTableModel pivot)
    {
        var maxSourceFieldIndex = pivot.RowFields
            .Concat(pivot.ColumnFields)
            .Concat(pivot.PageFields)
            .Select(field => field.SourceFieldIndex)
            .Concat(pivot.DataFields.Select(field => field.SourceFieldIndex))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Max();
        var nextIndex = maxSourceFieldIndex + 1;
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in pivot.CalculatedFields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
                continue;

            result.TryAdd(field.Name.Trim(), nextIndex++);
        }

        return result;
    }

    private static int ResolvePivotDataFieldIndex(
        PivotDataFieldModel field,
        IReadOnlyDictionary<string, int> calculatedFieldIndexes)
    {
        if (!string.IsNullOrWhiteSpace(field.CalculatedFieldName) &&
            calculatedFieldIndexes.TryGetValue(field.CalculatedFieldName.Trim(), out var calculatedFieldIndex))
        {
            return calculatedFieldIndex;
        }

        return Math.Max(0, field.SourceFieldIndex);
    }

    private static XElement? ToPivotCalculatedItemsXml(IReadOnlyList<PivotCalculatedItemModel> items, XNamespace workbookNs) =>
        items.Count == 0
            ? null
            : new XElement(
                workbookNs + "calculatedItems",
                new XAttribute("count", items.Count.ToString(CultureInfo.InvariantCulture)),
                items.Select(item => new XElement(
                    workbookNs + "calculatedItem",
                    new XAttribute("field", item.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("name", item.Name),
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
                                new XAttribute("selected", "0")))))));

    // Internal (not private): also called from XlsxFileAdapter.SavePostProcessing.cs's
    // RewritePreservedPivotValueAndLabelFilters to regenerate these elements on the hasSourcePackage
    // (preserved-part) save path, where this class's own Save() never runs (R54-io-pivot-filter-3-1).
    internal static XElement? ToPivotValueFiltersXml(IReadOnlyList<PivotValueFilterModel> filters, XNamespace workbookNs) =>
        filters.Count == 0
            ? null
            : new XElement(
                workbookNs + "valueFilters",
                new XAttribute("count", filters.Count.ToString(CultureInfo.InvariantCulture)),
                filters.Select(filter => new XElement(
                    workbookNs + "valueFilter",
                    new XAttribute("dataField", filter.DataFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("type", ToPivotValueFilterKindText(filter.Kind)),
                    new XAttribute("count", filter.Count.ToString(CultureInfo.InvariantCulture)),
                    filter.SourceFieldIndex is null ? null : new XAttribute("field", filter.SourceFieldIndex.Value.ToString(CultureInfo.InvariantCulture)),
                    filter.ComparisonValue is null ? null : new XAttribute("comparisonValue", FormatInvariant(filter.ComparisonValue.Value)),
                    filter.ComparisonValue2 is null ? null : new XAttribute("comparisonValue2", FormatInvariant(filter.ComparisonValue2.Value)))));

    internal static XElement? ToPivotLabelFiltersXml(IReadOnlyList<PivotLabelFilterModel> filters, XNamespace workbookNs) =>
        filters.Count == 0
            ? null
            : new XElement(
                workbookNs + "labelFilters",
                new XAttribute("count", filters.Count.ToString(CultureInfo.InvariantCulture)),
                filters.Select(filter => new XElement(
                    workbookNs + "labelFilter",
                    new XAttribute("field", filter.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("type", ToPivotLabelFilterKindText(filter.Kind)),
                    new XAttribute("value", filter.Value),
                    string.IsNullOrWhiteSpace(filter.Value2) ? null : new XAttribute("value2", filter.Value2))));

    private static XElement? ToPivotSortsXml(IReadOnlyList<PivotSortModel> sorts, XNamespace workbookNs) =>
        sorts.Count == 0
            ? null
            : new XElement(
                workbookNs + "pivotSorts",
                new XAttribute("count", sorts.Count.ToString(CultureInfo.InvariantCulture)),
                sorts.Select(sort => new XElement(
                    workbookNs + "pivotSort",
                    new XAttribute("target", sort.Target == PivotSortTarget.Label ? "label" : "value"),
                    new XAttribute("direction", sort.Direction == PivotSortDirection.Descending ? "descending" : "ascending"),
                    new XAttribute("dataField", sort.DataFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("field", sort.FieldIndex.ToString(CultureInfo.InvariantCulture)))));

}
