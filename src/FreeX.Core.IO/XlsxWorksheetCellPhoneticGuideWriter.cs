using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// R78-selfreg-twin-sweep-1: the full-save (ClosedXML) path never reads
/// <see cref="Sheet.CellPhoneticGuides"/> anywhere -- <c>ApplyRichTextRuns</c> writes only
/// bold/italic/etc. runs via ClosedXML's <c>IXLRichText</c> API, which has no concept of a
/// phonetic guide (furigana). Left unfixed, any full ClosedXML rewrite (a Save-As, or falling
/// back from an ineligible patch-save) silently drops every cell's <c>&lt;rPh&gt;</c>/
/// <c>&lt;phoneticPr&gt;</c> markup, even for a cell whose text/formatting never changed.
/// </summary>
/// <remarks>
/// Runs as a post-processing pass over the freshly-produced package (ClosedXML has already
/// finished serialising cells): for every cell address that <see cref="Sheet.CellPhoneticGuides"/>
/// still carries a guide for, rewrites that ONE cell as an inline string
/// (<c>t="inlineStr"</c>) built via <see cref="XlsxRichRunWriter.CreateRichInlineStringElement"/> --
/// the same emitter the incremental patch-save path uses -- so the guide's <c>&lt;rPh&gt;</c>/
/// <c>&lt;phoneticPr&gt;</c> XML is re-emitted in schema order after the run(s).
/// <para>
/// Converting to a private inline string (rather than patching the cell's shared-string entry in
/// place) sidesteps ClosedXML's shared-string deduplication entirely: two cells with identical
/// text but only one of them carrying a guide would otherwise risk one cell's phonetic markup
/// leaking onto the other via a shared <c>&lt;si&gt;</c> entry.
/// </para>
/// <para>
/// A cell already carrying phonetic markup -- either because it is already an inline string with
/// its own <c>&lt;rPh&gt;</c>/<c>&lt;phoneticPr&gt;</c>, or because its shared-string entry already
/// has one (e.g. restored verbatim by <see cref="XlsxSharedStringMetadataPreserver"/> on the
/// source-package save path) -- is left completely untouched, keeping an already-correct save
/// byte-stable.
/// </para>
/// </remarks>
internal static class XlsxWorksheetCellPhoneticGuideWriter
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Save(Stream packageStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        List<XElement>? sharedStrings = null;
        var sharedStringsLoaded = false;

        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.CellPhoneticGuides.Count == 0)
                continue;

            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var sheetData = worksheetXml.Root?.Element(WorksheetNs + "sheetData");
            if (sheetData is null)
                continue;

            var cellsByReference = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var cellElement in sheetData.Elements(WorksheetNs + "row").Elements(WorksheetNs + "c"))
            {
                var reference = cellElement.Attribute("r")?.Value;
                if (!string.IsNullOrEmpty(reference))
                    cellsByReference[reference] = cellElement;
            }

            var changed = false;
            foreach (var (address, guide) in sheet.CellPhoneticGuides)
            {
                if (!cellsByReference.TryGetValue(address.ToA1(), out var cellElement))
                    continue;

                if (ApplyPhoneticGuideToCell(cellElement, address, sheet, guide, archive, ref sharedStrings, ref sharedStringsLoaded))
                    changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static bool ApplyPhoneticGuideToCell(
        XElement cellElement,
        CellAddress address,
        Sheet sheet,
        CellPhoneticGuide guide,
        ZipArchive archive,
        ref List<XElement>? sharedStrings,
        ref bool sharedStringsLoaded)
    {
        var typeAttr = cellElement.Attribute("t")?.Value;

        if (string.Equals(typeAttr, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            var existingIs = cellElement.Element(WorksheetNs + "is");
            if (existingIs is not null && HasPhoneticMarkup(existingIs))
                return false; // Already carries this cell's own phonetic guide verbatim.
        }
        else if (string.Equals(typeAttr, "s", StringComparison.OrdinalIgnoreCase))
        {
            var vEl = cellElement.Element(WorksheetNs + "v");
            if (vEl is not null &&
                int.TryParse(vEl.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sstIndex))
            {
                if (!sharedStringsLoaded)
                {
                    sharedStrings = LoadSharedStrings(archive);
                    sharedStringsLoaded = true;
                }

                if (sharedStrings is not null && sstIndex >= 0 && sstIndex < sharedStrings.Count &&
                    HasPhoneticMarkup(sharedStrings[sstIndex]))
                {
                    // The shared-string entry this cell references already carries a phonetic
                    // guide (e.g. restored verbatim by XlsxSharedStringMetadataPreserver on the
                    // source-package save path) -- leave it alone.
                    return false;
                }
            }
        }

        var runs = sheet.RichTextRuns.TryGetValue(address, out var richRuns) && richRuns.Count > 0
            ? richRuns
            : BuildPlainRunFallback(sheet, address);
        if (runs is null)
            return false;

        var newIs = XlsxRichRunWriter.CreateRichInlineStringElement(WorksheetNs, runs, guide);

        cellElement.Element(WorksheetNs + "is")?.Remove();
        cellElement.Element(WorksheetNs + "v")?.Remove();
        cellElement.SetAttributeValue("t", "inlineStr");
        cellElement.Add(newIs);
        return true;
    }

    private static bool HasPhoneticMarkup(XElement richStringOrIs) =>
        richStringOrIs.Elements(WorksheetNs + "rPh").Any() ||
        richStringOrIs.Element(WorksheetNs + "phoneticPr") is not null;

    private static List<XElement>? LoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return null;

        var doc = XlsxPackageXmlEditor.LoadXml(entry);
        return doc.Root?.Elements(WorksheetNs + "si").ToList();
    }

    // A cell can carry a phonetic guide without any rich-run formatting at all (the common case:
    // plain furigana with no bold/italic on the base text), in which case XlsxRichRunLoader never
    // populated Sheet.RichTextRuns for this address (ReadRuns requires at least one <r> child).
    // Reconstruct a single plain (all-null formatting) run from the cell's own current text value
    // so the guide still has somewhere to attach.
    private static IReadOnlyList<CellTextRun>? BuildPlainRunFallback(Sheet sheet, CellAddress address)
    {
        var cell = sheet.GetCell(address.Row, address.Col);
        return cell?.Value is TextValue text
            ? new List<CellTextRun> { new(text.Value, null, null, null, null, null, null, null) }
            : null;
    }
}
