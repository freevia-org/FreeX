using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

// Writes exact column widths into each worksheet's <cols>. ClosedXML's Column.Width setter applies a
// character-width conversion that inflates the stored width (e.g. 2.0 -> 2.71), so a round-trip no
// longer matches the source. This post-pass overrides the width (and the customWidth flag) of each
// modelled column with the exact value, preserving the other attributes (hidden, style, outlineLevel)
// ClosedXML already emitted.
internal static class XlsxWorksheetColumnWidthWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = XlsxPackageXmlEditor.LoadXml(relsEntry).Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in XlsxPackageXmlEditor.LoadXml(workbookEntry).Root?.Element(ns + "sheets")?.Elements(ns + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.ColumnWidths.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            if (worksheetXml.Root is { } root && ApplyExactColumnWidths(root, ns, sheet))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static bool ApplyExactColumnWidths(XElement root, XNamespace ns, Sheet sheet)
    {
        var maxModelColumn = sheet.ColumnWidths.Keys.Where(c => c >= 1 && c <= CellAddress.MaxCol).DefaultIfEmpty(1u).Max();

        // Expand existing <col> entries to per-column, preserving their attributes (style/hidden/outline).
        var columns = new SortedDictionary<uint, XElement>();
        if (root.Element(ns + "cols") is { } existingCols)
        {
            foreach (var col in existingCols.Elements(ns + "col"))
            {
                if (!uint.TryParse(col.Attribute("min")?.Value, out var min) ||
                    !uint.TryParse(col.Attribute("max")?.Value, out var max) ||
                    min == 0 || max < min)
                {
                    continue;
                }

                max = Math.Min(max, Math.Max(min, maxModelColumn)); // cap whole-sheet default ranges
                for (var c = min; c <= max; c++)
                {
                    var clone = new XElement(col);
                    clone.SetAttributeValue("min", c.ToString(CultureInfo.InvariantCulture));
                    clone.SetAttributeValue("max", c.ToString(CultureInfo.InvariantCulture));
                    columns[c] = clone;
                }
            }
        }

        // The model is authoritative for column widths. Set exact widths on modelled columns, and
        // strip the width/customWidth that ClosedXML stamps on style-carrier columns (cell formatting,
        // no real width) so they don't round-trip as spurious widths. Columns that still carry styling,
        // hidden, or outline information are kept; otherwise the now-empty <col> is dropped.
        foreach (var (colNum, width) in sheet.ColumnWidths)
        {
            if (colNum == 0 || colNum > CellAddress.MaxCol || !double.IsFinite(width) || width <= 0)
                continue;

            if (!columns.TryGetValue(colNum, out var col))
            {
                col = new XElement(ns + "col",
                    new XAttribute("min", colNum.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("max", colNum.ToString(CultureInfo.InvariantCulture)));
                columns[colNum] = col;
            }

            col.SetAttributeValue("width", width.ToString("0.################", CultureInfo.InvariantCulture));
            col.SetAttributeValue("customWidth", "1");

            // ClosedXML stamps the default style (index 0) on every <col>; the loader discards a styled
            // near-default width as a styling-only entry, so drop the default style here to let a genuine
            // modelled width round-trip. A real, non-default column style is preserved.
            if (col.Attribute("style")?.Value == "0")
                col.SetAttributeValue("style", null);
        }

        foreach (var (colNum, col) in columns.ToList())
        {
            if (sheet.ColumnWidths.ContainsKey(colNum))
                continue;

            col.SetAttributeValue("width", null);
            col.SetAttributeValue("customWidth", null);
            if (!HasMeaningfulColumnAttributes(col))
                columns.Remove(colNum);
        }

        var newCols = columns.Count > 0 ? new XElement(ns + "cols", columns.Values) : null;
        root.Element(ns + "cols")?.Remove();
        if (newCols is not null)
            InsertColsElement(root, ns, newCols);
        return true;
    }

    // A <col> is worth keeping (without a width) only if it still carries hidden, outline, collapsed,
    // or a non-default (non-"0") style. A bare min/max (or default style="0") entry is dropped.
    private static bool HasMeaningfulColumnAttributes(XElement col)
    {
        if (IsTruthy(col.Attribute("hidden")?.Value) || IsTruthy(col.Attribute("collapsed")?.Value))
            return true;
        if (int.TryParse(col.Attribute("outlineLevel")?.Value, out var level) && level > 0)
            return true;
        var style = col.Attribute("style")?.Value;
        return !string.IsNullOrEmpty(style) && style != "0";
    }

    private static bool IsTruthy(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    // CT_Worksheet order: ...sheetViews, sheetFormatPr, cols, sheetData...
    private static void InsertColsElement(XElement root, XNamespace ns, XElement cols)
    {
        if (root.Element(ns + "sheetData") is { } sheetData)
        {
            sheetData.AddBeforeSelf(cols);
            return;
        }

        var anchor = root.Element(ns + "sheetFormatPr") ?? root.Element(ns + "sheetViews") ?? root.Element(ns + "dimension");
        if (anchor is not null)
            anchor.AddAfterSelf(cols);
        else
            root.AddFirst(cols);
    }
}
