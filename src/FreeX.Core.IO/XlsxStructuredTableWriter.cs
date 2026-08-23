using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        // Reserve every table PackagePart already claimed anywhere in the workbook up front, so a
        // freshly generated "xl/tables/tableN.xml" name can never collide with (and silently
        // overwrite) another table that kept its own preserved package part.
        var claimedTablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var otherSheet in workbook.Sheets)
        {
            foreach (var otherTable in otherSheet.StructuredTables)
            {
                if (string.IsNullOrWhiteSpace(otherTable.PackagePart))
                    continue;

                var otherPath = XlsxPackagePath.NormalizePackagePath(otherTable.PackagePart);
                if (otherPath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase))
                    claimedTablePaths.Add(otherPath);
            }
        }

        var tablePartIndex = 1;

        // R128-io-table-writer-collision-guard: claimedTablePaths above is pre-seeded with EVERY
        // table's own preserved PackagePart across the whole workbook (including the table we're
        // about to process), so ".Add" returning false against claimedTablePaths can't distinguish
        // "this is my own path, first time actually writing it" from "another table already wrote
        // this exact path during this save". Track the latter separately.
        var writtenTablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // R107-commands-autofilter-table-color-sync-1: allocate any missing colour-filter dxfs into
        // xl/styles.xml BEFORE writing any table part below, so the filterColumn writer can reference
        // the freshly allocated dxfId (mirrors XlsxWorksheetSourceIndependentMetadataBatchWriter's
        // identical ordering for worksheet-level AutoFilters -- see XlsxAutoFilterColorFilterDxfWriter).
        IReadOnlyDictionary<(SheetId SheetId, int TableId, int ColumnId), int>? tableColorFilterDxfIds = null;
        if (XlsxAutoFilterColorFilterDxfWriter.HasUnallocatedStructuredTableColorFilters(workbook))
            tableColorFilterDxfIds = XlsxAutoFilterColorFilterDxfWriter.SaveForStructuredTables(archive, workbook, workbookNs);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.StructuredTables.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var worksheetRoot = worksheetXml.Root;
            if (worksheetRoot is null)
                continue;

            var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
            var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
            var worksheetRelsXml = worksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));

            var tableParts = new List<XElement>();
            foreach (var table in sheet.StructuredTables)
            {
                var tablePath = string.IsNullOrWhiteSpace(table.PackagePart)
                    ? null
                    : XlsxPackagePath.NormalizePackagePath(table.PackagePart);
                // R128-io-table-writer-collision-guard: a preserved path is only safe to reuse as-is
                // the FIRST time it's actually WRITTEN in this save -- if writtenTablePaths.Add returns
                // false here, some other table processed earlier in this same loop already wrote this
                // exact path (two StructuredTableModel instances aliasing the same PackagePart, e.g. a
                // Duplicate Sheet clone that inherited its source table's PackagePart), so writing to it
                // again verbatim would silently overwrite that other table's freshly-written XML in the
                // zip. Fall back to minting a fresh, unclaimed path exactly like the "no preserved path"
                // branch below, instead of trusting an aliased PackagePart (defense in depth alongside
                // the Sheet.Clone fix that stops structured-table clones from aliasing PackagePart in
                // the first place). Note this checks writtenTablePaths, NOT claimedTablePaths --
                // claimedTablePaths is pre-seeded with every table's own preserved path up front, so an
                // ordinary first-time preserved path is already "claimed" there and that Add would
                // always (incorrectly) report a collision.
                if (tablePath is not null &&
                    tablePath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
                    !writtenTablePaths.Add(tablePath))
                {
                    tablePath = null;
                }

                if (tablePath is null)
                {
                    // Generate the next path not already claimed by another table's preserved
                    // package part (or by another table generated earlier in this same save).
                    do
                    {
                        tablePath = $"xl/tables/table{tablePartIndex}.xml";
                        tablePartIndex++;
                    } while (!claimedTablePaths.Add(tablePath));
                    writtenTablePaths.Add(tablePath);
                }

                XlsxPackageXmlEditor.ReplaceXml(archive, tablePath, ToXml(table, tablePath, sheet, tableColorFilterDxfIds));
                XlsxPackageXmlEditor.EnsureSpecificContentType(
                    archive,
                    $"/{tablePath}",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml");
                var tableRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                    worksheetRelsXml,
                    packageRelNs,
                    worksheetPath,
                    tablePath,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table");
                tableParts.Add(new XElement(workbookNs + "tablePart", new XAttribute(relNs + "id", tableRelId)));
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);
            worksheetRoot.Elements(workbookNs + "tableParts").Remove();
            InsertWorksheetTablePartsInOrder(worksheetRoot, workbookNs, new XElement(
                workbookNs + "tableParts",
                new XAttribute("count", tableParts.Count.ToString(CultureInfo.InvariantCulture)),
                tableParts));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static void InsertWorksheetTablePartsInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement tableParts)
    {
        XElement? extLst = null;
        foreach (var element in worksheetRoot.Elements(workbookNs + "extLst"))
        {
            extLst = element;
            break;
        }

        if (extLst is null)
            worksheetRoot.Add(tableParts);
        else
            extLst.AddBeforeSelf(tableParts);
    }

    private static XDocument ToXml(
        StructuredTableModel table,
        string tablePath,
        Sheet sheet,
        IReadOnlyDictionary<(SheetId SheetId, int TableId, int ColumnId), int>? colorFilterDxfIds)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var columns = table.Columns.Count > 0
            ? table.Columns.ToList()
            : Enumerable.Range(1, (int)(table.Range.End.Col - table.Range.Start.Col + 1))
                .Select(index => new StructuredTableColumnModel(index, $"Column{index}"))
                .ToList();

        var root = new XElement(
            workbookNs + "table",
            new XAttribute("id", table.Id > 0 ? table.Id : ExtractTrailingNumber(tablePath)),
            new XAttribute("name", string.IsNullOrWhiteSpace(table.Name) ? $"Table{ExtractTrailingNumber(tablePath)}" : table.Name),
            new XAttribute("displayName", string.IsNullOrWhiteSpace(table.DisplayName) ? table.Name : table.DisplayName),
            new XAttribute("ref", table.Range.ToString()),
            new XAttribute("totalsRowShown", table.TotalsRowShown ? "1" : "0"));
        if (table.HeaderRowCount is { } headerRowCount)
            root.SetAttributeValue("headerRowCount", headerRowCount.ToString(CultureInfo.InvariantCulture));
        if (table.TotalsRowCount is { } totalsRowCount)
            root.SetAttributeValue("totalsRowCount", totalsRowCount.ToString(CultureInfo.InvariantCulture));
        if (table.InsertRow is { } insertRow)
            root.SetAttributeValue("insertRow", insertRow ? "1" : "0");
        if (table.InsertRowShift is { } insertRowShift)
            root.SetAttributeValue("insertRowShift", insertRowShift ? "1" : "0");
        if (table.Published is { } published)
            root.SetAttributeValue("published", published ? "1" : "0");
        if (!string.IsNullOrWhiteSpace(table.Comment))
            root.SetAttributeValue("comment", table.Comment);
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(root, table.NativeAttributes);

        if (table.HasAutoFilter)
            root.Add(ToAutoFilterXml(table, workbookNs, sheet.Id, colorFilterDxfIds));
        if (TryCreateNativeSortState(table.NativeSortStateXml, workbookNs) is { } sortState)
            root.Add(sortState);
        root.Add(new XElement(
            workbookNs + "tableColumns",
            new XAttribute("count", columns.Count.ToString(CultureInfo.InvariantCulture)),
            columns.Select((column, index) => ToColumnXml(column, workbookNs, sheet, table, index))));
        if (ShouldWriteStyleInfo(table))
            root.Add(ToStyleInfoXml(table, workbookNs));
        foreach (var nativeChildXml in table.NativeChildXmls ?? [])
        {
            TryAddNativeTableElement(root, nativeChildXml, workbookNs);
        }

        XlsxStructuredTableSchemaNormalizer.NormalizeElement(root, tablePath);
        return new XDocument(root);
    }

    private static XElement? TryCreateNativeSortState(string? nativeXml, XNamespace workbookNs)
    {
        if (string.IsNullOrWhiteSpace(nativeXml))
            return null;

        try
        {
            var sortState = XElement.Parse(nativeXml);
            if (sortState.Name != workbookNs + "sortState")
                return null;

            XlsxWorksheetSortStateNormalizer.NormalizeElement(sortState);
            return sortState;
        }
        catch
        {
            // Ignore malformed native table sort payloads from older saves.
            return null;
        }
    }

    private static XElement ToColumnXml(
        StructuredTableColumnModel column,
        XNamespace workbookNs,
        Sheet sheet,
        StructuredTableModel table,
        int columnIndex)
    {
        var element = new XElement(
            workbookNs + "tableColumn",
            new XAttribute("id", column.Id),
            new XAttribute("name", ColumnHeaderText(sheet, table, columnIndex, column.Name)),
            string.IsNullOrWhiteSpace(column.TotalsRowLabel) ? null : new XAttribute("totalsRowLabel", column.TotalsRowLabel),
            string.IsNullOrWhiteSpace(column.TotalsRowFunction) ? null : new XAttribute("totalsRowFunction", column.TotalsRowFunction),
            string.IsNullOrWhiteSpace(column.CalculatedColumnFormula)
                ? null
                : new XElement(
                    workbookNs + "calculatedColumnFormula",
                    column.IsCalculatedColumnFormulaArray ? new XAttribute("array", "1") : null,
                    column.CalculatedColumnFormula),
            string.IsNullOrWhiteSpace(column.TotalsRowFormula)
                ? null
                : new XElement(
                    workbookNs + "totalsRowFormula",
                    column.IsTotalsRowFormulaArray ? new XAttribute("array", "1") : null,
                    column.TotalsRowFormula));

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, column.NativeAttributes);

        foreach (var nativeChildXml in column.NativeChildXmls ?? [])
        {
            TryAddNativeTableElement(element, nativeChildXml, workbookNs, "calculatedColumnFormula", "totalsRowFormula");
        }

        return element;
    }

    // R94: an ordinary header-cell edit renames a table column in Excel semantics but only ever
    // updates the sheet cell text -- nothing syncs StructuredTableColumnModel.Name back to match
    // (see StructuredReferenceResolver.ColumnHeaderText / StructuredTableTotalsCommand's own
    // mirror of the same gap). Writing tableColumn/@name from the stale stored Name here would
    // save a table1.xml whose declared column name disagrees with the header row actually
    // written into the sheet, which Excel treats as a repair-worthy inconsistency (ECMA-376
    // 18.3.1.4/18.3.1.24 read tableColumn/@name as authoritative and expect it to match the
    // header cell). Mirror the resolver's live-header-first lookup so the SAVED FILE is always
    // internally self-consistent. Falls back to the stored/synthesized name for a headerless
    // table (HeaderRowCount == 0), a blank header cell, or a header cell holding a non-text value
    // (number, formula, error, boolean) -- exactly like the resolver and totals-refresh siblings,
    // which also only special-case plain TextValue header cells.
    private static string ColumnHeaderText(Sheet sheet, StructuredTableModel table, int columnIndex, string storedName)
    {
        if (table.HeaderRowCount is 0)
            return storedName;

        var headerCol = table.Range.Start.Col + (uint)columnIndex;
        return sheet.GetCell(table.Range.Start.Row, headerCol)?.Value is TextValue { Value.Length: > 0 } text
            ? text.Value
            : storedName;
    }

    private static XElement ToStyleInfoXml(StructuredTableModel table, XNamespace workbookNs)
    {
        var element = new XElement(
            workbookNs + "tableStyleInfo",
            new XAttribute("showFirstColumn", table.ShowFirstColumn ? "1" : "0"),
            new XAttribute("showLastColumn", table.ShowLastColumn ? "1" : "0"),
            new XAttribute("showRowStripes", table.ShowRowStripes ? "1" : "0"),
            new XAttribute("showColumnStripes", table.ShowColumnStripes ? "1" : "0"));
        if (!string.IsNullOrWhiteSpace(table.StyleName))
            element.SetAttributeValue("name", table.StyleName);

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, table.NativeStyleInfoAttributes);

        foreach (var nativeChildXml in table.NativeStyleInfoChildXmls ?? [])
        {
            TryAddNativeTableElement(element, nativeChildXml, workbookNs);
        }

        return element;
    }

    private static bool ShouldWriteStyleInfo(StructuredTableModel table) =>
        !string.IsNullOrWhiteSpace(table.StyleName) ||
        table.ShowFirstColumn ||
        table.ShowLastColumn ||
        table.ShowRowStripes ||
        table.ShowColumnStripes ||
        (table.NativeStyleInfoAttributes?.Count > 0) ||
        (table.NativeStyleInfoChildXmls?.Count > 0);

    private static XElement ToAutoFilterXml(
        StructuredTableModel table,
        XNamespace workbookNs,
        SheetId sheetId,
        IReadOnlyDictionary<(SheetId SheetId, int TableId, int ColumnId), int>? colorFilterDxfIds)
    {
        var element = AddAutoFilterNativeMetadata(new XElement(
            workbookNs + "autoFilter",
            new XAttribute("ref", GetAutoFilterRange(table).ToString()),
            table.FilterColumns.Select(filterColumn => ToFilterColumnXml(
                filterColumn,
                workbookNs,
                colorFilterDxfIds is not null &&
                colorFilterDxfIds.TryGetValue((sheetId, table.Id, filterColumn.ColumnId), out var allocatedDxfId)
                    ? allocatedDxfId
                    : null))),
            table,
            workbookNs);
        XlsxWorksheetAutoFilterNormalizer.NormalizeElement(element);
        return element;
    }

    /// <summary>
    /// The table's own <c>ref</c> legitimately spans header+data+totals rows, but Excel scopes
    /// <c>&lt;autoFilter ref&gt;</c> to the header+data rows only, excluding the totals row -- so
    /// filtering never hides or misinterprets the totals row as ordinary filterable data.
    /// </summary>
    private static GridRange GetAutoFilterRange(StructuredTableModel table)
    {
        if (!table.TotalsRowShown)
            return table.Range;

        var totalsRowCount = Math.Max(1, table.TotalsRowCount ?? 1);
        var start = table.Range.Start;
        var end = table.Range.End;
        if (end.Row <= start.Row)
            return table.Range;

        var clampedEndRow = Math.Max(start.Row, end.Row - (uint)totalsRowCount);
        return clampedEndRow == end.Row
            ? table.Range
            : new GridRange(start, new CellAddress(end.Sheet, clampedEndRow, end.Col));
    }

    private static XElement AddAutoFilterNativeMetadata(
        XElement element,
        StructuredTableModel table,
        XNamespace workbookNs)
    {
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, table.NativeAutoFilterAttributes);

        foreach (var nativeChildXml in table.NativeAutoFilterChildXmls ?? [])
        {
            TryAddNativeTableElement(element, nativeChildXml, workbookNs, "filterColumn");
        }

        return element;
    }

    private static XElement ToFilterColumnXml(
        StructuredTableFilterColumnModel filterColumn,
        XNamespace workbookNs,
        int? allocatedColorFilterDxfId)
    {
        var element = new XElement(
            workbookNs + "filterColumn",
            new XAttribute("colId", filterColumn.ColumnId.ToString(CultureInfo.InvariantCulture)));
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, filterColumn.NativeAttributes);

        var hasCustomFilters = filterColumn.CustomFilters.Count > 0;
        if (!hasCustomFilters && (filterColumn.Values.Count > 0 || filterColumn.IncludeBlank || filterColumn.DateGroups.Count > 0))
        {
            element.Add(new XElement(
                workbookNs + "filters",
                filterColumn.IncludeBlank ? new XAttribute("blank", "1") : null,
                filterColumn.Values.Select(value => new XElement(workbookNs + "filter", new XAttribute("val", value))),
                // R111-io-structured-table-dategroup-roundtrip-1: re-emit dateGroupItem children so a
                // table column carrying Excel's Year/Quarter/Month/Day date-checklist criterion
                // (StructuredTableFilterColumnModel.DateGroups) round-trips back into the same
                // <filters> element it was read from, mirroring
                // XlsxWorksheetAutoFilterXmlMapper.ToFilterColumnXml's identical DateGroups emission
                // for the sheet-level AutoFilter path.
                filterColumn.DateGroups.Select(dateGroup => XlsxAutoFilterXmlCodec.WriteDateGroupItem(dateGroup, workbookNs))));
        }

        if (hasCustomFilters)
        {
            var customFilters = new XElement(
                workbookNs + "customFilters",
                filterColumn.CustomFilters.Select(filter => ToCustomFilterXml(filter, workbookNs)));
            if (filterColumn.CustomFiltersAndRaw is not null)
                customFilters.SetAttributeValue("and", filterColumn.CustomFiltersAndRaw);
            else if (filterColumn.CustomFiltersAnd)
                customFilters.SetAttributeValue("and", "1");

            XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(customFilters, filterColumn.NativeCustomFiltersAttributes);

            element.Add(customFilters);
        }

        // R107-commands-autofilter-table-color-sync-1: mirrors XlsxWorksheetAutoFilterXmlMapper's own
        // hasColorFilter branch -- a command-driven Filter-by-Cell/Font-Colour/No-Fill criterion on a
        // table column. Excluded from "colorFilter" below in the NativeFilterXmls passthrough loop the
        // same way "filters"/"customFilters" already are.
        // R111-io-structured-table-colorfilter-roundtrip-1: XlsxStructuredTableMetadataReader now
        // populates ColorFilter from a loaded file's <colorFilter> element too (mirroring
        // XlsxAutoFilterXmlCodec.ReadColorFilter) and excludes "colorFilter" from
        // NativeFilterXmls, so this branch -- not the passthrough loop below -- is what re-emits a
        // round-tripped colorFilter. That keeps ColorFilter as the single source of truth: never
        // dropped (this branch fires whenever it is set, loaded or fresh) and never duplicated (the
        // passthrough loop below never sees a "colorFilter" element to re-add).
        if (!hasCustomFilters && filterColumn.ColorFilter is { } colorFilter)
            element.Add(XlsxAutoFilterXmlCodec.WriteColorFilter(colorFilter, workbookNs, allocatedColorFilterDxfId));

        foreach (var nativeFilterXml in filterColumn.NativeFilterXmls)
        {
            TryAddNativeTableElement(element, nativeFilterXml, workbookNs, "filters", "customFilters", "colorFilter");
        }

        return element;
    }

    private static XElement ToCustomFilterXml(StructuredTableCustomFilterModel filter, XNamespace workbookNs)
    {
        var element = new XElement(workbookNs + "customFilter");
        if (filter.Operator is not null)
            element.SetAttributeValue("operator", filter.Operator);
        if (filter.Value is not null)
            element.SetAttributeValue("val", filter.Value);

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, filter.NativeAttributes);

        return element;
    }

    private static int ExtractTrailingNumber(string text)
    {
        var start = text.Length;
        while (start > 0 && char.IsDigit(text[start - 1]))
        {
            start--;
        }

        var value = 0;
        for (var index = start; index < text.Length; index++)
        {
            var digit = text[index] - '0';
            if (value > (int.MaxValue - digit) / 10)
                return 1;

            value = (value * 10) + digit;
        }

        return value > 0
            ? value
            : 1;
    }

    private static void TryAddNativeTableElement(
        XElement target,
        string? xml,
        XNamespace workbookNs,
        params string[] excludedLocalNames)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return;

        try
        {
            var element = XElement.Parse(xml);
            if (element.Name.Namespace != workbookNs || excludedLocalNames.Contains(element.Name.LocalName))
                return;

            target.Add(element);
        }
        catch
        {
            // Ignore malformed native table payloads from older saves.
        }
    }

}
