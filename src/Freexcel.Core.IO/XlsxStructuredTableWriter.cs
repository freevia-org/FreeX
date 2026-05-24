using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
        var tablePartIndex = 1;

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
                    ? $"xl/tables/table{tablePartIndex}.xml"
                    : table.PackagePart.TrimStart('/').Replace('\\', '/');
                if (!tablePath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase))
                    tablePath = $"xl/tables/table{tablePartIndex}.xml";

                tablePartIndex++;
                XlsxPackageXmlEditor.ReplaceXml(archive, tablePath, ToXml(table, tablePath));
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
            worksheetRoot.Add(new XElement(
                workbookNs + "tableParts",
                new XAttribute("count", tableParts.Count.ToString(CultureInfo.InvariantCulture)),
                tableParts));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static XDocument ToXml(StructuredTableModel table, string tablePath)
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
        foreach (var (name, value) in table.NativeAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && root.Attribute(name) is null)
                root.SetAttributeValue(name, value);
        }

        if (table.HasAutoFilter)
            root.Add(ToAutoFilterXml(table, workbookNs));
        if (!string.IsNullOrWhiteSpace(table.NativeSortStateXml))
        {
            try
            {
                var sortState = XElement.Parse(table.NativeSortStateXml);
                if (sortState.Name == workbookNs + "sortState")
                    root.Add(sortState);
            }
            catch
            {
                // Ignore malformed native table sort payloads from older saves.
            }
        }
        root.Add(new XElement(
            workbookNs + "tableColumns",
            new XAttribute("count", columns.Count.ToString(CultureInfo.InvariantCulture)),
            columns.Select(column => ToColumnXml(column, workbookNs))));
        if (!string.IsNullOrWhiteSpace(table.StyleName))
            root.Add(ToStyleInfoXml(table, workbookNs));
        foreach (var nativeChildXml in (table.NativeChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == workbookNs)
                    root.Add(nativeChild);
            }
            catch
            {
                // Ignore malformed native table payloads from older saves.
            }
        }

        return new XDocument(root);
    }

    private static XElement ToColumnXml(StructuredTableColumnModel column, XNamespace workbookNs)
    {
        var element = new XElement(
            workbookNs + "tableColumn",
            new XAttribute("id", column.Id),
            new XAttribute("name", column.Name),
            string.IsNullOrWhiteSpace(column.TotalsRowLabel) ? null : new XAttribute("totalsRowLabel", column.TotalsRowLabel),
            string.IsNullOrWhiteSpace(column.TotalsRowFunction) ? null : new XAttribute("totalsRowFunction", column.TotalsRowFunction),
            string.IsNullOrWhiteSpace(column.CalculatedColumnFormula)
                ? null
                : new XElement(workbookNs + "calculatedColumnFormula", column.CalculatedColumnFormula),
            string.IsNullOrWhiteSpace(column.TotalsRowFormula)
                ? null
                : new XElement(workbookNs + "totalsRowFormula", column.TotalsRowFormula));

        foreach (var (name, value) in column.NativeAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && element.Attribute(name) is null)
                element.SetAttributeValue(name, value);
        }

        foreach (var nativeChildXml in (column.NativeChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == workbookNs &&
                    nativeChild.Name.LocalName is not "calculatedColumnFormula" and not "totalsRowFormula")
                {
                    element.Add(nativeChild);
                }
            }
            catch
            {
                // Ignore malformed native table-column payloads from older saves.
            }
        }

        return element;
    }

    private static XElement ToStyleInfoXml(StructuredTableModel table, XNamespace workbookNs)
    {
        var element = new XElement(
            workbookNs + "tableStyleInfo",
            new XAttribute("name", table.StyleName!),
            new XAttribute("showFirstColumn", table.ShowFirstColumn ? "1" : "0"),
            new XAttribute("showLastColumn", table.ShowLastColumn ? "1" : "0"),
            new XAttribute("showRowStripes", table.ShowRowStripes ? "1" : "0"),
            new XAttribute("showColumnStripes", table.ShowColumnStripes ? "1" : "0"));

        foreach (var (name, value) in table.NativeStyleInfoAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && element.Attribute(name) is null)
                element.SetAttributeValue(name, value);
        }

        foreach (var nativeChildXml in (table.NativeStyleInfoChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == workbookNs)
                    element.Add(nativeChild);
            }
            catch
            {
                // Ignore malformed native table-style payloads from older saves.
            }
        }

        return element;
    }

    private static XElement ToAutoFilterXml(StructuredTableModel table, XNamespace workbookNs) =>
        AddAutoFilterNativeMetadata(new XElement(
            workbookNs + "autoFilter",
            new XAttribute("ref", table.Range.ToString()),
            table.FilterColumns.Select(filterColumn => ToFilterColumnXml(filterColumn, workbookNs))),
            table,
            workbookNs);

    private static XElement AddAutoFilterNativeMetadata(
        XElement element,
        StructuredTableModel table,
        XNamespace workbookNs)
    {
        foreach (var (name, value) in table.NativeAutoFilterAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && element.Attribute(name) is null)
                element.SetAttributeValue(name, value);
        }

        foreach (var nativeChildXml in (table.NativeAutoFilterChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == workbookNs && nativeChild.Name.LocalName != "filterColumn")
                    element.Add(nativeChild);
            }
            catch
            {
                // Ignore malformed native table AutoFilter payloads from older saves.
            }
        }

        return element;
    }

    private static XElement ToFilterColumnXml(StructuredTableFilterColumnModel filterColumn, XNamespace workbookNs)
    {
        var element = new XElement(
            workbookNs + "filterColumn",
            new XAttribute("colId", filterColumn.ColumnId.ToString(CultureInfo.InvariantCulture)));
        foreach (var (name, value) in filterColumn.NativeAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && element.Attribute(name) is null)
                element.SetAttributeValue(name, value);
        }

        if (filterColumn.Values.Count > 0 || filterColumn.IncludeBlank)
        {
            element.Add(new XElement(
                workbookNs + "filters",
                filterColumn.IncludeBlank ? new XAttribute("blank", "1") : null,
                filterColumn.Values.Select(value => new XElement(workbookNs + "filter", new XAttribute("val", value)))));
        }

        foreach (var nativeFilterXml in filterColumn.NativeFilterXmls.Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeFilter = XElement.Parse(nativeFilterXml);
                if (nativeFilter.Name.Namespace == workbookNs && nativeFilter.Name.LocalName != "filters")
                    element.Add(nativeFilter);
            }
            catch
            {
                // Ignore malformed native filter payloads from older saves; value filters above remain valid.
            }
        }

        return element;
    }

    private static int ExtractTrailingNumber(string text)
    {
        var digits = new string(text.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 1;
    }
}
