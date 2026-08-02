using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableWriter
{
    // R60-io-pivot-layout-6-1: the real Excel wire format for "Repeat All Item Labels" is the x14
    // extension on each axis pivotField (DocumentFormat.OpenXml.Office2010.Excel.PivotField.FillDownLabels),
    // NOT the private fx:tableProps repeatItemLabels attribute FreeX also writes. Ext URI per
    // [MS-XLSX] section on pivotField extLst.
    private static readonly XNamespace X14PivotFieldExtensionNamespace = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string X14PivotFieldExtensionUri = "{2946ED86-A175-432A-8AC1-64E0C546D7DE}";

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        PivotNumberFormatIdMap numberFormatIdMap)
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
        var calculatedItemsByCacheId = GetCalculatedItemsByCacheId(workbook);
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
            var calculatedItems = calculatedItemsByCacheId.TryGetValue(cache.CacheId, out var cacheCalculatedItems)
                ? cacheCalculatedItems
                : [];
            calculatedFieldIndexesByCacheId[cache.CacheId] = CreateCalculatedFieldIndexMap(cache, calculatedFields);
            var cacheRecords = ToPivotCacheRecordsXml(cache, workbook, workbookNs);
            // Resync cache.Fields' type/range metadata against the live source data (which the records
            // above were just generated from) so the saved cache definition doesn't contradict its own
            // records when the underlying cells were edited since the cache was loaded/created.
            ResyncPivotCacheFieldTypeMetadata(cache, workbook);
            XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, ToPivotCacheDefinitionXml(cache, calculatedFields, calculatedItems, workbookNs, relNs, recordsRelId, cacheRecords.RecordCount, numberFormatIdMap));
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

    // R116-io-pivot-calcitem-part: Calculated Items are modeled per-PivotTableModel (mirroring
    // CalculatedFields above), but the real OOXML home for them is the shared pivotCacheDefinition part
    // (CT_PivotCacheDefinition.calculatedItems, ECMA-376 18.10.1.3) -- every pivot table built on the same
    // cache shows the same calculated items in real Excel. Union the (field, formula) pairs across every
    // pivot table sharing a cache so the single write of that cache's calculatedItems reflects whichever
    // pivot(s) the user actually defined the item on, deduped so two pivots on the same cache that both
    // carry the identical item (the common case) don't double it up.
    private static Dictionary<int, IReadOnlyList<PivotCalculatedItemModel>> GetCalculatedItemsByCacheId(Workbook workbook)
    {
        var result = new Dictionary<int, List<PivotCalculatedItemModel>>();
        foreach (var pivot in workbook.Sheets.SelectMany(sheet => sheet.PivotTables))
        {
            if (pivot.CacheId <= 0 || pivot.CalculatedItems.Count == 0)
                continue;

            if (!result.TryGetValue(pivot.CacheId, out var items))
            {
                items = [];
                result[pivot.CacheId] = items;
            }

            foreach (var item in pivot.CalculatedItems)
            {
                if (string.IsNullOrWhiteSpace(item.Name) ||
                    string.IsNullOrWhiteSpace(item.Formula) ||
                    items.Any(existing => existing.SourceFieldIndex == item.SourceFieldIndex &&
                                           string.Equals(existing.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                items.Add(item);
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<PivotCalculatedItemModel>)pair.Value);
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
        PivotNumberFormatIdMap numberFormatIdMap,
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
        PivotNumberFormatIdMap numberFormatIdMap)
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
            // R116-io-pivot-calcitem-part: calculatedItems is NOT a child of CT_pivotTableDefinition at
            // all (verified via reflection against DocumentFormat.OpenXml.Spreadsheet.PivotTableDefinition
            // -- it has no CalculatedItems property). It is a child of CT_PivotCacheDefinition instead
            // (PivotCacheDefinition.CalculatedItems exists, positioned after tupleCache/before
            // calculatedMembers), so it is now emitted by ToPivotCacheDefinitionXml
            // (XlsxPivotTableWriter.Cache.cs) into the pivotCacheDefinitionN.xml part. Emitting it here
            // produced schema-invalid pivotTableDefinition XML that real Excel's repair flow silently
            // dropped the calculated item from on every open.
            // R82-io-pivot-layout-5-2: CT_pivotTableDefinition's real child sequence (verified via
            // OpenXmlValidator against the OpenXml SDK's own PivotTableDefinition property order) places
            // pivotTableStyleInfo BEFORE filters -- the OLD code emitted the (also-invented) valueFilters/
            // labelFilters/pivotSorts elements before pivotTableStyleInfo, which is backwards even setting
            // aside those elements not being real CT_pivotTableDefinition children at all.
            new XElement(workbookNs + "pivotTableStyleInfo",
                new XAttribute("name", string.IsNullOrWhiteSpace(pivot.StyleName) ? "PivotStyleLight16" : pivot.StyleName),
                new XAttribute("showRowHeaders", pivot.ShowRowHeaders ? "1" : "0"),
                new XAttribute("showColHeaders", pivot.ShowColumnHeaders ? "1" : "0"),
                new XAttribute("showRowStripes", pivot.ShowRowStripes ? "1" : "0"),
                new XAttribute("showColStripes", pivot.ShowColumnStripes ? "1" : "0"),
                new XAttribute("showLastColumn", "1")),
            // R82-io-pivot-layout-5-2: AboveAverage/BelowAverage value filters have no representation in
            // the real <filters> mechanism at all (see ToNativePivotValueFilterKindText) -- reuse the
            // FreeX-authored <valueFilters> shape (unchanged, still used by XlsxFileAdapter.
            // SavePostProcessing.cs's RewritePreservedPivotValueAndLabelFilters on the preserved-part save
            // path) purely so those two kinds still round-trip through FreeX itself.
            ToPivotValueFiltersXml(pivot.ValueFilters.Where(filter => filter.Kind is PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage).ToList(), workbookNs),
            // R82-io-pivot-layout-5-2: every other value-filter kind, plus every label-filter kind, now
            // goes through the REAL <filters>/<filter> shape instead of the invented <valueFilters>/
            // <labelFilters> elements ToPivotValueFiltersXml/ToPivotLabelFiltersXml above and below emit
            // (those two functions are kept only for the AboveAverage/BelowAverage fallback and the
            // preserved-part patch path, respectively).
            ToPivotFiltersXml(pivot.ValueFilters, pivot.LabelFilters, workbookNs),
            FreeXPivotTableExtension(pivot, workbookNs)));
    }

    // Persists FreeX-authored field selection/grouping state, plus a legacy repeatItemLabels marker kept
    // ONLY for back-compat with older FreeX-authored files, inside a schema-valid extLst/ext block. The
    // real OOXML wire format for "Repeat All Item Labels" is the per-field x14 fillDownLabels extension
    // (see ToX14FillDownLabelsExtension), which real Excel actually reads/writes.
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

    // R60-io-pivot-layout-6-1: builds the real per-field x14 extension real Excel uses for
    // "Repeat All Item Labels" -- <extLst><ext uri="{2946ED86-...}"><x14:pivotField fillDownLabels="1"/></ext></extLst>.
    private static XElement ToX14FillDownLabelsExtension(XNamespace workbookNs) =>
        new(
            workbookNs + "extLst",
            new XElement(
                workbookNs + "ext",
                new XAttribute("uri", X14PivotFieldExtensionUri),
                new XAttribute(XNamespace.Xmlns + "x14", X14PivotFieldExtensionNamespace),
                new XElement(X14PivotFieldExtensionNamespace + "pivotField", new XAttribute("fillDownLabels", "1"))));

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
                // R82-io-pivot-layout-5-2: a row/column field's sort order is expressed on the
                // CT_PivotField ITSELF (sortType + an autoSortScope child identifying the driving data
                // field for a value sort) -- NOT the invented top-level <pivotSorts> element ToPivotSortsXml
                // below emits, which isn't part of CT_pivotTableDefinition's content model at all. Only
                // meaningful for an axis field; a sort recorded against a filter/page field (which the UI
                // never allows) is dropped rather than emitted somewhere schema-invalid.
                var sort = isAxisField ? pivot.Sorts.LastOrDefault(s => s.FieldIndex == index) : null;
                return new XElement(
                    workbookNs + "pivotField",
                    axisValue is not null ? new XAttribute("axis", axisValue) : null,
                    dataFieldIndexes.Contains(index) ? new XAttribute("dataField", "1") : null,
                    // insertBlankRow is the per-field flag for a blank line after items in OOXML
                    // (CT_PivotField); it is not a pivotTableDefinition attribute. repeatItemLabels has no
                    // home in the base spreadsheetml schema -- it is emitted below as the real x14
                    // fillDownLabels per-field extension (ToX14FillDownLabelsExtension).
                    isAxisField && pivot.BlankLineAfterItems ? new XAttribute("insertBlankRow", "1") : null,
                    // R52-io-pivot-layout-3-4 / R75-io-pivottable-layout-4-3: CT_PivotField's own
                    // compact/outline attributes (both default true when omitted) are what a real Excel
                    // client actually applies when rendering this field's header form -- the table-level
                    // compact/outline/outlineData/compactData attributes (PivotReportLayoutAttributes,
                    // written on the root <pivotTableDefinition> above) are only the defaults Excel seeds
                    // onto newly-added fields, not a live override of an existing field's own attributes.
                    // Prefer this field's OWN ReportLayout (metadataField?.ReportLayout) when the model
                    // records one, falling back to the table-wide pivot.ReportLayout otherwise -- this is
                    // what lets two axis fields carry independently different report forms.
                    isAxisField ? new XAttribute("compact", (metadataField?.ReportLayout ?? pivot.ReportLayout) == PivotReportLayout.Compact ? "1" : "0") : null,
                    isAxisField ? new XAttribute("outline", (metadataField?.ReportLayout ?? pivot.ReportLayout) == PivotReportLayout.Tabular ? "0" : "1") : null,
                    // R75-io-pivottable-layout-4-2: prefer this field's OWN ShowSubtotals/SubtotalPlacement
                    // when the model records one, falling back to the table-wide pivot.ShowSubtotals/
                    // SubtotalPlacement otherwise -- mirrors the ReportLayout fallback above. Written
                    // unconditionally (not gated on the effective value being true), matching subtotalTop's
                    // own R60-io-pivot-layout-6-2 rationale: the OOXML schema default for an omitted
                    // defaultSubtotal is TRUE, so omitting it for an off field would silently revert to
                    // subtotals-shown on the next load.
                    new XAttribute("defaultSubtotal", (metadataField?.ShowSubtotals ?? pivot.ShowSubtotals) ? "1" : "0"),
                    // R60-io-pivot-layout-6-2: the OOXML schema default for subtotalTop (when omitted) is
                    // TRUE (Top), so Bottom placement must explicitly write "0" -- omitting the attribute
                    // for Bottom (as before) is schema-identical to Top and silently reverts the user's
                    // choice when opened in real Excel or any correct OOXML consumer. Unlike defaultSubtotal,
                    // this is written unconditionally (not gated on pivot.ShowSubtotals): subtotalTop is the
                    // model's persisted top/bottom PREFERENCE, independent of whether subtotals currently
                    // display -- gating it on ShowSubtotals would silently forget a user's Bottom choice
                    // (defaulting back to Top on read) the moment subtotals are toggled off.
                    new XAttribute("subtotalTop", (metadataField?.SubtotalPlacement ?? pivot.SubtotalPlacement) == PivotSubtotalPlacement.Top ? "1" : "0"),
                    // R116-io-pivot-showall: CT_PivotField's showAll defaults to TRUE when omitted
                    // (ECMA-376 18.3.1.66). Unlike defaultSubtotal/subtotalTop above (which are written
                    // unconditionally because their table-wide fallback is always known), showAll has no
                    // such fallback -- ShowAll is null whenever the source file legitimately omitted the
                    // attribute (relying on the true default) and the user never touched the field's
                    // filter settings. Collapsing that null to "0" (the old `== true ? "1" : "0"` ternary)
                    // silently flipped the field to showAll=false on the very next save. Use the optional
                    // form, matching every other unknown-default attribute on this element
                    // (includeNewItemsInFilter, multipleItemSelectionAllowed, dragTo*, showDropDowns
                    // below), so an unset ShowAll stays omitted and the true default is preserved.
                    ToOptionalBoolAttribute("showAll", metadataField?.ShowAll),
                    ToOptionalBoolAttribute("includeNewItemsInFilter", metadataField?.IncludeNewItemsInFilter),
                    ToOptionalBoolAttribute("multipleItemSelectionAllowed", metadataField?.MultipleItemSelectionAllowed),
                    ToOptionalBoolAttribute("dragToRow", metadataField?.DragToRow),
                    ToOptionalBoolAttribute("dragToCol", metadataField?.DragToColumn),
                    ToOptionalBoolAttribute("dragToPage", metadataField?.DragToPage),
                    ToOptionalBoolAttribute("dragToData", metadataField?.DragToData),
                    ToOptionalBoolAttribute("showDropDowns", metadataField?.ShowDropDowns),
                    sort is not null ? new XAttribute("sortType", sort.Direction == PivotSortDirection.Descending ? "descending" : "ascending") : null,
                    ToPivotFieldItemsXml(metadataField, pivotCache, index, workbookNs),
                    // CT_PivotField declares autoSortScope BEFORE its extLst (PivotFieldExtensionList) --
                    // must come after <items> and before the x14 fillDownLabels extension below.
                    sort is { Target: PivotSortTarget.Value } ? ToPivotFieldAutoSortScopeXml(sort.DataFieldIndex, workbookNs) : null,
                    // R60-io-pivot-layout-6-1: emit the real x14 fillDownLabels extension (the private
                    // fx:tableProps repeatItemLabels attribute below is kept only for FreeX's own
                    // back-compat round-trip; it is not real OOXML and real Excel never reads it).
                    isAxisField && pivot.RepeatItemLabels ? ToX14FillDownLabelsExtension(workbookNs) : null);
            }));
    }

    // ECMA-376's CT_Reference "field" attribute is an xsd:unsignedInt; the special sentinel Excel uses to
    // mark "this reference identifies the Values/data axis, not an ordinary row/column field" is -2 in its
    // unsigned wire form. Mirrors XlsxPivotTableReader.FiltersAndSorts.cs's
    // PivotFieldDataAxisReferenceValue (duplicated locally because that constant is private there).
    private const string PivotFieldDataAxisReferenceValue = "4294967294";

    // R82-io-pivot-layout-5-2: writes the REAL OOXML shape for "sort by a data field's value" -- a
    // <pivotField>'s own <autoSortScope><pivotArea><references><reference field="{sentinel}"><x v="N"/>
    // identifying which data field drives the order. Verified schema-valid via OpenXmlValidator; mirrors
    // XlsxPivotTableReader.FiltersAndSorts.cs's ReadAutoSortScopeDataFieldIndex, which already parses this
    // exact shape back.
    private static XElement ToPivotFieldAutoSortScopeXml(int dataFieldIndex, XNamespace workbookNs) =>
        new(workbookNs + "autoSortScope",
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
                        new XAttribute("field", PivotFieldDataAxisReferenceValue),
                        new XElement(workbookNs + "x", new XAttribute("v", dataFieldIndex.ToString(CultureInfo.InvariantCulture)))))));

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

    // Internal (not private): also called from XlsxFileAdapter.SavePostProcessing.cs's
    // RewritePreservedPivotFieldAxes to regenerate the rowFields/colFields containers on the
    // hasSourcePackage (preserved-part) save path when a field moves between Row/Column/Filter areas
    // (R82-io-pivot-layout-5-1), where this class's own Save() never runs.
    internal static XElement? ToPivotFieldCollectionXml(string elementName, IReadOnlyList<PivotFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + elementName,
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "field",
                    new XAttribute("x", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)))));

    // Internal (not private): sibling of ToPivotFieldCollectionXml above, also called from
    // RewritePreservedPivotFieldAxes to regenerate the pageFields container (R82-io-pivot-layout-5-1).
    internal static XElement? ToPivotPageFieldsXml(
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
        PivotNumberFormatIdMap numberFormatIdMap) =>
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
        PivotNumberFormatIdMap numberFormatIdMap)
    {
        if (field.NumberFormatId is not { } numberFormatId)
            return null;

        // R118-io-numfmt-pivot-sentinel-collision: resolve by (id, code) rather than id alone -- two data
        // fields can share the same hardcoded sentinel id (see PivotValueFieldPlanner) with different
        // custom format text, and a plain id-only lookup cannot tell them apart.
        var mappedId = numberFormatIdMap.ResolveDataFieldNumberFormatId(numberFormatId, field.NumberFormatCode);
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

    // R116-io-pivot-calcitem-part: the calculatedItems emitter now lives in XlsxPivotTableWriter.Cache.cs
    // (ToPivotCacheCalculatedItemsXml) since CT_CalculatedItem is a child of CT_PivotCacheDefinition, not
    // CT_pivotTableDefinition -- see the comment on the ToPivotTableDefinitionXml call site above.

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

    // R82-io-pivot-layout-5-2: the REAL OOXML shape for pivot value/label filters is a single
    // CT_PivotFilters <filters> collection of CT_PivotFilter <filter> elements (fld/iMeasureFld identify
    // the target field(s), type is a real ST_PivotFilterType token, stringValue1/stringValue2 carry the
    // comparison operand(s)) -- NOT the invented top-level <valueFilters>/<labelFilters> elements
    // ToPivotValueFiltersXml/ToPivotLabelFiltersXml above emit, which aren't declared anywhere in
    // CT_pivotTableDefinition's content model and make real Excel repair/drop the workbook. Verified
    // schema-valid via OpenXmlValidator (FileFormatVersions.Microsoft365); mirrors
    // XlsxPivotTableReader.FiltersAndSorts.cs's ReadNativePivotValueFilters/ReadNativePivotLabelFilters,
    // which already parse this exact shape back.
    // Internal (not private): also called from XlsxFileAdapter.SavePostProcessing.cs's
    // RewritePreservedPivotValueAndLabelFilters to regenerate the real <filters> element on the
    // hasSourcePackage (preserved-part) save path, mirroring the fresh-part fix immediately above
    // (R83-order-guard-invented-sweep-1).
    internal static XElement? ToPivotFiltersXml(
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        XNamespace workbookNs)
    {
        var filterElements = new List<XElement>();
        var nextId = 0;

        foreach (var filter in valueFilters)
        {
            var nativeType = ToNativePivotValueFilterKindText(filter.Kind);
            if (nativeType is null)
                continue; // AboveAverage/BelowAverage: no real ST_PivotFilterType token -- see the converter.

            filterElements.Add(new XElement(
                workbookNs + "filter",
                new XAttribute("fld", (filter.SourceFieldIndex ?? 0).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("type", nativeType),
                new XAttribute("id", (nextId++).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("iMeasureFld", filter.DataFieldIndex.ToString(CultureInfo.InvariantCulture)),
                filter.ComparisonValue is null ? null : new XAttribute("stringValue1", FormatInvariant(filter.ComparisonValue.Value)),
                filter.ComparisonValue2 is null ? null : new XAttribute("stringValue2", FormatInvariant(filter.ComparisonValue2.Value)),
                ToPivotValueFilterAutoFilterFillerXml(filter, workbookNs)));
        }

        foreach (var filter in labelFilters)
        {
            filterElements.Add(new XElement(
                workbookNs + "filter",
                new XAttribute("fld", filter.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("type", ToNativePivotLabelFilterKindText(filter.Kind)),
                new XAttribute("id", (nextId++).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("stringValue1", filter.Value),
                string.IsNullOrWhiteSpace(filter.Value2) ? null : new XAttribute("stringValue2", filter.Value2),
                ToPivotLabelFilterAutoFilterFillerXml(filter, workbookNs)));
        }

        return filterElements.Count == 0
            ? null
            : new XElement(
                workbookNs + "filters",
                new XAttribute("count", filterElements.Count.ToString(CultureInfo.InvariantCulture)),
                filterElements);
    }

    // CT_PivotFilter declares <autoFilter> as a REQUIRED child (confirmed via OpenXmlValidator against
    // the real schema -- omitting it produces "incomplete content" errors). FreeX's own reader
    // (ReadNativePivotValueFilters) never looks inside it; all criteria are read straight off the
    // <filter>'s own attributes above. Emitting the real <top10>/<customFilters> shape here (rather than
    // an arbitrary placeholder) means a real-Excel user who reopens the filter dialog afterwards sees
    // sensible pre-filled criteria instead of nonsense.
    private static XElement ToPivotValueFilterAutoFilterFillerXml(PivotValueFilterModel filter, XNamespace workbookNs)
    {
        var filterColumn = new XElement(workbookNs + "filterColumn", new XAttribute("colId", "0"));
        if (filter.Kind is PivotValueFilterKind.Top or PivotValueFilterKind.Bottom)
        {
            // Real ST_PivotFilterType has no separate "bottom" token (see ToNativePivotValueFilterKindText)
            // -- direction lives on <top10>'s own "top" attribute (schema default true = Top).
            filterColumn.Add(new XElement(
                workbookNs + "top10",
                new XAttribute("val", filter.Count.ToString(CultureInfo.InvariantCulture)),
                filter.Kind == PivotValueFilterKind.Bottom ? new XAttribute("top", "0") : null));
        }
        else
        {
            var isRange = filter.Kind is PivotValueFilterKind.Between or PivotValueFilterKind.NotBetween;
            var customFilters = new XElement(
                workbookNs + "customFilters",
                isRange ? new XAttribute("and", filter.Kind == PivotValueFilterKind.Between ? "1" : "0") : null);
            customFilters.Add(new XElement(
                workbookNs + "customFilter",
                new XAttribute("operator", ToPivotComparisonAutoFilterOperator(filter.Kind)),
                filter.ComparisonValue is null ? null : new XAttribute("val", FormatInvariant(filter.ComparisonValue.Value))));
            if (isRange && filter.ComparisonValue2 is not null)
            {
                customFilters.Add(new XElement(
                    workbookNs + "customFilter",
                    new XAttribute("operator", filter.Kind == PivotValueFilterKind.Between ? "lessThanOrEqual" : "greaterThan"),
                    new XAttribute("val", FormatInvariant(filter.ComparisonValue2.Value))));
            }

            filterColumn.Add(customFilters);
        }

        return new XElement(workbookNs + "autoFilter", filterColumn);
    }

    // Sibling of ToPivotValueFilterAutoFilterFillerXml, for label (caption) filters.
    private static XElement ToPivotLabelFilterAutoFilterFillerXml(PivotLabelFilterModel filter, XNamespace workbookNs)
    {
        var customFilters = new XElement(workbookNs + "customFilters");
        customFilters.Add(new XElement(
            workbookNs + "customFilter",
            new XAttribute("operator", "equal"),
            new XAttribute("val", filter.Value)));
        return new XElement(
            workbookNs + "autoFilter",
            new XElement(workbookNs + "filterColumn", new XAttribute("colId", "0"), customFilters));
    }
}
