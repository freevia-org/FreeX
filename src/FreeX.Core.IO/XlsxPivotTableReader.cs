using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableReader
{
    public static PivotPackageMetadata Load(
        Stream xlsxStream,
        IReadOnlyDictionary<int, string> numberFormatCatalog)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            return Load(archive, numberFormatCatalog);
        }
        catch
        {
            return PivotPackageMetadata.Empty;
        }
    }

    internal static PivotPackageMetadata Load(
        ZipArchive archive,
        IReadOnlyDictionary<int, string> numberFormatCatalog)
    {
        try
        {
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || workbookRelsEntry is null)
                return PivotPackageMetadata.Empty;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var workbookRelsXml = XlsxPackageXmlEditor.LoadXml(workbookRelsEntry);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var workbookRels = XlsxRelationshipReader.ReadTargets(
                workbookRelsXml,
                packageRelNs,
                target => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target));

            var pivotCaches = XlsxPivotCacheReader.Load(archive, workbookXml, workbookRels, workbookNs, relNs);
            var pivotCachesById = pivotCaches.ToDictionary(cache => cache.CacheId);
            var sheetsByPath = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
                .ToDictionary(pair => pair.WorksheetPath, pair => pair.SheetName, StringComparer.OrdinalIgnoreCase);
            var pivotTablesBySheetName = LoadPivotTablesBySheetName(archive, sheetsByPath, pivotCachesById, numberFormatCatalog, workbookNs, relNs, packageRelNs);

            return new PivotPackageMetadata(pivotCaches, pivotTablesBySheetName);
        }
        catch
        {
            return PivotPackageMetadata.Empty;
        }
    }

    private static Dictionary<string, List<PendingPivotTableModel>> LoadPivotTablesBySheetName(
        ZipArchive archive,
        IReadOnlyDictionary<string, string> sheetsByPath,
        IReadOnlyDictionary<int, PivotCacheModel> pivotCachesById,
        IReadOnlyDictionary<int, string> numberFormatCatalog,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var result = new Dictionary<string, List<PendingPivotTableModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (worksheetPath, sheetName) in sheetsByPath)
        {
            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            // Pivot tables are linked to a worksheet purely through worksheet-rels relationships of the
            // pivotTable type (the schema-valid mechanism that real Excel files use). Discover them by
            // scanning those relationships; fall back to a legacy worksheet-embedded pivotTableDefinition
            // r:id reference for older FreeX saves that wrote that (non-schema) element.
            var pivotPaths = ReadWorksheetPivotTableTargets(archive, worksheetPath, packageRelNs);
            if (pivotPaths.Count == 0)
                pivotPaths = ReadLegacyEmbeddedPivotTableTargets(archive, worksheetEntry, worksheetPath, relNs, packageRelNs);

            foreach (var pivotPath in pivotPaths)
            {
                var pivotEntry = archive.GetEntry(pivotPath);
                if (pivotEntry is null)
                    continue;

                var pivotXml = XlsxPackageXmlEditor.LoadXml(pivotEntry);
                if (TryReadPivotTable(pivotXml, pivotPath, pivotCachesById, numberFormatCatalog, out var pivotTable))
                {
                    if (!result.TryGetValue(sheetName, out var sheetTables))
                    {
                        sheetTables = [];
                        result[sheetName] = sheetTables;
                    }

                    sheetTables.Add(pivotTable);
                }
            }
        }

        return result;
    }

    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";

    private static List<string> ReadWorksheetPivotTableTargets(
        ZipArchive archive,
        string worksheetPath,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (relsEntry is null)
            return [];

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var targets = new List<string>();
        foreach (var relationship in relsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            if (!string.Equals(relationship.Attribute("Type")?.Value, PivotTableRelationshipType, StringComparison.Ordinal))
                continue;

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target) ||
                string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            targets.Add(XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target));
        }

        return targets;
    }

    private static List<string> ReadLegacyEmbeddedPivotTableTargets(
        ZipArchive archive,
        ZipArchiveEntry worksheetEntry,
        string worksheetPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var pivotRelIds = ReadWorksheetRelationshipIds(
            worksheetEntry,
            "pivotTableDefinition",
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
            relNs.NamespaceName);
        if (pivotRelIds.Count == 0)
            return [];

        var worksheetRels = XlsxRelationshipReader.LoadTargets(archive, XlsxPackagePath.GetRelationshipPartPath(worksheetPath), worksheetPath, packageRelNs);
        var targets = new List<string>();
        foreach (var pivotRelId in pivotRelIds)
        {
            if (worksheetRels.TryGetValue(pivotRelId, out var pivotPath))
                targets.Add(pivotPath);
        }

        return targets;
    }

    private static Dictionary<string, string> LoadRelationshipTargets(
        ZipArchive archive,
        string relsPath,
        string sourcePart,
        XNamespace packageRelNs) =>
        XlsxRelationshipReader.LoadTargets(archive, relsPath, sourcePart, packageRelNs);

    private static List<string> ReadWorksheetRelationshipIds(
        ZipArchiveEntry worksheetEntry,
        string localName,
        string namespaceName,
        string relationshipNamespaceName)
    {
        var result = new List<string>();
        using var stream = worksheetEntry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, localName, StringComparison.Ordinal) ||
                !string.Equals(reader.NamespaceURI, namespaceName, StringComparison.Ordinal))
            {
                continue;
            }

            var relId = reader.GetAttribute("id", relationshipNamespaceName);
            if (!string.IsNullOrWhiteSpace(relId))
                result.Add(relId);
        }

        return result;
    }

    private static bool TryReadPivotTable(
        XDocument pivotXml,
        string pivotPath,
        IReadOnlyDictionary<int, PivotCacheModel> pivotCachesById,
        IReadOnlyDictionary<int, string> numberFormatCatalog,
        out PendingPivotTableModel pivotTable)
    {
        pivotTable = new PendingPivotTableModel("", 0, "", "", "", pivotPath, null, null, null, true, 1, 1, 1, false, PivotSubtotalPlacement.Bottom, true, true, true, true, false, PivotReportLayout.Tabular, 1, "PivotStyleLight16", true, true, false, false, true, true, true, false, false, false, false, false, 0, true, true, false, true, true, true, false, true, true, true, true, true, true, false, false, null, null, null, null, null, null, false, [], [], [], [], [], [], [], [], []);
        var root = pivotXml.Root;
        if (root is null)
            return false;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var name = root.Attribute("name")?.Value ?? "";
        var cacheIdAttribute = XlsxXmlAttributeReader.ReadIntAttribute(root, "cacheId");
        var location = root.Element(workbookNs + "location");
        var targetReference = location?.Attribute("ref")?.Value ?? "";
        if (string.IsNullOrWhiteSpace(name) ||
            cacheIdAttribute is null or < 0 ||
            string.IsNullOrWhiteSpace(targetReference))
        {
            return false;
        }

        var cacheId = cacheIdAttribute.Value;
        pivotCachesById.TryGetValue(cacheId, out var pivotCache);
        var pivotFieldsElement = root.Element(workbookNs + "pivotFields");
        var nativeFieldSelections = MergeMissing(
            ReadNativePivotFieldSelections(pivotFieldsElement, pivotCache, workbookNs),
            ReadFreeXPivotFieldSelections(root, workbookNs));
        var nativeFieldGroups = MergeMissing(
            ReadNativePivotFieldGroups(pivotFieldsElement, workbookNs),
            MergeMissing(
                ReadNativePivotCacheFieldGroups(pivotCache),
                ReadFreeXPivotFieldGroups(root, workbookNs)));
        var nativeFieldMetadata = ReadNativePivotFieldMetadata(pivotFieldsElement, workbookNs);
        var firstPivotFieldElement = FindFirstPivotFieldElement(
            pivotFieldsElement,
            workbookNs,
            root.Element(workbookNs + "rowFields"),
            root.Element(workbookNs + "colFields"));
        var nativeFiltersElement = root.Element(workbookNs + "filters");
        var calculatedFieldsElement = root.Element(workbookNs + "calculatedFields");
        var calculatedFields = ReadPivotCalculatedFields(calculatedFieldsElement, workbookNs, pivotCache);
        var calculatedFieldNamesByIndex = ReadPivotCalculatedFieldNamesByIndex(calculatedFieldsElement, workbookNs, pivotCache);
        // R82-io-pivot-layout-5-2: native-format entries first, then any FreeX-invented-format entries --
        // going forward, XlsxPivotTableWriter.cs only ever falls back to the invented <valueFilters>/
        // <labelFilters> shape for the handful of kinds with no real ST_PivotFilterType token at all
        // (AboveAverage/BelowAverage value filters), so this keeps a mixed native+invented list in the
        // same relative order a fresh save's own iteration order produces (native kinds first, in their
        // original order, with any non-representable kind appended after).
        var valueFilters = ReadNativePivotValueFilters(nativeFiltersElement, workbookNs)
            .Concat(ReadPivotValueFilters(root.Element(workbookNs + "valueFilters"), workbookNs))
            .ToList();
        var labelFilters = ReadNativePivotLabelFilters(nativeFiltersElement, workbookNs)
            .Concat(ReadPivotLabelFilters(root.Element(workbookNs + "labelFilters"), workbookNs))
            .ToList();
        var sorts = ReadPivotSorts(root.Element(workbookNs + "pivotSorts"), workbookNs)
            .Concat(ReadNativePivotFieldSorts(root.Element(workbookNs + "pivotFields"), workbookNs))
            .ToList();
        var styleInfo = root.Element(workbookNs + "pivotTableStyleInfo");
        pivotTable = new PendingPivotTableModel(
            name,
            cacheId,
            targetReference,
            pivotCache?.SourceReference ?? "",
            pivotCache?.SourceSheetName ?? "",
            pivotPath,
            XlsxXmlAttributeReader.ReadIntAttribute(root, "createdVersion"),
            XlsxXmlAttributeReader.ReadIntAttribute(root, "updatedVersion"),
            XlsxXmlAttributeReader.ReadIntAttribute(root, "minRefreshableVersion"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "dataOnRows", defaultValue: true),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(location!, "firstHeaderRow") ?? 1),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(location!, "firstDataRow") ?? 1),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(location!, "firstDataCol") ?? 1),
            XlsxXmlAttributeReader.ReadBoolAttribute(firstPivotFieldElement, "defaultSubtotal"),
            // R60-io-pivot-layout-6-2: CT_PivotField's subtotalTop defaults to TRUE (Top) when the
            // attribute is omitted -- defaulting to false (Bottom) here reads a schema-correct Top-placed
            // file (the overwhelmingly common case) backwards as Bottom.
            XlsxXmlAttributeReader.ReadBoolAttribute(firstPivotFieldElement, "subtotalTop", defaultValue: true)
                ? PivotSubtotalPlacement.Top
                : PivotSubtotalPlacement.Bottom,
            // OOXML CT_pivotTableDefinition spells grand-total visibility as rowGrandTotals / colGrandTotals
            // (both default true). Older FreeX saves used the non-schema showRowGrandTotals /
            // showColumnGrandTotals / showGrandTotals names, so fall back to those for backward compatibility.
            ReadGrandTotal(root, "rowGrandTotals", "showRowGrandTotals") || ReadGrandTotal(root, "colGrandTotals", "showColumnGrandTotals"),
            ReadGrandTotal(root, "rowGrandTotals", "showRowGrandTotals"),
            ReadGrandTotal(root, "colGrandTotals", "showColumnGrandTotals"),
            // R60-io-pivot-layout-6-1: the real OOXML wire format is the per-field x14 fillDownLabels
            // extension (read off whichever pivotField is actually on the row/col axis, same field
            // FindFirstPivotFieldElement resolves for subtotalTop below); prefer that, then fall back to
            // the legacy FreeX fx:tableProps repeatItemLabels attribute, then the legacy definition-level
            // attribute, for back-compat with older FreeX-authored files. insertBlankRow is a per-pivotField
            // OOXML flag, so read it from any field (legacy fallback to blankLineAfterItems).
            ReadX14FillDownLabels(firstPivotFieldElement, workbookNs)
                ?? ReadFreeXTableBool(root, workbookNs, "repeatItemLabels")
                ?? XlsxXmlAttributeReader.ReadBoolAttribute(root, "repeatItemLabels", defaultValue: true),
            ReadAnyPivotFieldBool(root, workbookNs, "insertBlankRow")
                ?? XlsxXmlAttributeReader.ReadBoolAttribute(root, "blankLineAfterItems"),
            ReadPivotReportLayout(root),
            Math.Clamp(XlsxXmlAttributeReader.ReadIntAttribute(root, "indent") ?? 1, 0, 15),
            styleInfo?.Attribute("name")?.Value ?? "PivotStyleLight16",
            XlsxXmlAttributeReader.ReadBoolAttribute(styleInfo, "showRowHeaders", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(styleInfo, "showColHeaders", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(styleInfo, "showRowStripes"),
            XlsxXmlAttributeReader.ReadBoolAttribute(styleInfo, "showColStripes"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showHeaders", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showDataTips", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showMemberPropertyTips", defaultValue: true),
            // Real Excel keys the 'Classic PivotTable Layout (enables dragging of fields in the grid)'
            // checkbox off gridDropZones (default false), NOT showDropZones (an unrelated, unmodeled flag
            // that defaults to true).
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "gridDropZones"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "mergeItem"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showEmptyRow"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showEmptyCol"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "pageOverThenDown"),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(root, "pageWrap") ?? 0),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showDrill", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableDrill", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "asteriskTotals"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "multipleFieldFilters", defaultValue: true),
            // OOXML expresses these as disableFieldList (inverse) and editData; prefer those, then fall back
            // to the legacy enableFieldDialog / enableDataValueEditing names FreeX previously wrote.
            root.Attribute("disableFieldList") is not null
                ? !XlsxXmlAttributeReader.ReadBoolAttribute(root, "disableFieldList")
                : XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableFieldDialog", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableFieldProperties", defaultValue: true),
            root.Attribute("editData") is not null
                ? XlsxXmlAttributeReader.ReadBoolAttribute(root, "editData")
                : XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableDataValueEditing"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyNumberFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyBorderFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyFontFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyPatternFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyWidthHeightFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "preserveFormatting", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "itemPrintTitles") || XlsxXmlAttributeReader.ReadBoolAttribute(root, "fieldPrintTitles"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "printDrill"),
            ReadFreeXTableText(root, workbookNs, "altTextTitle") ?? root.Attribute("altText")?.Value,
            ReadFreeXTableText(root, workbookNs, "altTextDescription") ?? root.Attribute("altTextSummary")?.Value,
            root.Attribute("dataCaption")?.Value,
            root.Attribute("grandTotalCaption")?.Value,
            root.Attribute("missingCaption")?.Value,
            root.Attribute("errorCaption")?.Value,
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "fieldListSortAscending"),
            ReadPivotFieldIndexes(root.Element(workbookNs + "rowFields"), workbookNs, nativeFieldSelections, nativeFieldGroups, nativeFieldMetadata),
            ReadPivotFieldIndexes(root.Element(workbookNs + "colFields"), workbookNs, nativeFieldSelections, nativeFieldGroups, nativeFieldMetadata),
            ReadPivotPageFields(root.Element(workbookNs + "pageFields"), pivotCache, workbookNs, nativeFieldSelections, nativeFieldGroups, nativeFieldMetadata),
            ReadPivotDataFields(root.Element(workbookNs + "dataFields"), workbookNs, calculatedFields, calculatedFieldNamesByIndex, numberFormatCatalog),
            calculatedFields,
            // R116-io-pivot-calcitem-part: calculatedItems now lives on the shared pivotCacheDefinition
            // part (XlsxPivotCacheReader populates pivotCache.CalculatedItems from it), not this
            // pivotTableDefinition part. Fall back to reading this root's own <calculatedItems> only for
            // a file this codebase saved before that fix, so a FreeX round trip of an already-saved file
            // does not silently lose the item the moment the reader stops looking in the old spot.
            pivotCache is { CalculatedItems.Count: > 0 }
                ? [.. pivotCache.CalculatedItems]
                : ReadPivotCalculatedItems(root.Element(workbookNs + "calculatedItems"), workbookNs),
            valueFilters,
            labelFilters,
            sorts);
        return true;
    }

    // R52-io-pivot-layout-3-2: PivotTableModel.ShowSubtotals/SubtotalPlacement collapse the per-pivotField
    // defaultSubtotal/subtotalTop settings into one table-wide flag. The pivotField carrying the setting
    // that actually matters is whichever field is really placed on the row or column axis (rowFields is
    // listed before colFields in CT_pivotTableDefinition, so it takes precedence) -- NOT simply the first
    // <pivotField> in cache/document order, which may be an unrelated page/filter field (subtotals don't
    // apply to filter fields, so its defaultSubtotal/subtotalTop attributes are irrelevant/default).
    private static XElement? FindFirstPivotFieldElement(
        XElement? pivotFieldsElement,
        XNamespace workbookNs,
        XElement? rowFieldsElement,
        XElement? colFieldsElement)
    {
        if (pivotFieldsElement is null)
            return null;

        var pivotFields = pivotFieldsElement.Elements(workbookNs + "pivotField").ToList();
        var axisFieldIndex = FindFirstAxisFieldIndex(rowFieldsElement, workbookNs)
            ?? FindFirstAxisFieldIndex(colFieldsElement, workbookNs);
        if (axisFieldIndex is { } index && index >= 0 && index < pivotFields.Count)
            return pivotFields[index];

        return pivotFields.Count > 0 ? pivotFields[0] : null;
    }

    // Returns the SourceFieldIndex of the first real field listed in a rowFields/colFields element,
    // skipping the x="-2" "Σ Values" pseudo-field placeholder (R52-io-pivot-layout-3-1), which has no
    // corresponding pivotField/cache field to look subtotal settings up on.
    private static int? FindFirstAxisFieldIndex(XElement? fieldsElement, XNamespace workbookNs)
    {
        if (fieldsElement is null)
            return null;

        foreach (var field in fieldsElement.Elements(workbookNs + "field"))
        {
            var index = XlsxXmlAttributeReader.ReadIntAttribute(field, "x");
            if (index is { } value && value != -2)
                return value;
        }

        return null;
    }

    // R60-io-pivot-layout-6-1: reads the real x14 fillDownLabels extension off a pivotField -- the wire
    // format real Excel uses for "Repeat All Item Labels" (ext uri "{2946ED86-A175-432A-8AC1-64E0C546D7DE}"
    // per [MS-XLSX], DocumentFormat.OpenXml.Office2010.Excel.PivotField.FillDownLabels). Returns null when
    // the field carries no such extension so callers can fall back to FreeX's legacy fx extension/attribute.
    private static bool? ReadX14FillDownLabels(XElement? pivotFieldElement, XNamespace workbookNs)
    {
        if (pivotFieldElement is null)
            return null;

        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        var x14PivotField = pivotFieldElement
            .Element(workbookNs + "extLst")?
            .Elements(workbookNs + "ext")
            .FirstOrDefault(ext => string.Equals(
                ext.Attribute("uri")?.Value,
                "{2946ED86-A175-432A-8AC1-64E0C546D7DE}",
                StringComparison.OrdinalIgnoreCase))?
            .Element(x14Ns + "pivotField");
        if (x14PivotField?.Attribute("fillDownLabels") is null)
            return null;

        return XlsxXmlAttributeReader.ReadBoolAttribute(x14PivotField, "fillDownLabels");
    }

    // Reads a grand-total flag, preferring the OOXML attribute name (rowGrandTotals / colGrandTotals) and
    // falling back to the legacy non-schema FreeX names (showRowGrandTotals / showColumnGrandTotals /
    // showGrandTotals). All grand-total attributes default to true (visible) per the OOXML schema.
    private static bool ReadGrandTotal(XElement root, string ooxmlName, string legacyName)
    {
        if (root.Attribute(ooxmlName) is not null)
            return XlsxXmlAttributeReader.ReadBoolAttribute(root, ooxmlName, defaultValue: true);
        if (root.Attribute(legacyName) is not null)
            return XlsxXmlAttributeReader.ReadBoolAttribute(root, legacyName, defaultValue: true);

        return XlsxXmlAttributeReader.ReadBoolAttribute(root, "showGrandTotals", defaultValue: true);
    }

}
