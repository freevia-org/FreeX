using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Freexcel.Core.Model;
using System.Globalization;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetAutoFilterMapper
{
    public static WorksheetAutoFilterModel? Read(XElement? autoFilter)
    {
        if (autoFilter is null)
            return null;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var model = new WorksheetAutoFilterModel(
            autoFilter.Attribute("ref")?.Value,
            autoFilter.ToString(SaveOptions.DisableFormatting))
        {
            NativeAttributes = ReadNativeAttributes(autoFilter),
            NativeChildXmls = autoFilter
                .Elements()
                .Where(element => element.Name != worksheetNs + "filterColumn")
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToArray()
        };
        model.FilterColumns.AddRange(ReadFilterColumns(autoFilter, worksheetNs));
        return model;
    }

    public static void MaterializeFilters(Sheet sheet)
    {
        var autoFilter = sheet.AutoFilter;
        if (autoFilter is null || autoFilter.FilterColumns.Count == 0 || string.IsNullOrWhiteSpace(autoFilter.Reference))
            return;

        GridRange range;
        try
        {
            range = GridRange.Parse(autoFilter.Reference, sheet.Id);
        }
        catch
        {
            return;
        }

        var filters = BuildFilters(autoFilter, range).ToList();
        if (filters.Count != autoFilter.FilterColumns.Count)
            return;

        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, filters))
                sheet.FilterHiddenRows.Add(row);
        }
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.AutoFilter is not null))
        {
            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(worksheetNs + "autoFilter")?.Remove();
            if (ToAutoFilterXml(sheet.AutoFilter, worksheetNs) is { } autoFilter)
                InsertAutoFilter(root, worksheetNs, autoFilter);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static XElement? ToAutoFilterXml(WorksheetAutoFilterModel? autoFilter, XNamespace worksheetNs)
    {
        if (autoFilter is null)
            return null;

        var hasModeledMetadata =
            autoFilter.FilterColumns.Count > 0 ||
            autoFilter.NativeAttributes?.Count > 0 ||
            autoFilter.NativeChildXmls?.Count > 0;
        if (!hasModeledMetadata && !string.IsNullOrWhiteSpace(autoFilter.NativeXml))
        {
            try
            {
                var element = XElement.Parse(autoFilter.NativeXml);
                return element.Name == worksheetNs + "autoFilter" ? element : null;
            }
            catch
            {
                // Fall back to a range-only AutoFilter when legacy native JSON contains malformed XML.
            }
        }

        return string.IsNullOrWhiteSpace(autoFilter.Reference)
            ? null
            : ToModeledAutoFilterXml(autoFilter, worksheetNs);
    }

    private static XElement ToModeledAutoFilterXml(WorksheetAutoFilterModel autoFilter, XNamespace worksheetNs)
    {
        var element = new XElement(
            worksheetNs + "autoFilter",
            new XAttribute("ref", autoFilter.Reference!),
            autoFilter.FilterColumns.Select(filterColumn => ToFilterColumnXml(filterColumn, worksheetNs)));
        foreach (var (name, value) in autoFilter.NativeAttributes ?? new Dictionary<string, string>())
        {
            TrySetNativeAttributeIfMissing(element, name, value);
        }

        foreach (var nativeChildXml in autoFilter.NativeChildXmls ?? [])
        {
            if (TryParseNativeWorksheetChild(nativeChildXml, worksheetNs, "filterColumn") is { } nativeChild)
                element.Add(nativeChild);
        }

        return element;
    }

    private static XElement ToFilterColumnXml(WorksheetAutoFilterColumnModel filterColumn, XNamespace worksheetNs)
    {
        var element = new XElement(
            worksheetNs + "filterColumn",
            new XAttribute("colId", filterColumn.ColumnId.ToString(CultureInfo.InvariantCulture)));
        foreach (var (name, value) in filterColumn.NativeAttributes ?? new Dictionary<string, string>())
        {
            TrySetNativeAttributeIfMissing(element, name, value);
        }

        var hasCustomFilters = filterColumn.CustomFilters.Count > 0;
        if (!hasCustomFilters && (filterColumn.Values.Count > 0 || filterColumn.IncludeBlank))
        {
            element.Add(new XElement(
                worksheetNs + "filters",
                filterColumn.IncludeBlank ? new XAttribute("blank", "1") : null,
                filterColumn.Values.Select(value => new XElement(worksheetNs + "filter", new XAttribute("val", value)))));
        }

        if (hasCustomFilters)
        {
            var customFilters = new XElement(
                worksheetNs + "customFilters",
                filterColumn.CustomFilters.Select(customFilter => ToCustomFilterXml(customFilter, worksheetNs)));
            if (filterColumn.CustomFiltersAndRaw is not null)
                customFilters.SetAttributeValue("and", filterColumn.CustomFiltersAndRaw);
            else if (filterColumn.CustomFiltersAnd)
                customFilters.SetAttributeValue("and", "1");

            foreach (var (name, value) in filterColumn.NativeCustomFiltersAttributes ?? new Dictionary<string, string>())
            {
                TrySetNativeAttributeIfMissing(customFilters, name, value);
            }

            element.Add(customFilters);
        }

        foreach (var nativeFilterXml in filterColumn.NativeFilterXmls)
        {
            if (TryParseNativeWorksheetChild(nativeFilterXml, worksheetNs, "filters", "customFilters") is { } nativeFilter)
                element.Add(nativeFilter);
        }

        return element;
    }

    private static XElement ToCustomFilterXml(WorksheetAutoFilterCustomFilterModel customFilter, XNamespace worksheetNs)
    {
        var element = new XElement(worksheetNs + "customFilter");
        if (customFilter.Operator is not null)
            element.SetAttributeValue("operator", customFilter.Operator);
        if (customFilter.Value is not null)
            element.SetAttributeValue("val", customFilter.Value);

        foreach (var (name, value) in customFilter.NativeAttributes ?? new Dictionary<string, string>())
        {
            TrySetNativeAttributeIfMissing(element, name, value);
        }

        return element;
    }

    private static XElement? TryParseNativeWorksheetChild(string? nativeXml, XNamespace worksheetNs, params string[] modeledLocalNames)
    {
        if (string.IsNullOrWhiteSpace(nativeXml))
            return null;

        try
        {
            var nativeChild = XElement.Parse(nativeXml);
            return nativeChild.Name.Namespace == worksheetNs && !modeledLocalNames.Contains(nativeChild.Name.LocalName, StringComparer.Ordinal)
                ? nativeChild
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TrySetNativeAttributeIfMissing(XElement element, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            var attributeName = XName.Get(name);
            if (element.Attribute(attributeName) is null)
                element.SetAttributeValue(attributeName, value);
        }
        catch (ArgumentException)
        {
        }
        catch (XmlException)
        {
        }
    }

    private static IReadOnlyDictionary<string, string>? ReadNativeAttributes(XElement autoFilter)
    {
        var attributes = autoFilter.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName != "ref")
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }

    private static IEnumerable<WorksheetAutoFilterColumnModel> ReadFilterColumns(XElement autoFilter, XNamespace worksheetNs)
    {
        foreach (var column in autoFilter.Elements(worksheetNs + "filterColumn"))
        {
            var filters = column.Element(worksheetNs + "filters");
            var customFilters = column.Element(worksheetNs + "customFilters");
            var nativeFilters = column.Elements()
                .Where(element => element.Name != worksheetNs + "filters" && element.Name != worksheetNs + "customFilters")
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToArray();
            var nativeAttributes = column.Attributes()
                .Where(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName != "colId")
                .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
            var customFiltersAnd = XlsxXmlAttributeReader.ReadBoolAttribute(customFilters, "and");
            var nativeCustomFiltersAttributes = customFilters?
                .Attributes()
                .Where(attribute => !IsWorksheetAutoFilterModeledAttribute(attribute, "and"))
                .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
            var filterColumn = new WorksheetAutoFilterColumnModel(
                XlsxXmlAttributeReader.ReadIntAttribute(column, "colId") ?? -1,
                filters?
                    .Elements(worksheetNs + "filter")
                    .Select(filter => filter.Attribute("val")?.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToArray() ?? [],
                XlsxXmlAttributeReader.ReadBoolAttribute(filters, "blank"),
                customFilters?
                    .Elements(worksheetNs + "customFilter")
                    .Select(filter => new WorksheetAutoFilterCustomFilterModel(
                        filter.Attribute("operator")?.Value,
                        filter.Attribute("val")?.Value,
                        ReadCustomFilterNativeAttributes(filter)))
                    .ToArray() ?? [],
                customFiltersAnd,
                customFilters?.Attribute("and")?.Value,
                nativeCustomFiltersAttributes?.Count > 0 ? nativeCustomFiltersAttributes : null,
                nativeFilters,
                nativeAttributes.Count == 0 ? null : nativeAttributes);
            if (filterColumn.ColumnId >= 0 &&
                (filterColumn.Values.Count > 0 ||
                 filterColumn.IncludeBlank ||
                 filterColumn.CustomFilters.Count > 0 ||
                 filterColumn.CustomFiltersAndRaw is not null ||
                 filterColumn.NativeCustomFiltersAttributes?.Count > 0 ||
                 filterColumn.NativeFilterXmls.Count > 0 ||
                 filterColumn.NativeAttributes?.Count > 0))
            {
                yield return filterColumn;
            }
        }
    }

    private static IReadOnlyDictionary<string, string>? ReadCustomFilterNativeAttributes(XElement filter)
    {
        var attributes = filter.Attributes()
            .Where(attribute =>
                !IsWorksheetAutoFilterModeledAttribute(attribute, "operator") &&
                !IsWorksheetAutoFilterModeledAttribute(attribute, "val"))
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }

    private static bool IsWorksheetAutoFilterModeledAttribute(XAttribute attribute, string localName) =>
        attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == localName;

    private static IEnumerable<WorksheetAutoFilterState> BuildFilters(WorksheetAutoFilterModel autoFilter, GridRange range)
    {
        foreach (var filterColumn in autoFilter.FilterColumns)
        {
            if (filterColumn.ColumnId < 0)
                continue;
            if (filterColumn.CustomFilters.Count > 0 ||
                filterColumn.CustomFiltersAndRaw is not null ||
                filterColumn.NativeCustomFiltersAttributes?.Count > 0 ||
                filterColumn.NativeFilterXmls.Count > 0)
            {
                continue;
            }

            yield return new WorksheetAutoFilterState(
                range.Start.Col + (uint)filterColumn.ColumnId,
                new HashSet<string>(filterColumn.Values, StringComparer.OrdinalIgnoreCase),
                filterColumn.IncludeBlank);
        }
    }

    private static bool RowMatchesAllFilters(
        Sheet sheet,
        uint row,
        IReadOnlyList<WorksheetAutoFilterState> filters)
    {
        foreach (var filter in filters)
        {
            var text = ToFilterText(sheet.GetValue(row, filter.Column));
            if (text.Length == 0 && filter.IncludeBlank)
                continue;
            if (!filter.AllowedValues.Contains(text))
                return false;
        }

        return true;
    }

    private static void InsertAutoFilter(XElement root, XNamespace worksheetNs, XElement autoFilter)
    {
        string[] laterWorksheetElements =
        [
            "sortState",
            "dataConsolidate",
            "customSheetViews",
            "mergeCells",
            "phoneticPr",
            "conditionalFormatting",
            "dataValidations",
            "hyperlinks",
            "printOptions",
            "pageMargins",
            "pageSetup",
            "headerFooter",
            "rowBreaks",
            "colBreaks",
            "customProperties",
            "cellWatches",
            "ignoredErrors",
            "smartTags",
            "drawing",
            "legacyDrawing",
            "legacyDrawingHF",
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts",
            "extLst"
        ];

        var insertionPoint = root.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == worksheetNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is not null)
            insertionPoint.AddBeforeSelf(autoFilter);
        else
            root.Add(autoFilter);
    }

    private static string ToFilterText(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue dateTime => dateTime.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ErrorValue error => error.Code,
        _ => string.Empty
    };

    private sealed record WorksheetAutoFilterState(
        uint Column,
        HashSet<string> AllowedValues,
        bool IncludeBlank);
}
