using System.IO.Compression;
using System.Xml.Linq;
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

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var pivotRelIds = worksheetXml.Root?
                .Elements(workbookNs + "pivotTableDefinition")
                .Select(e => e.Attribute(relNs + "id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList() ?? [];
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

    private static bool TryReadPivotTable(
        XDocument pivotXml,
        string pivotPath,
        IReadOnlyDictionary<int, PivotCacheModel> pivotCachesById,
        IReadOnlyDictionary<int, string> numberFormatCatalog,
        out PendingPivotTableModel pivotTable)
    {
        pivotTable = new PendingPivotTableModel("", 0, "", "", pivotPath, false, PivotSubtotalPlacement.Bottom, true, true, true, true, false, PivotReportLayout.Tabular, 1, "PivotStyleLight16", true, true, false, false, true, true, true, false, false, false, 0, true, true, true, false, false, null, null, [], [], [], [], [], [], [], [], []);
        var root = pivotXml.Root;
        if (root is null)
            return false;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var name = root.Attribute("name")?.Value ?? "";
        var cacheId = XlsxXmlAttributeReader.ReadIntAttribute(root, "cacheId") ?? 0;
        var targetReference = root.Element(workbookNs + "location")?.Attribute("ref")?.Value ?? "";
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
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "pageOverThenDown"),
            Math.Max(0, XlsxXmlAttributeReader.ReadIntAttribute(root, "pageWrap") ?? 0),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "showDrill", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "applyWidthHeightFormats", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "preserveFormatting", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "itemPrintTitles") || XlsxXmlAttributeReader.ReadBoolAttribute(root, "fieldPrintTitles"),
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "printDrill"),
            root.Attribute("altText")?.Value,
            root.Attribute("altTextSummary")?.Value,
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

    private static PivotFieldGrouping ReadPivotFieldGrouping(string? value, PivotFieldGrouping defaultValue = PivotFieldGrouping.None) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "years" or "year" => PivotFieldGrouping.Year,
            "quarters" or "quarter" => PivotFieldGrouping.Quarter,
            "months" or "month" => PivotFieldGrouping.Month,
            "days" or "day" => PivotFieldGrouping.Day,
            "range" or "numberrange" or "number-range" or "number" => PivotFieldGrouping.NumberRange,
            _ => defaultValue
        };

    private static PivotReportLayout ReadPivotReportLayout(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "compact" or "compactform" or "compact-form" => PivotReportLayout.Compact,
            "outline" or "outlineform" or "outline-form" => PivotReportLayout.Outline,
            _ => PivotReportLayout.Tabular
        };

    private static PivotShowValuesAs ReadPivotShowValuesAs(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "percentofgrandtotal" or "percent-grand-total" => PivotShowValuesAs.PercentOfGrandTotal,
            "percentofrowtotal" or "percent-row-total" => PivotShowValuesAs.PercentOfRowTotal,
            "percentofcolumntotal" or "percentofcoltotal" or "percent-column-total" or "percent-col-total" => PivotShowValuesAs.PercentOfColumnTotal,
            "runningtotalin" or "running-total-in" => PivotShowValuesAs.RunningTotalIn,
            "differencefrom" or "difference-from" => PivotShowValuesAs.DifferenceFrom,
            "percentdifferencefrom" or "percent-difference-from" => PivotShowValuesAs.PercentDifferenceFrom,
            "ranksmallest" or "rank-smallest" => PivotShowValuesAs.RankSmallest,
            "ranklargest" or "rank-largest" => PivotShowValuesAs.RankLargest,
            "index" => PivotShowValuesAs.Index,
            "percentofparentrowtotal" or "percent-parent-row-total" => PivotShowValuesAs.PercentOfParentRowTotal,
            "percentofparentcolumntotal" or "percentofparentcoltotal" or "percent-parent-column-total" or "percent-parent-col-total" => PivotShowValuesAs.PercentOfParentColumnTotal,
            "percentofparenttotal" or "percent-parent-total" => PivotShowValuesAs.PercentOfParentTotal,
            _ => PivotShowValuesAs.None
        };

    private static PivotValueFilterKind ReadPivotValueFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "bottom" => PivotValueFilterKind.Bottom,
            "greaterthan" or "greater_than" => PivotValueFilterKind.GreaterThan,
            "greaterthanorequal" or "greater_than_or_equal" => PivotValueFilterKind.GreaterThanOrEqual,
            "lessthan" or "less_than" => PivotValueFilterKind.LessThan,
            "lessthanorequal" or "less_than_or_equal" => PivotValueFilterKind.LessThanOrEqual,
            "equals" or "equal" => PivotValueFilterKind.Equals,
            "doesnotequal" or "not_equal" => PivotValueFilterKind.DoesNotEqual,
            "between" => PivotValueFilterKind.Between,
            "notbetween" or "not_between" => PivotValueFilterKind.NotBetween,
            "aboveaverage" or "above_average" => PivotValueFilterKind.AboveAverage,
            "belowaverage" or "below_average" => PivotValueFilterKind.BelowAverage,
            _ => PivotValueFilterKind.Top
        };

    private static PivotLabelFilterKind ReadPivotLabelFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "doesnotequal" or "not_equal" => PivotLabelFilterKind.DoesNotEqual,
            "beginswith" or "begins_with" => PivotLabelFilterKind.BeginsWith,
            "endswith" or "ends_with" => PivotLabelFilterKind.EndsWith,
            "contains" => PivotLabelFilterKind.Contains,
            "doesnotcontain" or "does_not_contain" => PivotLabelFilterKind.DoesNotContain,
            "greaterthan" or "greater_than" => PivotLabelFilterKind.GreaterThan,
            "greaterthanorequal" or "greater_than_or_equal" => PivotLabelFilterKind.GreaterThanOrEqual,
            "lessthan" or "less_than" => PivotLabelFilterKind.LessThan,
            "lessthanorequal" or "less_than_or_equal" => PivotLabelFilterKind.LessThanOrEqual,
            "between" => PivotLabelFilterKind.Between,
            _ => PivotLabelFilterKind.Equals
        };

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

    private static PivotTableModel ToPivotTableModel(PendingPivotTableModel pending, SheetId sheetId)
    {
        var pivotTable = new PivotTableModel
        {
            Name = pending.Name,
            CacheId = pending.CacheId,
            SourceRange = ParseOptionalRange(pending.SourceReference, sheetId),
            TargetRange = GridRange.Parse(pending.TargetReference, sheetId),
            PackagePart = pending.PackagePart,
            ShowSubtotals = pending.ShowSubtotals,
            SubtotalPlacement = pending.SubtotalPlacement,
            ShowRowGrandTotals = pending.ShowRowGrandTotals,
            ShowColumnGrandTotals = pending.ShowColumnGrandTotals,
            RepeatItemLabels = pending.RepeatItemLabels,
            BlankLineAfterItems = pending.BlankLineAfterItems,
            ReportLayout = pending.ReportLayout,
            CompactRowLabelIndent = pending.CompactRowLabelIndent,
            StyleName = string.IsNullOrWhiteSpace(pending.StyleName) ? "PivotStyleLight16" : pending.StyleName,
            ShowRowHeaders = pending.ShowRowHeaders,
            ShowColumnHeaders = pending.ShowColumnHeaders,
            ShowRowStripes = pending.ShowRowStripes,
            ShowColumnStripes = pending.ShowColumnStripes,
            ShowFieldHeaders = pending.ShowFieldHeaders,
            ShowContextualTooltips = pending.ShowContextualTooltips,
            ShowPropertiesInTooltips = pending.ShowPropertiesInTooltips,
            ShowClassicLayout = pending.ShowClassicLayout,
            MergeAndCenterLabels = pending.MergeAndCenterLabels,
            PageOverThenDown = pending.PageOverThenDown,
            PageWrap = pending.PageWrap,
            ShowExpandCollapseButtons = pending.ShowExpandCollapseButtons,
            AutofitColumnsOnUpdate = pending.AutofitColumnsOnUpdate,
            PreserveFormattingOnUpdate = pending.PreserveFormattingOnUpdate,
            PrintTitles = pending.PrintTitles,
            PrintExpandCollapseButtons = pending.PrintExpandCollapseButtons,
            AltTextTitle = string.IsNullOrWhiteSpace(pending.AltTextTitle) ? null : pending.AltTextTitle,
            AltTextDescription = string.IsNullOrWhiteSpace(pending.AltTextDescription) ? null : pending.AltTextDescription
        };

        pivotTable.RowFields.AddRange(pending.RowFields);
        pivotTable.ColumnFields.AddRange(pending.ColumnFields);
        pivotTable.PageFields.AddRange(pending.PageFields);
        pivotTable.DataFields.AddRange(pending.DataFields);
        pivotTable.CalculatedFields.AddRange(pending.CalculatedFields);
        pivotTable.CalculatedItems.AddRange(pending.CalculatedItems);
        pivotTable.ValueFilters.AddRange(pending.ValueFilters);
        pivotTable.LabelFilters.AddRange(pending.LabelFilters);
        pivotTable.Sorts.AddRange(pending.Sorts);
        return pivotTable;
    }

    private static GridRange ParseOptionalRange(string reference, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return default;

        try
        {
            return GridRange.Parse(reference, sheetId);
        }
        catch
        {
            return default;
        }
    }


    public sealed record PivotPackageMetadata(
        IReadOnlyList<PivotCacheModel> PivotCaches,
        IReadOnlyDictionary<string, List<PendingPivotTableModel>> PivotTablesBySheetName)
    {
        public static PivotPackageMetadata Empty { get; } = new(
            [],
            new Dictionary<string, List<PendingPivotTableModel>>(StringComparer.OrdinalIgnoreCase));
    }

    public sealed record PendingPivotTableModel(
        string Name,
        int CacheId,
        string TargetReference,
        string SourceReference,
        string PackagePart,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool ShowGrandTotals,
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        PivotReportLayout ReportLayout,
        int CompactRowLabelIndent,
        string StyleName,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        bool ShowFieldHeaders,
        bool ShowContextualTooltips,
        bool ShowPropertiesInTooltips,
        bool ShowClassicLayout,
        bool MergeAndCenterLabels,
        bool PageOverThenDown,
        int PageWrap,
        bool ShowExpandCollapseButtons,
        bool AutofitColumnsOnUpdate,
        bool PreserveFormattingOnUpdate,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        string? AltTextTitle,
        string? AltTextDescription,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields,
        IReadOnlyList<PivotCalculatedFieldModel> CalculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> CalculatedItems,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotSortModel> Sorts)
    {
        public PivotTableModel ToPivotTableModel(SheetId sheetId) =>
            XlsxPivotTableReader.ToPivotTableModel(this, sheetId);
    }
}
