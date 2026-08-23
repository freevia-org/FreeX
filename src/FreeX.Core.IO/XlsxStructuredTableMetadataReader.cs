using FreeX.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableMetadataReader
{
    public static StructuredTablePackageMetadata Load(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            return Load(archive);
        }
        catch
        {
            return StructuredTablePackageMetadata.Empty;
        }
    }

    internal static StructuredTablePackageMetadata Load(ZipArchive archive)
    {
        try
        {
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

    internal static StructuredTablePackageMetadata Load(
        ZipArchive archive,
        IReadOnlyDictionary<string, string>? worksheetPathsBySheetName,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? tableRelationshipIdsBySheetName)
    {
        if (worksheetPathsBySheetName is null ||
            tableRelationshipIdsBySheetName is null)
        {
            return Load(archive);
        }

        try
        {
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var tablesBySheetName = LoadTablesBySheetName(
                archive,
                worksheetPathsBySheetName,
                tableRelationshipIdsBySheetName,
                packageRelNs);
            return new StructuredTablePackageMetadata(tablesBySheetName);
        }
        catch
        {
            return Load(archive);
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

            var tableRelIds = XlsxWorksheetRelationshipIdReader.ReadAll(
                worksheetEntry,
                workbookNs + "tablePart",
                relNs + "id");
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

    private static Dictionary<string, List<PendingStructuredTableModel>> LoadTablesBySheetName(
        ZipArchive archive,
        IReadOnlyDictionary<string, string> worksheetPathsBySheetName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tableRelationshipIdsBySheetName,
        XNamespace packageRelNs)
    {
        var result = new Dictionary<string, List<PendingStructuredTableModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sheetName, worksheetPath) in worksheetPathsBySheetName)
        {
            if (!tableRelationshipIdsBySheetName.TryGetValue(sheetName, out var tableRelIds) ||
                tableRelIds.Count == 0)
            {
                continue;
            }

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
            XlsxXmlAttributeReader.ReadIntAttribute(root, "headerRowCount"),
            XlsxXmlAttributeReader.ReadIntAttribute(root, "totalsRowCount"),
            XlsxStructuredTableNativeMetadataReader.ReadOptionalBoolAttribute(root, "insertRow"),
            XlsxStructuredTableNativeMetadataReader.ReadOptionalBoolAttribute(root, "insertRowShift"),
            XlsxStructuredTableNativeMetadataReader.ReadOptionalBoolAttribute(root, "published"),
            root.Attribute("comment")?.Value,
            style?.Attribute("name")?.Value,
            XlsxXmlAttributeReader.ReadBoolAttribute(style, "showFirstColumn"),
            XlsxXmlAttributeReader.ReadBoolAttribute(style, "showLastColumn"),
            XlsxXmlAttributeReader.ReadBoolAttribute(style, "showRowStripes"),
            XlsxXmlAttributeReader.ReadBoolAttribute(style, "showColumnStripes"),
            tablePath,
            root.Element(workbookNs + "sortState")?.ToString(SaveOptions.DisableFormatting),
            XlsxStructuredTableNativeMetadataReader.ReadTableAttributes(root),
            XlsxStructuredTableNativeMetadataReader.ReadTableChildXmls(root, workbookNs),
            XlsxStructuredTableNativeMetadataReader.ReadAutoFilterAttributes(autoFilter),
            XlsxStructuredTableNativeMetadataReader.ReadAutoFilterChildXmls(autoFilter, workbookNs),
            XlsxStructuredTableNativeMetadataReader.ReadStyleInfoAttributes(style),
            XlsxStructuredTableNativeMetadataReader.ReadStyleInfoChildXmls(style),
            root.Element(workbookNs + "tableColumns")?
                .Elements(workbookNs + "tableColumn")
                .Select(column => new StructuredTableColumnModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(column, "id") ?? 0,
                    column.Attribute("name")?.Value ?? "",
                    column.Attribute("totalsRowLabel")?.Value,
                    column.Attribute("totalsRowFunction")?.Value,
                    ReadTableColumnFormula(column, workbookNs, "calculatedColumnFormula"),
                    ReadTableColumnFormula(column, workbookNs, "totalsRowFormula"),
                    XlsxStructuredTableNativeMetadataReader.ReadColumnChildXmls(column, workbookNs),
                    XlsxStructuredTableNativeMetadataReader.ReadColumnAttributes(column),
                    ReadTableColumnFormulaArray(column, workbookNs, "calculatedColumnFormula"),
                    ReadTableColumnFormulaArray(column, workbookNs, "totalsRowFormula")))
                .Where(column => column.Id > 0 && !string.IsNullOrWhiteSpace(column.Name))
                .ToList() ?? [],
            ReadFilterColumns(autoFilter, workbookNs));
        return true;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
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

    private static bool ReadTableColumnFormulaArray(XElement column, XNamespace workbookNs, string elementName)
    {
        var element = column.Element(workbookNs + elementName);
        if (element is null)
            return false;
        var raw = element.Attribute("array")?.Value;
        return raw is "1" or "true";
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
                var customFilters = column.Element(workbookNs + "customFilters");
                var colorFilterElement = column.Element(workbookNs + "colorFilter");
                var nativeFilters = XlsxStructuredTableNativeMetadataReader.ReadFilterXmls(column, workbookNs);
                var nativeCustomFiltersAttributes = customFilters?
                    .Attributes()
                    .Where(attribute => !IsModeledAttribute(attribute, "and"))
                    .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
                return new StructuredTableFilterColumnModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(column, "colId") ?? -1,
                    filters?
                        .Elements(workbookNs + "filter")
                        .Select(filter => filter.Attribute("val")?.Value)
                        // R99-io-structured-table-blank-filter-1: keep any present (non-null) val,
                        // including empty/whitespace-only strings -- a Table AutoFilter's "(Blanks)"
                        // checklist entry round-trips as the literal sentinel value "" (see
                        // FilterValueFormatter.ToText's BlankValue => "" and FilterCommand's
                        // ApplyToStructuredTableIfMatched, which never converts that "" entry into
                        // IncludeBlank=true before persisting it into StructuredTableFilterColumnModel),
                        // and a whitespace-only literal cell value (e.g. " ") round-trips as
                        // <filter val=" "/>. Dropping either here silently un-hides/re-hides rows the
                        // user explicitly filtered on the moment the workbook is saved and reopened.
                        // Only a genuinely absent val attribute (null) is meaningless and should be
                        // discarded. Mirrors XlsxWorksheetAutoFilterXmlMapper's identical fix (see its
                        // comment) for the plain worksheet-level AutoFilter reader.
                        .Where(value => value is not null)
                        .Select(value => value!)
                        .ToList() ?? [],
                    XlsxXmlAttributeReader.ReadBoolAttribute(filters, "blank"),
                    customFilters?
                        .Elements(workbookNs + "customFilter")
                        .Select(filter => new StructuredTableCustomFilterModel(
                            filter.Attribute("operator")?.Value,
                            filter.Attribute("val")?.Value,
                            ReadCustomFilterNativeAttributes(filter)))
                        .ToList() ?? [],
                    XlsxXmlAttributeReader.ReadBoolAttribute(customFilters, "and"),
                    customFilters?.Attribute("and")?.Value,
                    nativeCustomFiltersAttributes?.Count > 0 ? nativeCustomFiltersAttributes : null,
                    nativeFilters,
                    XlsxStructuredTableNativeMetadataReader.ReadFilterColumnAttributes(column))
                {
                    // R111-io-structured-table-colorfilter-roundtrip-1: a Table's own <colorFilter>
                    // (Filter by Cell/Font Colour or No Fill) now has to be parsed into the typed
                    // ColorFilter field, not left to the generic NativeFilterXmls passthrough --
                    // XlsxStructuredTableWriter.ToFilterColumnXml unconditionally excludes
                    // "colorFilter" from that passthrough (it re-emits ColorFilter itself instead),
                    // so a loaded table whose colorFilter never lands here was silently dropped on
                    // the very next save. The shared codec optionally resolves DXF colors when the
                    // minus dxf-color resolution: this reader has no access to the workbook's
                    // resolved differential styles, but Color is purely an informational
                    // convenience (the writer only ever emits dxfId/cellColor), so leaving it null
                    // here does not affect round-trip fidelity.
                    ColorFilter = XlsxAutoFilterXmlCodec.ReadColorFilter(colorFilterElement),
                    // R111-io-structured-table-dategroup-roundtrip-1: parse <filters>/<dateGroupItem>
                    // children (Excel's built-in Year/Quarter/Month/Day date-checklist filter) the same
                    // way XlsxAutoFilterXmlCodec does for the sheet-level
                    // AutoFilter path -- see StructuredTableFilterColumnModel.DateGroups for the full
                    // rationale. A <filters> element whose only children are dateGroupItem has no
                    // <filter> children at all, so without this the column had Values.Count==0 and
                    // nothing else to keep it, and the inclusion guard below dropped it entirely.
                    DateGroups = filters?
                        .Elements(workbookNs + "dateGroupItem")
                        .Select(XlsxAutoFilterXmlCodec.ReadDateGroupItem)
                        .ToList() ?? [],
                };
            })
            .Where(column => column.ColumnId >= 0 &&
                (column.Values.Count > 0 ||
                 column.IncludeBlank ||
                 column.DateGroups.Count > 0 ||
                 column.CustomFilters.Count > 0 ||
                 column.CustomFiltersAndRaw is not null ||
                 column.NativeCustomFiltersAttributes?.Count > 0 ||
                 column.ColorFilter is not null ||
                 column.NativeFilterXmls.Count > 0 ||
                 column.NativeAttributes?.Count > 0))
            .ToList();
    }

    private static IReadOnlyDictionary<string, string>? ReadCustomFilterNativeAttributes(XElement filter)
    {
        var attributes = filter.Attributes()
            .Where(attribute =>
                !IsModeledAttribute(attribute, "operator") &&
                !IsModeledAttribute(attribute, "val"))
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }

    private static bool IsModeledAttribute(XAttribute attribute, string localName) =>
        attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == localName;

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
    int? HeaderRowCount,
    int? TotalsRowCount,
    bool? InsertRow,
    bool? InsertRowShift,
    bool? Published,
    string? Comment,
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
        null,
        null,
        null,
        null,
        null,
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

