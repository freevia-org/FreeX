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

                // Cap whole-sheet default-width ranges (e.g. ClosedXML's min="1" max="16384" carrier)
                // down to the modelled range so they don't expand into thousands of spurious <col>
                // entries. A run that carries real hidden/outline/collapsed state must keep its full
                // min..max span regardless of where the modelled widths fall, or that state would be
                // silently truncated to a single column.
                if (!HasMeaningfulColumnAttributes(col))
                    max = Math.Min(max, Math.Max(min, maxModelColumn));
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

            // ClosedXML stamps a style index on every <col> it emits (the default style "0" on plain
            // columns, a real index when the column carries cell formatting). The loader treats a styled
            // column at a near-default width (<= 9.2) as a styling-only carrier and discards its width.
            // FreeX does not model a per-column style, so any style here is ClosedXML's stamp: drop it on
            // a genuinely-modelled width that falls in that carrier band so the width is never mistaken
            // for a carrier and round-trips intact (e.g. a narrow 1.71 / 5.71 gutter, or a real 8.14 /
            // 8.71 column). Columns wider than the band keep their stamped style — the loader keeps those
            // widths regardless, so a real column style still survives there.
            if (col.Attribute("style") is not null && width <= ColumnWidthCarrierBandMax)
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

        var mergedRuns = MergeAdjacentIdenticalRuns(columns);
        var newCols = mergedRuns.Count > 0 ? new XElement(ns + "cols", mergedRuns) : null;
        root.Element(ns + "cols")?.Remove();
        if (newCols is not null)
            InsertColsElement(root, ns, newCols);
        return true;
    }

    // Excel (and ClosedXML's own writer) always emits <cols> as compact, non-overlapping min..max runs;
    // the per-column expansion above (needed to stamp exact widths/attributes onto individual columns)
    // must be coalesced back into runs before writing, or a uniform-width multi-column selection (e.g. a
    // ribbon "Column Width" action over a full header selection) turns into thousands of singleton
    // min==max <col> entries instead of one compact run.
    private static List<XElement> MergeAdjacentIdenticalRuns(SortedDictionary<uint, XElement> columns)
    {
        var result = new List<XElement>();
        XElement? runTemplate = null;
        uint runMin = 0, runMax = 0;

        foreach (var (colNum, col) in columns)
        {
            if (runTemplate is not null && colNum == runMax + 1 && HasIdenticalNonRangeAttributes(runTemplate, col))
            {
                runMax = colNum;
                continue;
            }

            if (runTemplate is not null)
                result.Add(BuildRunElement(runTemplate, runMin, runMax));

            runTemplate = col;
            runMin = colNum;
            runMax = colNum;
        }

        if (runTemplate is not null)
            result.Add(BuildRunElement(runTemplate, runMin, runMax));

        return result;
    }

    private static XElement BuildRunElement(XElement template, uint min, uint max)
    {
        var clone = new XElement(template);
        clone.SetAttributeValue("min", min.ToString(CultureInfo.InvariantCulture));
        clone.SetAttributeValue("max", max.ToString(CultureInfo.InvariantCulture));
        return clone;
    }

    private static bool HasIdenticalNonRangeAttributes(XElement a, XElement b)
    {
        var aAttrs = a.Attributes().Where(attr => attr.Name.LocalName is not ("min" or "max"))
            .ToDictionary(attr => attr.Name.LocalName, attr => attr.Value);
        var bAttrs = b.Attributes().Where(attr => attr.Name.LocalName is not ("min" or "max"))
            .ToDictionary(attr => attr.Name.LocalName, attr => attr.Value);
        if (aAttrs.Count != bAttrs.Count)
            return false;
        foreach (var (key, value) in aAttrs)
        {
            if (!bAttrs.TryGetValue(key, out var otherValue) || otherValue != value)
                return false;
        }
        return true;
    }

    // The upper bound of the near-default "carrier" width band the loader
    // (XlsxWorksheetRowColumnLayoutReader.ReadColumnLayout) uses to discard styling-only columns. A
    // genuinely-modelled width at or below this must have its ClosedXML-stamped style dropped on save so
    // the loader does not mistake it for a carrier. Keep in sync with the loader's threshold.
    private const double ColumnWidthCarrierBandMax = 9.2;

    // A <col> is worth keeping (without a width) only if it still carries hidden, outline, collapsed,
    // or a non-default (non-"0") style. A bare min/max (or default style="0") entry is dropped.
    private static bool HasMeaningfulColumnAttributes(XElement col)
    {
        if (XlsxWorksheetXmlValueParser.IsTruthy(col.Attribute("hidden")?.Value) ||
            XlsxWorksheetXmlValueParser.IsTruthy(col.Attribute("collapsed")?.Value))
            return true;
        if (int.TryParse(col.Attribute("outlineLevel")?.Value, out var level) && level > 0)
            return true;
        var style = col.Attribute("style")?.Value;
        return !string.IsNullOrEmpty(style) && style != "0";
    }

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
