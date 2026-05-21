using Freexcel.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxStructuredTableMetadataReader
{
    public static StructuredTablePackageMetadata Load(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || workbookRelsEntry is null)
                return StructuredTablePackageMetadata.Empty;

            var workbookXml = LoadXml(workbookEntry);
            var workbookRelsXml = LoadXml(workbookRelsEntry);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var workbookRels = workbookRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
                .ToDictionary(
                    e => e.Attribute("Id")!.Value,
                    e => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", e.Attribute("Target")!.Value),
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var sheetsByPath = GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
                .ToDictionary(pair => pair.WorksheetPath, pair => pair.SheetName, StringComparer.OrdinalIgnoreCase);
            var tablesBySheetName = LoadTablesBySheetName(archive, sheetsByPath, workbookNs, relNs, packageRelNs);
            return new StructuredTablePackageMetadata(tablesBySheetName);
        }
        catch
        {
            return StructuredTablePackageMetadata.Empty;
        }
    }

    private static Dictionary<string, List<PendingStructuredTableModel>> LoadTablesBySheetName(
        ZipArchive archive,
        IReadOnlyDictionary<string, string> sheetsByPath,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var result = new Dictionary<string, List<PendingStructuredTableModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (worksheetPath, sheetName) in sheetsByPath)
        {
            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = LoadXml(worksheetEntry);
            var tableRelIds = worksheetXml.Root?
                .Element(workbookNs + "tableParts")?
                .Elements(workbookNs + "tablePart")
                .Select(e => e.Attribute(relNs + "id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList() ?? [];
            if (tableRelIds.Count == 0)
                continue;

            var worksheetRels = LoadRelationshipTargets(archive, XlsxPackagePath.GetRelationshipPartPath(worksheetPath), worksheetPath, packageRelNs);
            foreach (var tableRelId in tableRelIds)
            {
                if (!worksheetRels.TryGetValue(tableRelId, out var tablePath))
                    continue;

                var tableEntry = archive.GetEntry(tablePath);
                if (tableEntry is null)
                    continue;

                var tableXml = LoadXml(tableEntry);
                if (TryReadTable(tableXml, tablePath, out var table))
                {
                    if (!result.TryGetValue(sheetName, out var sheetTables))
                    {
                        sheetTables = [];
                        result[sheetName] = sheetTables;
                    }

                    sheetTables.Add(table);
                }
            }
        }

        return result;
    }

    private static bool TryReadTable(XDocument tableXml, string tablePath, out PendingStructuredTableModel table)
    {
        table = PendingStructuredTableModel.Empty(tablePath);
        var root = tableXml.Root;
        if (root is null)
            return false;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var id = XlsxXmlAttributeReader.ReadIntAttribute(root, "id") ?? 0;
        var name = root.Attribute("name")?.Value ?? "";
        var displayName = root.Attribute("displayName")?.Value ?? name;
        var rangeReference = root.Attribute("ref")?.Value ?? "";
        if (id <= 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rangeReference))
            return false;

        var style = root.Element(workbookNs + "tableStyleInfo");
        var autoFilter = root.Element(workbookNs + "autoFilter");
        table = new PendingStructuredTableModel(
            id,
            name,
            displayName,
            rangeReference,
            autoFilter is not null,
            XlsxXmlAttributeReader.ReadBoolAttribute(root, "totalsRowShown"),
            style?.Attribute("name")?.Value,
            XlsxXmlAttributeReader.ReadBoolAttribute(style, "showFirstColumn"),
            XlsxXmlAttributeReader.ReadBoolAttribute(style, "showLastColumn"),
            XlsxXmlAttributeReader.ReadBoolAttribute(style, "showRowStripes"),
            XlsxXmlAttributeReader.ReadBoolAttribute(style, "showColumnStripes"),
            tablePath,
            root.Element(workbookNs + "sortState")?.ToString(SaveOptions.DisableFormatting),
            ReadNativeTableAttributes(root),
            ReadNativeTableChildXmls(root, workbookNs),
            ReadNativeAutoFilterAttributes(autoFilter),
            ReadNativeAutoFilterChildXmls(autoFilter, workbookNs),
            ReadNativeStyleInfoAttributes(style),
            ReadNativeStyleInfoChildXmls(style),
            root.Element(workbookNs + "tableColumns")?
                .Elements(workbookNs + "tableColumn")
                .Select(column => new StructuredTableColumnModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(column, "id") ?? 0,
                    column.Attribute("name")?.Value ?? "",
                    column.Attribute("totalsRowLabel")?.Value,
                    column.Attribute("totalsRowFunction")?.Value,
                    ReadTableColumnFormula(column, workbookNs, "calculatedColumnFormula"),
                    ReadTableColumnFormula(column, workbookNs, "totalsRowFormula"),
                    ReadNativeColumnChildXmls(column, workbookNs),
                    ReadNativeColumnAttributes(column)))
                .Where(column => column.Id > 0 && !string.IsNullOrWhiteSpace(column.Name))
                .ToList() ?? [],
            ReadFilterColumns(autoFilter, workbookNs));
        return true;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, string> LoadRelationshipTargets(
        ZipArchive archive,
        string relsPath,
        string sourcePart,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var relsXml = LoadXml(relsEntry);
        return relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.ResolveRelationshipTarget(sourcePart, e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string SheetName, string WorksheetPath)> GetWorkbookSheetPaths(
        XDocument workbookXml,
        IReadOnlyDictionary<string, string> workbookRels,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(name) &&
                !string.IsNullOrWhiteSpace(relId) &&
                workbookRels.TryGetValue(relId, out var worksheetPath))
            {
                yield return (name, worksheetPath);
            }
        }
    }

    private static string? ReadTableColumnFormula(XElement column, XNamespace workbookNs, string elementName)
    {
        var formula = column.Element(workbookNs + elementName)?.Value;
        return string.IsNullOrWhiteSpace(formula) ? null : formula;
    }

    private static List<string> ReadNativeColumnChildXmls(XElement column, XNamespace workbookNs) =>
        column.Elements()
            .Where(element =>
                element.Name != workbookNs + "calculatedColumnFormula" &&
                element.Name != workbookNs + "totalsRowFormula")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

    private static Dictionary<string, string> ReadNativeColumnAttributes(XElement column)
    {
        string[] modeledAttributes = ["id", "name", "totalsRowLabel", "totalsRowFunction"];
        return column.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, string>? ReadNativeStyleInfoAttributes(XElement? styleInfo)
    {
        if (styleInfo is null)
            return null;

        string[] modeledAttributes = ["name", "showFirstColumn", "showLastColumn", "showRowStripes", "showColumnStripes"];
        var attributes = styleInfo.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }

    private static List<string>? ReadNativeStyleInfoChildXmls(XElement? styleInfo)
    {
        if (styleInfo is null)
            return null;

        var children = styleInfo.Elements()
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
        return children.Count == 0 ? null : children;
    }

    private static Dictionary<string, string>? ReadNativeTableAttributes(XElement table)
    {
        string[] modeledAttributes = ["id", "name", "displayName", "ref", "totalsRowShown"];
        var attributes = table.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }

    private static List<string>? ReadNativeTableChildXmls(XElement table, XNamespace workbookNs)
    {
        XName[] modeledChildren =
        [
            workbookNs + "autoFilter",
            workbookNs + "sortState",
            workbookNs + "tableColumns",
            workbookNs + "tableStyleInfo"
        ];
        var children = table.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
        return children.Count == 0 ? null : children;
    }

    private static Dictionary<string, string>? ReadNativeAutoFilterAttributes(XElement? autoFilter)
    {
        if (autoFilter is null)
            return null;

        var attributes = autoFilter.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName != "ref")
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }

    private static List<string>? ReadNativeAutoFilterChildXmls(XElement? autoFilter, XNamespace workbookNs)
    {
        if (autoFilter is null)
            return null;

        var children = autoFilter.Elements()
            .Where(element => element.Name != workbookNs + "filterColumn")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
        return children.Count == 0 ? null : children;
    }

    private static List<StructuredTableFilterColumnModel> ReadFilterColumns(
        XElement? autoFilter,
        XNamespace workbookNs)
    {
        if (autoFilter is null)
            return [];

        return autoFilter
            .Elements(workbookNs + "filterColumn")
            .Select(column =>
            {
                var filters = column.Element(workbookNs + "filters");
                var nativeFilters = ReadNativeFilterXmls(column, workbookNs);
                return new StructuredTableFilterColumnModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(column, "colId") ?? -1,
                    filters?
                        .Elements(workbookNs + "filter")
                        .Select(filter => filter.Attribute("val")?.Value)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .ToList() ?? [],
                    XlsxXmlAttributeReader.ReadBoolAttribute(filters, "blank"),
                    nativeFilters,
                    ReadNativeFilterColumnAttributes(column));
            })
            .Where(column => column.ColumnId >= 0 && (column.Values.Count > 0 || column.IncludeBlank || column.NativeFilterXmls.Count > 0 || column.NativeAttributes?.Count > 0))
            .ToList();
    }

    private static List<string> ReadNativeFilterXmls(XElement filterColumn, XNamespace workbookNs) =>
        filterColumn.Elements()
            .Where(element => element.Name != workbookNs + "filters")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

    private static Dictionary<string, string>? ReadNativeFilterColumnAttributes(XElement filterColumn)
    {
        var attributes = filterColumn.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName != "colId")
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }
}

