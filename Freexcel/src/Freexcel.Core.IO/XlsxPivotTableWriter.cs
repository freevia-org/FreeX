using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxPivotTableWriter
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
        var pivotCacheElements = new List<XElement>();
        var cacheIndex = 1;
        foreach (var cache in workbook.PivotCaches.OrderBy(cache => cache.CacheId))
        {
            if (cache.CacheId <= 0)
                continue;

            var cachePath = $"xl/pivotCache/pivotCacheDefinition{cacheIndex++}.xml";
            XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, ToPivotCacheDefinitionXml(cache, workbookNs, relNs));
            XlsxPackageXmlEditor.ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(cachePath), ToPivotCacheDefinitionRelsXml(packageRelNs));
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{cachePath}", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml");

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
            var sheetsElement = workbookRoot.Element(workbookNs + "sheets");
            var pivotCachesElement = new XElement(
                workbookNs + "pivotCaches",
                new XAttribute("count", pivotCacheElements.Count.ToString(CultureInfo.InvariantCulture)),
                pivotCacheElements);
            if (sheetsElement is not null)
                sheetsElement.AddBeforeSelf(pivotCachesElement);
            else
                workbookRoot.Add(pivotCachesElement);
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

            WriteWorksheetPivotTables(archive, worksheetPath, sheet, cachePartById, numberFormatIdMap, ref pivotIndex, workbookNs, relNs, packageRelNs);
        }
    }

    private static void WriteWorksheetPivotTables(
        ZipArchive archive,
        string worksheetPath,
        Sheet sheet,
        IReadOnlyDictionary<int, string> cachePartById,
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

        var references = new List<XElement>();
        foreach (var pivot in sheet.PivotTables)
        {
            if (!cachePartById.TryGetValue(pivot.CacheId, out var cachePath))
                continue;

            var pivotPath = $"xl/pivotTables/pivotTable{pivotIndex++}.xml";
            var cacheRelId = "rIdPivotCache";
            XlsxPackageXmlEditor.ReplaceXml(archive, pivotPath, ToPivotTableDefinitionXml(pivot, workbookNs, cacheRelId, numberFormatIdMap));
            XlsxPackageXmlEditor.ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(pivotPath), new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", cacheRelId),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition"),
                        new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(pivotPath, cachePath))))));
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{pivotPath}", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml");

            var pivotRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                worksheetRelsXml,
                packageRelNs,
                worksheetPath,
                pivotPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable");
            references.Add(new XElement(workbookNs + "pivotTableDefinition", new XAttribute(relNs + "id", pivotRelId)));
        }

        if (references.Count == 0)
            return;

        worksheetXml.Root?.Elements(workbookNs + "pivotTableDefinition").Remove();
        worksheetXml.Root?.Add(references);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static XDocument ToPivotCacheDefinitionXml(PivotCacheModel cache, XNamespace workbookNs, XNamespace relNs)
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
            cache.IsOlap ? new XAttribute("olap", "1") : null,
            new XAttribute("refreshOnLoad", cache.RefreshOnLoad ? "1" : "0"),
            new XAttribute("saveData", cache.SaveData ? "1" : "0"),
            new XAttribute("enableRefresh", cache.EnableRefresh ? "1" : "0"),
            cache.RefreshedVersion is { } refreshedVersion ? new XAttribute("refreshedVersion", refreshedVersion.ToString(CultureInfo.InvariantCulture)) : null,
            !string.IsNullOrWhiteSpace(cache.RefreshedBy) ? new XAttribute("refreshedBy", cache.RefreshedBy) : null,
            new XAttribute("recordCount", "0"),
            cacheSource,
            new XElement(
                workbookNs + "cacheFields",
                new XAttribute("count", cache.Fields.Count.ToString(CultureInfo.InvariantCulture)),
                cache.Fields.Select(field => new XElement(
                    workbookNs + "cacheField",
                    new XAttribute("name", string.IsNullOrWhiteSpace(field.Name) ? "Field" : field.Name),
                    field.NumberFormatId is { } numFmtId ? new XAttribute("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture)) : null,
                    ToPivotCacheSharedItemsXml(field, workbookNs))))));
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

    private static XDocument ToPivotCacheDefinitionRelsXml(XNamespace packageRelNs) =>
        new(new XElement(packageRelNs + "Relationships"));

    private static XDocument ToPivotTableDefinitionXml(
        PivotTableModel pivot,
        XNamespace workbookNs,
        string cacheRelId,
        IReadOnlyDictionary<int, int> numberFormatIdMap) =>
        new(new XElement(
            workbookNs + "pivotTableDefinition",
            new XAttribute("name", string.IsNullOrWhiteSpace(pivot.Name) ? "PivotTable" : pivot.Name),
            new XAttribute("cacheId", pivot.CacheId.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("dataOnRows", "1"),
            new XAttribute("applyNumberFormats", "1"),
            new XAttribute("applyBorderFormats", "1"),
            new XAttribute("applyFontFormats", "1"),
            new XAttribute("applyPatternFormats", "1"),
            new XAttribute("applyWidthHeightFormats", pivot.AutofitColumnsOnUpdate ? "1" : "0"),
            new XAttribute("preserveFormatting", pivot.PreserveFormattingOnUpdate ? "1" : "0"),
            new XAttribute("updatedVersion", "8"),
            new XAttribute("minRefreshableVersion", "3"),
            new XAttribute("showGrandTotals", pivot.ShowGrandTotals ? "1" : "0"),
            new XAttribute("showRowGrandTotals", pivot.ShowRowGrandTotals ? "1" : "0"),
            new XAttribute("showColumnGrandTotals", pivot.ShowColumnGrandTotals ? "1" : "0"),
            new XAttribute("repeatItemLabels", pivot.RepeatItemLabels ? "1" : "0"),
            new XAttribute("blankLineAfterItems", pivot.BlankLineAfterItems ? "1" : "0"),
            new XAttribute("showDrill", pivot.ShowExpandCollapseButtons ? "1" : "0"),
            new XAttribute("itemPrintTitles", pivot.PrintTitles ? "1" : "0"),
            new XAttribute("fieldPrintTitles", pivot.PrintTitles ? "1" : "0"),
            new XAttribute("printDrill", pivot.PrintExpandCollapseButtons ? "1" : "0"),
            new XAttribute("indent", Math.Clamp(pivot.CompactRowLabelIndent, 0, 15).ToString(CultureInfo.InvariantCulture)),
            OptionalAttribute("altText", pivot.AltTextTitle),
            OptionalAttribute("altTextSummary", pivot.AltTextDescription),
            new XAttribute("reportLayout", ToPivotReportLayoutText(pivot.ReportLayout)),
            new XElement(
                workbookNs + "location",
                new XAttribute("ref", pivot.TargetRange.ToString()),
                new XAttribute("firstDataCol", "1"),
                new XAttribute("firstDataRow", "1"),
                new XAttribute("firstHeaderRow", "1")),
            ToPivotFieldsXml(pivot, workbookNs),
            ToPivotFieldCollectionXml("rowFields", pivot.RowFields, workbookNs),
            ToPivotFieldCollectionXml("colFields", pivot.ColumnFields, workbookNs),
            ToPivotPageFieldsXml(pivot.PageFields, workbookNs),
            ToPivotDataFieldsXml(pivot.DataFields, workbookNs, numberFormatIdMap),
            ToPivotCalculatedFieldsXml(pivot.CalculatedFields, workbookNs),
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
                new XAttribute("showLastColumn", "1"))));

    private static XElement ToPivotFieldsXml(PivotTableModel pivot, XNamespace workbookNs)
    {
        var maxFieldIndex = pivot.RowFields
            .Concat(pivot.ColumnFields)
            .Concat(pivot.PageFields)
            .Select(field => field.SourceFieldIndex)
            .Concat(pivot.DataFields.Select(field => field.SourceFieldIndex))
            .DefaultIfEmpty(-1)
            .Max();

        return new XElement(
            workbookNs + "pivotFields",
            new XAttribute("count", Math.Max(0, maxFieldIndex + 1).ToString(CultureInfo.InvariantCulture)),
            Enumerable.Range(0, Math.Max(0, maxFieldIndex + 1)).Select(index => new XElement(
                workbookNs + "pivotField",
                pivot.RowFields.Any(field => field.SourceFieldIndex == index) ? new XAttribute("axis", "axisRow") : null,
                pivot.ColumnFields.Any(field => field.SourceFieldIndex == index) ? new XAttribute("axis", "axisCol") : null,
                pivot.PageFields.Any(field => field.SourceFieldIndex == index) ? new XAttribute("axis", "axisPage") : null,
                pivot.ShowSubtotals ? new XAttribute("defaultSubtotal", "1") : null,
                pivot.ShowSubtotals && pivot.SubtotalPlacement == PivotSubtotalPlacement.Top ? new XAttribute("subtotalTop", "1") : null,
                new XAttribute("showAll", "0"),
                new XElement(workbookNs + "items",
                    new XAttribute("count", "1"),
                    new XElement(workbookNs + "item", new XAttribute("t", "default"))))));
    }

    private static XElement? ToPivotFieldCollectionXml(string elementName, IReadOnlyList<PivotFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + elementName,
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "field",
                    new XAttribute("x", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    string.IsNullOrWhiteSpace(field.SelectedItem) ? null : new XAttribute("name", field.SelectedItem),
                    field.SelectedItems is null || field.SelectedItems.Count == 0 ? null : new XAttribute("selectedItems", string.Join(",", field.SelectedItems)),
                    field.Grouping == PivotFieldGrouping.None ? null : new XAttribute("groupBy", ToPivotFieldGroupingText(field.Grouping)),
                    field.GroupStart is null ? null : new XAttribute("groupStart", FormatInvariant(field.GroupStart.Value)),
                    field.GroupEnd is null ? null : new XAttribute("groupEnd", FormatInvariant(field.GroupEnd.Value)),
                    field.GroupInterval is null ? null : new XAttribute("groupInterval", FormatInvariant(field.GroupInterval.Value)))));

    private static XElement? ToPivotPageFieldsXml(IReadOnlyList<PivotFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + "pageFields",
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "pageField",
                    new XAttribute("fld", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    string.IsNullOrWhiteSpace(field.SelectedItem) ? null : new XAttribute("name", field.SelectedItem),
                    field.SelectedItems is null || field.SelectedItems.Count == 0 ? null : new XAttribute("selectedItems", string.Join(",", field.SelectedItems)),
                    field.Grouping == PivotFieldGrouping.None ? null : new XAttribute("groupBy", ToPivotFieldGroupingText(field.Grouping)),
                    field.GroupStart is null ? null : new XAttribute("groupStart", FormatInvariant(field.GroupStart.Value)),
                    field.GroupEnd is null ? null : new XAttribute("groupEnd", FormatInvariant(field.GroupEnd.Value)),
                    field.GroupInterval is null ? null : new XAttribute("groupInterval", FormatInvariant(field.GroupInterval.Value)))));

    private static XElement? ToPivotDataFieldsXml(
        IReadOnlyList<PivotDataFieldModel> fields,
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
                    new XAttribute("fld", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("subtotal", string.IsNullOrWhiteSpace(field.SummaryFunction) ? "sum" : field.SummaryFunction),
                    string.IsNullOrWhiteSpace(field.CalculatedFieldName) ? null : new XAttribute("calculatedField", field.CalculatedFieldName),
                    field.ShowValuesAs == PivotShowValuesAs.None ? null : new XAttribute("showValuesAs", ToPivotShowValuesAsText(field.ShowValuesAs)),
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

    private static XElement? ToPivotCalculatedFieldsXml(IReadOnlyList<PivotCalculatedFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + "calculatedFields",
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select((field, index) => new XElement(
                    workbookNs + "calculatedField",
                    new XAttribute("name", field.Name),
                    new XAttribute("fld", index.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formula", field.Formula))));

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
                    new XAttribute("formula", item.Formula))));

    private static XElement? ToPivotValueFiltersXml(IReadOnlyList<PivotValueFilterModel> filters, XNamespace workbookNs) =>
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

    private static XElement? ToPivotLabelFiltersXml(IReadOnlyList<PivotLabelFilterModel> filters, XNamespace workbookNs) =>
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

    private static string ToPivotFieldGroupingText(PivotFieldGrouping grouping) =>
        grouping switch
        {
            PivotFieldGrouping.Year => "years",
            PivotFieldGrouping.Quarter => "quarters",
            PivotFieldGrouping.Month => "months",
            PivotFieldGrouping.Day => "days",
            PivotFieldGrouping.NumberRange => "numberRange",
            _ => "none"
        };

    private static string ToPivotReportLayoutText(PivotReportLayout layout) =>
        layout switch
        {
            PivotReportLayout.Compact => "compact",
            PivotReportLayout.Outline => "outline",
            _ => "tabular"
        };

    private static string ToPivotShowValuesAsText(PivotShowValuesAs showValuesAs) =>
        showValuesAs switch
        {
            PivotShowValuesAs.PercentOfGrandTotal => "percentOfGrandTotal",
            PivotShowValuesAs.PercentOfRowTotal => "percentOfRowTotal",
            PivotShowValuesAs.PercentOfColumnTotal => "percentOfColumnTotal",
            PivotShowValuesAs.RunningTotalIn => "runningTotalIn",
            PivotShowValuesAs.DifferenceFrom => "differenceFrom",
            PivotShowValuesAs.PercentDifferenceFrom => "percentDifferenceFrom",
            PivotShowValuesAs.RankSmallest => "rankSmallest",
            PivotShowValuesAs.RankLargest => "rankLargest",
            PivotShowValuesAs.Index => "index",
            PivotShowValuesAs.PercentOfParentRowTotal => "percentOfParentRowTotal",
            PivotShowValuesAs.PercentOfParentColumnTotal => "percentOfParentColumnTotal",
            PivotShowValuesAs.PercentOfParentTotal => "percentOfParentTotal",
            _ => "none"
        };

    private static string ToPivotValueFilterKindText(PivotValueFilterKind kind) =>
        kind switch
        {
            PivotValueFilterKind.Bottom => "bottom",
            PivotValueFilterKind.GreaterThan => "greaterThan",
            PivotValueFilterKind.GreaterThanOrEqual => "greaterThanOrEqual",
            PivotValueFilterKind.LessThan => "lessThan",
            PivotValueFilterKind.LessThanOrEqual => "lessThanOrEqual",
            PivotValueFilterKind.Equals => "equals",
            PivotValueFilterKind.DoesNotEqual => "doesNotEqual",
            PivotValueFilterKind.Between => "between",
            PivotValueFilterKind.NotBetween => "notBetween",
            PivotValueFilterKind.AboveAverage => "aboveAverage",
            PivotValueFilterKind.BelowAverage => "belowAverage",
            _ => "top"
        };

    private static string ToPivotLabelFilterKindText(PivotLabelFilterKind kind) =>
        kind switch
        {
            PivotLabelFilterKind.DoesNotEqual => "doesNotEqual",
            PivotLabelFilterKind.BeginsWith => "beginsWith",
            PivotLabelFilterKind.EndsWith => "endsWith",
            PivotLabelFilterKind.Contains => "contains",
            PivotLabelFilterKind.DoesNotContain => "doesNotContain",
            PivotLabelFilterKind.GreaterThan => "greaterThan",
            PivotLabelFilterKind.GreaterThanOrEqual => "greaterThanOrEqual",
            PivotLabelFilterKind.LessThan => "lessThan",
            PivotLabelFilterKind.LessThanOrEqual => "lessThanOrEqual",
            PivotLabelFilterKind.Between => "between",
            _ => "equals"
        };

    private static string FormatInvariant(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static XAttribute? OptionalAttribute(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value.Trim());

}
