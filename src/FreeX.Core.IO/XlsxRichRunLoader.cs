using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Reads per-run rich-text formatting from an XLSX package and populates
/// <see cref="Sheet.RichTextRuns"/> for each sheet in the workbook.
/// </summary>
/// <remarks>
/// Two sources are handled:
/// <list type="bullet">
///   <item>
///     <b>Inline-string cells</b> (<c>t="inlineStr"</c>): the <c>&lt;is&gt;&lt;r&gt;…</c>
///     elements live directly inside the worksheet XML.
///   </item>
///   <item>
///     <b>Shared-string cells</b> (<c>t="s"</c>): the run formatting lives in
///     <c>xl/sharedStrings.xml</c>; cells reference entries by zero-based index via
///     their <c>&lt;v&gt;</c> value.
///   </item>
/// </list>
/// This is a separate read-only pass over the package (not the ClosedXML object model)
/// so it is invisible to formulas, calculations, and number-format.
/// </remarks>
internal static class XlsxRichRunLoader
{
    private const string SharedStringsPath = "xl/sharedStrings.xml";
    private static readonly XNamespace WorkbookNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Loads rich-text runs from <paramref name="xlsxStream"/> and writes them into
    /// the matching <see cref="Sheet.RichTextRuns"/> dictionaries of <paramref name="workbook"/>.
    /// No-ops silently if the archive has no shared-string or no inline-string rich text.
    /// </summary>
    public static void Load(
        Stream xlsxStream,
        Workbook workbook,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        xlsxStream.Position = 0;
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);

            // 1. Build SST rich-run map (index → runs/phonetic-guide | null).
            var sstEntries = LoadSstRichRuns(archive, theme, indexedColors);

            // 2. Resolve worksheet → sheet, then scan each worksheet XML.
            var sheetByPath = BuildSheetPathMap(archive, workbook);

            foreach (var (worksheetPath, sheet) in sheetByPath)
                LoadWorksheetRichRuns(archive, worksheetPath, sheet, sstEntries, theme, indexedColors);
        }
        catch
        {
            // Rich-text load is best-effort: any failure leaves RichTextRuns empty.
        }
    }

    // ── SST ─────────────────────────────────────────────────────────────────

    /// <summary>Per-shared-string-index rich-run runs and phonetic-guide passthrough, if any.</summary>
    private readonly record struct SstRichEntry(
        IReadOnlyList<CellTextRun>? Runs,
        CellPhoneticGuide? PhoneticGuide);

    private static IReadOnlyList<SstRichEntry>? LoadSstRichRuns(
        ZipArchive archive,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var entry = archive.GetEntry(SharedStringsPath);
        if (entry is null) return null;

        XDocument doc;
        doc = OpcXml.LoadXml(entry);

        var root = doc.Root;
        if (root is null) return null;

        var siElements = root.Elements(WorkbookNs + "si").ToList();
        if (siElements.Count == 0) return null;

        var result = new List<SstRichEntry>(siElements.Count);
        var anyRich = false;

        foreach (var si in siElements)
        {
            var runs = XlsxRichRunReader.ReadRuns(si, WorkbookNs, theme, indexedColors);
            var phoneticGuide = XlsxRichRunReader.ReadPhoneticGuide(si, WorkbookNs);
            result.Add(new SstRichEntry(runs, phoneticGuide));
            if (runs is not null || phoneticGuide is not null)
                anyRich = true;
        }

        return anyRich ? result : null;
    }

    // ── Sheet path resolution ────────────────────────────────────────────────

    private static Dictionary<string, Sheet> BuildSheetPathMap(ZipArchive archive, Workbook workbook)
    {
        var map = new Dictionary<string, Sheet>(StringComparer.OrdinalIgnoreCase);

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry     = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null) return map;

        XDocument workbookXml, relsXml;
        workbookXml = OpcXml.LoadXml(workbookEntry);
        relsXml     = OpcXml.LoadXml(relsEntry);

        var relTargets = XlsxRelationshipReader.ReadTargets(
            relsXml,
            PackageRelNs,
            XlsxPackagePath.NormalizeWorkbookTarget);

        foreach (var sheetElement in workbookXml.Root?.Element(WorkbookNs + "sheets")
                     ?.Elements(WorkbookNs + "sheet") ?? [])
        {
            var name  = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(RelNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId)) continue;
            if (!relTargets.TryGetValue(relId, out var path)) continue;

            var sheet = workbook.GetSheet(name);
            if (sheet is null) continue;

            map[path] = sheet;
        }

        return map;
    }

    // ── Per-worksheet pass ───────────────────────────────────────────────────

    private static void LoadWorksheetRichRuns(
        ZipArchive archive,
        string worksheetPath,
        Sheet sheet,
        IReadOnlyList<SstRichEntry>? sstEntries,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var entry = archive.GetEntry(worksheetPath);
        if (entry is null) return;

        XDocument doc;
        doc = OpcXml.LoadXml(entry);

        var sheetData = doc.Root?.Element(WorkbookNs + "sheetData");
        if (sheetData is null) return;

        var rowName  = WorkbookNs + "row";
        var cellName = WorkbookNs + "c";
        var isName   = WorkbookNs + "is";
        var vName    = WorkbookNs + "v";

        foreach (var rowElement in sheetData.Elements(rowName))
        {
            if (!uint.TryParse(rowElement.Attribute("r")?.Value,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNum))
                continue;

            foreach (var cellElement in rowElement.Elements(cellName))
            {
                var cellRef = cellElement.Attribute("r")?.Value;
                if (string.IsNullOrWhiteSpace(cellRef) ||
                    !CellAddress.TryParse(cellRef, sheet.Id, out var addr))
                    continue;

                var cellType = cellElement.Attribute("t")?.Value;

                if (string.Equals(cellType, "inlineStr", StringComparison.OrdinalIgnoreCase))
                {
                    // Inline-string: runs live directly in <is>.
                    var isEl = cellElement.Element(isName);
                    if (isEl is null) continue;

                    var runs = XlsxRichRunReader.ReadRuns(isEl, WorkbookNs, theme, indexedColors);
                    if (runs is not null)
                        sheet.RichTextRuns[addr] = runs;

                    var phoneticGuide = XlsxRichRunReader.ReadPhoneticGuide(isEl, WorkbookNs);
                    if (phoneticGuide is not null)
                        sheet.CellPhoneticGuides[addr] = phoneticGuide;
                }
                else if (string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase) &&
                         sstEntries is not null)
                {
                    // Shared-string: look up the SST index.
                    var vEl = cellElement.Element(vName);
                    if (vEl is null) continue;
                    if (!int.TryParse(vEl.Value, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var sstIndex) ||
                        sstIndex < 0 || sstIndex >= sstEntries.Count)
                        continue;

                    var sstEntry = sstEntries[sstIndex];
                    if (sstEntry.Runs is not null)
                        sheet.RichTextRuns[addr] = sstEntry.Runs;
                    if (sstEntry.PhoneticGuide is not null)
                        sheet.CellPhoneticGuides[addr] = sstEntry.PhoneticGuide;
                }
            }
        }
    }
}
