using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxPivotTableReader
{
    public static PivotPackageMetadata Load(
        Stream xlsxStream,
        IReadOnlyDictionary<int, string> numberFormatCatalog)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
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

            var pivotRelIds = ReadWorksheetRelationshipIds(
                worksheetEntry,
                "pivotTableDefinition",
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
                relNs.NamespaceName);
            if (pivotRelIds.Count == 0)
                continue;

            var worksheetRels = XlsxRelationshipReader.LoadTargets(archive, XlsxPackagePath.GetRelationshipPartPath(worksheetPath), worksheetPath, packageRelNs);
            foreach (var pivotRelId in pivotRelIds)
            {
                if (!worksheetRels.TryGetValue(pivotRelId, out var pivotPath))
                    continue;

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
        pivotTable = new PendingPivotTableModel("", 0, "", "", pivotPath, null, null, null, true, 1, 1, 1, false, PivotSubtotalPlacement.Bottom, true, true, true, true, false, PivotReportLayout.Tabular, 1, "PivotStyleLight16", true, true, false, false, true, true, true, false, false, false, false, false, 0, true, true, false, true, true, true, false, true, true, true, true, true, true, false, false, null, null, null, null, null, null, [], [], [], [], [], [], [], [], []);
        var root = pivotXml.Root;
        if (root is null)
            return false;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var name = root.Attribute("name")?.Value ?? "";
        var cacheId = XlsxXmlAttributeReader.ReadIntAttribute(root, "cacheId") ?? 0;
        var location = root.Element(workbookNs + "location");
        var targetReference = location?.Attribute("ref")?.Value ?? "";
        if (string.IsNullOrWhiteSpace(name) || cacheId <= 0 || string.IsNullOrWhiteSpace(targetReference))
            return false;

        pivotCachesById.TryGetValue(cacheId, out var pivotCache);
        var pivotFieldsElement = root.Element(workbookNs + "pivotFields");
        var nativeFieldSelections = ReadNativePivotFieldSelections(pivotFieldsElement, pivotCache, workbookNs);
        var nativeFieldGroups = ReadNativePivotFieldGroups(pivotFieldsElement, workbookNs);
        var nativeFiltersElement = root.Element(workbookNs + "filters");
        var calculatedFields = ReadPivotCalculatedFields(root.Element(workbookNs + "calculatedFields"), workbookNs);
        var valueFilters = ReadPivotValueFilters(root.Element(workbookNs + "valueFilters"), workbookNs)
            .Concat(ReadNativePivotValueFilters(nativeFiltersElement, workbookNs))
            .ToList();
        var labelFilters = ReadPivotLabelFilters(root.Element(workbookNs + "labelFilters"), workbookNs)
            .Concat(ReadNativePivotLabelFilters(nativeFiltersElement, workbookNs))
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
            pivotPath,
            XlsxXmlAttributeReader.ReadIntAttribute(root, "createdVersion"),
            XlsxXmlAttributeReader.ReadIntAttribute(root, "updatedVersion"),
            XlsxXmlAttributeReader.ReadIntAttribute(root, "minRefreshableVersion"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "dataOnRows", defaultValue: true),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(location!, "firstHeaderRow") ?? 1),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(location!, "firstDataRow") ?? 1),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(location!, "firstDataCol") ?? 1),
            XlsxXmlAttributeReader.ReadBoolAttribute(root.Element(workbookNs + "pivotFields")?.Elements(workbookNs + "pivotField").FirstOrDefault(), "defaultSubtotal"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root.Element(workbookNs + "pivotFields")?.Elements(workbookNs + "pivotField").FirstOrDefault(), "subtotalTop")
                ? PivotSubtotalPlacement.Top
                : PivotSubtotalPlacement.Bottom,
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showGrandTotals", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showRowGrandTotals", XlsxXmlAttributeReader.ReadBoolAttribute(root, "showGrandTotals", defaultValue: true)),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showColumnGrandTotals", XlsxXmlAttributeReader.ReadBoolAttribute(root, "showGrandTotals", defaultValue: true)),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "repeatItemLabels", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "blankLineAfterItems"),
            ReadPivotReportLayout(root.Attribute("reportLayout")?.Value),
            Math.Clamp(XlsxXmlAttributeReader.ReadIntAttribute(root, "indent") ?? 1, 0, 15),
            styleInfo?.Attribute("name")?.Value ?? "PivotStyleLight16",
            XlsxXmlAttributeReader.ReadBoolAttribute(styleInfo, "showRowHeaders", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(styleInfo, "showColHeaders", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(styleInfo, "showRowStripes"),
            XlsxXmlAttributeReader.ReadBoolAttribute(styleInfo, "showColStripes"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showHeaders", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showDataTips", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showMemberPropertyTips", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showDropZones"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "mergeItem"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showEmptyRow"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showEmptyCol"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "pageOverThenDown"),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(root, "pageWrap") ?? 0),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showDrill", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableDrill", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "asteriskTotals"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "multipleFieldFilters", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableFieldDialog", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableFieldProperties", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "enableDataValueEditing"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyNumberFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyBorderFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyFontFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyPatternFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyWidthHeightFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "preserveFormatting", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "itemPrintTitles") || XlsxXmlAttributeReader.ReadBoolAttribute(root, "fieldPrintTitles"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "printDrill"),
            root.Attribute("altText")?.Value,
            root.Attribute("altTextSummary")?.Value,
            root.Attribute("dataCaption")?.Value,
            root.Attribute("grandTotalCaption")?.Value,
            root.Attribute("missingCaption")?.Value,
            root.Attribute("errorCaption")?.Value,
            ReadPivotFieldIndexes(root.Element(workbookNs + "rowFields"), workbookNs, nativeFieldSelections, nativeFieldGroups),
            ReadPivotFieldIndexes(root.Element(workbookNs + "colFields"), workbookNs, nativeFieldSelections, nativeFieldGroups),
            ReadPivotPageFields(root.Element(workbookNs + "pageFields"), workbookNs, nativeFieldSelections, nativeFieldGroups),
            ReadPivotDataFields(root.Element(workbookNs + "dataFields"), workbookNs, calculatedFields, numberFormatCatalog),
            calculatedFields,
            ReadPivotCalculatedItems(root.Element(workbookNs + "calculatedItems"), workbookNs),
            valueFilters,
            labelFilters,
            sorts);
        return true;
    }

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
            var sharedItems = pivotCache.Fields[fieldIndex].SharedItems;
            if (sharedItems is null || sharedItems.Count == 0)
                continue;

            var hiddenIndexes = pivotFields[fieldIndex]
                .Element(workbookNs + "items")?
                .Elements(workbookNs + "item")
                .Where(item => XlsxXmlAttributeReader.ReadBoolAttribute(item, "hidden"))
                .Select(item => XlsxXmlAttributeReader.ReadIntAttribute(item, "x"))
                .Where(index => index.HasValue && index.Value >= 0 && index.Value < sharedItems.Count)
                .Select(index => index!.Value)
                .ToHashSet() ?? [];
            if (hiddenIndexes.Count == 0)
                continue;

            result[fieldIndex] = sharedItems
                .Where((_, itemIndex) => !hiddenIndexes.Contains(itemIndex))
                .ToList();
        }

        return result;
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

    private static List<PivotFieldModel> ReadPivotFieldIndexes(
        XElement? fieldsElement,
        XNamespace workbookNs,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? nativeFieldSelections = null,
        IReadOnlyDictionary<int, PivotFieldModel>? nativeFieldGroups = null)
    {
        if (fieldsElement is null)
            return [];

        return fieldsElement
            .Elements(workbookNs + "field")
            .Select(field =>
            {
                var index = XlsxXmlAttributeReader.ReadIntAttribute(field, "x");
                return index.HasValue
                    ? new PivotFieldModel(
                        index.Value,
                        field.Attribute("name")?.Value,
                        ReadCsvAttribute(field.Attribute("selectedItems")?.Value) ?? ReadNativePivotFieldSelection(nativeFieldSelections, index.Value),
                        Grouping: ReadPivotFieldGrouping(field.Attribute("groupBy")?.Value, ReadNativePivotFieldGroup(nativeFieldGroups, index.Value)?.Grouping ?? PivotFieldGrouping.None),
                        GroupStart: XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupStart") ?? ReadNativePivotFieldGroup(nativeFieldGroups, index.Value)?.GroupStart,
                        GroupEnd: XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupEnd") ?? ReadNativePivotFieldGroup(nativeFieldGroups, index.Value)?.GroupEnd,
                        GroupInterval: XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupInterval") ?? ReadNativePivotFieldGroup(nativeFieldGroups, index.Value)?.GroupInterval)
                    : null;
            })
            .Where(field => field is not null)
            .Select(field => field!)
            .ToList();
    }

    private static List<PivotFieldModel> ReadPivotPageFields(
        XElement? fieldsElement,
        XNamespace workbookNs,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? nativeFieldSelections = null,
        IReadOnlyDictionary<int, PivotFieldModel>? nativeFieldGroups = null)
    {
        if (fieldsElement is null)
            return [];

        var pageFields = fieldsElement
            .Elements(workbookNs + "pageField")
            .Select(field => new PivotFieldModel(
                XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1,
                field.Attribute("name")?.Value,
                ReadCsvAttribute(field.Attribute("selectedItems")?.Value) ?? ReadNativePivotFieldSelection(nativeFieldSelections, XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1),
                ReadPivotFieldGrouping(field.Attribute("groupBy")?.Value, ReadNativePivotFieldGroup(nativeFieldGroups, XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1)?.Grouping ?? PivotFieldGrouping.None),
                XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupStart") ?? ReadNativePivotFieldGroup(nativeFieldGroups, XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1)?.GroupStart,
                XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupEnd") ?? ReadNativePivotFieldGroup(nativeFieldGroups, XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1)?.GroupEnd,
                XlsxXmlAttributeReader.ReadDoubleAttribute(field, "groupInterval") ?? ReadNativePivotFieldGroup(nativeFieldGroups, XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1)?.GroupInterval))
            .Where(field => field.SourceFieldIndex >= 0)
            .ToList();
        if (pageFields.Count > 0)
            return pageFields;

        return ReadPivotFieldIndexes(fieldsElement, workbookNs, nativeFieldSelections, nativeFieldGroups);
    }

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

    private static List<PivotDataFieldModel> ReadPivotDataFields(
        XElement? dataFieldsElement,
        XNamespace workbookNs,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyDictionary<int, string> numberFormatCatalog)
    {
        if (dataFieldsElement is null)
            return [];

        return dataFieldsElement
            .Elements(workbookNs + "dataField")
            .Select(field =>
            {
                var fieldIndex = XlsxXmlAttributeReader.ReadIntAttribute(field, "fld") ?? -1;
                var numberFormatId = XlsxXmlAttributeReader.ReadIntAttribute(field, "numFmtId");
                var calculatedFieldName = field.Attribute("calculatedField")?.Value ??
                    calculatedFields.FirstOrDefault(calculated => string.Equals(calculated.Name, field.Attribute("name")?.Value, StringComparison.OrdinalIgnoreCase))?.Name;
                return new PivotDataFieldModel(
                    calculatedFieldName is null ? fieldIndex : -1,
                    field.Attribute("name")?.Value ?? "",
                    field.Attribute("subtotal")?.Value ?? "sum",
                    numberFormatId,
                    calculatedFieldName,
                    ReadPivotShowValuesAs(field.Attribute("showValuesAs")?.Value),
                    XlsxXmlAttributeReader.ReadIntAttribute(field, "baseField"),
                    field.Attribute("baseItem")?.Value,
                    numberFormatId is not null && numberFormatCatalog.TryGetValue(numberFormatId.Value, out var formatCode)
                        ? formatCode
                        : null);
            })
            .Where(field => field.SourceFieldIndex >= 0 || field.CalculatedFieldName is not null)
            .ToList();
    }

    private static IReadOnlyList<string>? ReadCsvAttribute(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static List<PivotCalculatedFieldModel> ReadPivotCalculatedFields(XElement? calculatedFieldsElement, XNamespace workbookNs)
    {
        if (calculatedFieldsElement is null)
            return [];

        return calculatedFieldsElement
            .Elements(workbookNs + "calculatedField")
            .Select(field => new PivotCalculatedFieldModel(
                field.Attribute("name")?.Value ?? "",
                field.Attribute("formula")?.Value ?? ""))
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToList();
    }

    private static List<PivotCalculatedItemModel> ReadPivotCalculatedItems(XElement? calculatedItemsElement, XNamespace workbookNs)
    {
        if (calculatedItemsElement is null)
            return [];

        return calculatedItemsElement
            .Elements(workbookNs + "calculatedItem")
            .Select(item => new PivotCalculatedItemModel(
                XlsxXmlAttributeReader.ReadIntAttribute(item, "field") ?? -1,
                item.Attribute("name")?.Value ?? "",
                item.Attribute("formula")?.Value ?? ""))
            .Where(item => item.SourceFieldIndex >= 0 && !string.IsNullOrWhiteSpace(item.Name))
            .ToList();
    }

}