internal sealed record StructuredTablePackageMetadata(
    IReadOnlyDictionary<string, List<PendingStructuredTableModel>> TablesBySheetName)
{
    public static StructuredTablePackageMetadata Empty { get; } = new(
        new Dictionary<string, List<PendingStructuredTableModel>>(StringComparer.OrdinalIgnoreCase));
}

internal sealed record PendingStructuredTableModel(
    int Id,
    string Name,
    string DisplayName,
    string RangeReference,
    bool HasAutoFilter,
    bool TotalsRowShown,
    string? StyleName,
    bool ShowFirstColumn,
    bool ShowLastColumn,
    bool ShowRowStripes,
    bool ShowColumnStripes,
    string PackagePart,
    string? NativeSortStateXml,
    IReadOnlyDictionary<string, string>? NativeAttributes,
    IReadOnlyList<string>? NativeChildXmls,
    IReadOnlyDictionary<string, string>? NativeAutoFilterAttributes,
    IReadOnlyList<string>? NativeAutoFilterChildXmls,
    IReadOnlyDictionary<string, string>? NativeStyleInfoAttributes,
    IReadOnlyList<string>? NativeStyleInfoChildXmls,
    IReadOnlyList<StructuredTableColumnModel> Columns,
    IReadOnlyList<StructuredTableFilterColumnModel> FilterColumns)
{
    public static PendingStructuredTableModel Empty(string packagePart) => new(
        0,
        "",
        "",
        "",
        false,
        false,
        null,
        false,
        false,
        false,
        false,
        packagePart,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        [],
        []);
}

