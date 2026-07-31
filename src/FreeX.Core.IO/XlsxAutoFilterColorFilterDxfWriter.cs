using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// R89-io-autofilter-color-dxf-1-1: allocates a workbook-level &lt;dxfs&gt; entry for every AutoFilter
/// "Filter by Cell Colour"/"Filter by Font Colour"/"Filter by No Fill" column that does not already
/// carry a dxfId (native passthrough for an already-dxfId'd colour filter -- e.g. one read from a file
/// Excel wrote -- is left completely alone; see <see cref="XlsxWorksheetAutoFilterXmlMapper"/>'s
/// existing DifferentialFormatId/DifferentialFormatIdRaw passthrough). This is the round-trip gap the
/// round-87 colour-filter work explicitly deferred: without an allocator, FreeX had no way to turn a
/// chosen fill/font colour into the `dxfId` OOXML actually requires (`&lt;colorFilter dxfId="N"
/// cellColor="1"/&gt;`). dxfId is a REQUIRED attribute on CT_ColorFilter (confirmed by
/// XlsxNonChartSchemaValidationTests' real-schema validation), so even "No Fill" -- which has no actual
/// colour to record -- still needs one; it gets an empty &lt;dxf/&gt; (no font/fill/border at all),
/// which resolves back to `Color: null` on reload exactly like today's colourless model.
/// </summary>
internal static class XlsxAutoFilterColorFilterDxfWriter
{
    public static bool HasUnallocatedColorFilters(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.AutoFilter is null)
                continue;

            foreach (var column in sheet.AutoFilter.FilterColumns)
            {
                if (NeedsAllocation(column.ColorFilter))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Allocates dxfs (mutating xl/styles.xml in <paramref name="archive"/>) for every AutoFilter colour
    /// filter that needs one, and returns the allocated index keyed by (sheet, column). Filters that
    /// already carry a native dxfId (e.g. read from a file Excel wrote) are left alone entirely and
    /// never appear in the returned map -- <see cref="XlsxWorksheetAutoFilterXmlMapper"/>'s own
    /// passthrough logic keeps handling that case unchanged.
    /// </summary>
    public static IReadOnlyDictionary<(SheetId SheetId, int ColumnId), int> Save(
        ZipArchive archive,
        Workbook workbook,
        XNamespace workbookNs)
    {
        var pending = new List<(SheetId SheetId, int ColumnId, CellStyle Style)>();
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.AutoFilter is null)
                continue;

            foreach (var column in sheet.AutoFilter.FilterColumns)
            {
                var colorFilter = column.ColorFilter;
                if (!NeedsAllocation(colorFilter))
                    continue;

                var style = ToDxfStyle(colorFilter!);
                pending.Add((sheet.Id, column.ColumnId, style));
            }
        }

        if (pending.Count == 0)
            return new Dictionary<(SheetId, int), int>();

        var stylesEntry = archive.GetEntry("xl/styles.xml");
        var stylesXml = stylesEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(stylesEntry)
            : new XDocument(new XElement(workbookNs + "styleSheet"));
        var root = stylesXml.Root;
        if (root is null)
            return new Dictionary<(SheetId, int), int>();

        var dxfs = XlsxDifferentialStyleAllocator.GetOrCreateDxfsElement(root, workbookNs);
        var nextNumFmtId = XlsxDifferentialStyleAllocator.ComputeNextCustomNumFmtId(root, dxfs, workbookNs);

        var result = new Dictionary<(SheetId, int), int>();
        foreach (var (sheetId, columnId, style) in pending)
        {
            // A plain fill/font colour never sets a NumberFormat, so ToDifferentialStyleXml never
            // actually emits a <numFmt> here -- nextNumFmtId is passed only so the shared builder's
            // signature matches the conditional-format writer's; it is not consumed for these dxfs.
            var dxfXml = XlsxAdvancedConditionalFormatWriter.ToDifferentialStyleXml(style, workbookNs, nextNumFmtId);
            var index = XlsxDifferentialStyleAllocator.AllocateOrReuse(dxfs, dxfXml, workbookNs);
            result[(sheetId, columnId)] = index;
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        return result;
    }

    /// <summary>
    /// R107-commands-autofilter-table-color-sync-1: same allocation as <see cref="Save"/>, but for
    /// structured tables' own <see cref="StructuredTableFilterColumnModel.ColorFilter"/> (a table
    /// carries its own &lt;autoFilter&gt; inside the table part rather than a worksheet-level one, so
    /// it needs its own dxf allocation pass -- <see cref="XlsxStructuredTableWriter"/> calls this
    /// itself, before writing any table part, so the caller need not coordinate ordering). Keyed by
    /// (sheet, table, column) rather than just (sheet, column) since multiple tables on the same sheet
    /// each number their own columns from 0, so the same ColumnId can legitimately collide across
    /// tables.
    /// </summary>
    public static bool HasUnallocatedStructuredTableColorFilters(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                foreach (var column in table.FilterColumns)
                {
                    if (NeedsAllocation(column.ColorFilter))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>See <see cref="HasUnallocatedStructuredTableColorFilters"/>. Mirrors <see cref="Save"/>'s allocation logic exactly, appending into the SAME workbook-level &lt;dxfs&gt; element.</summary>
    public static IReadOnlyDictionary<(SheetId SheetId, int TableId, int ColumnId), int> SaveForStructuredTables(
        ZipArchive archive,
        Workbook workbook,
        XNamespace workbookNs)
    {
        var pending = new List<(SheetId SheetId, int TableId, int ColumnId, CellStyle Style)>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                foreach (var column in table.FilterColumns)
                {
                    var colorFilter = column.ColorFilter;
                    if (!NeedsAllocation(colorFilter))
                        continue;

                    var style = ToDxfStyle(colorFilter!);
                    pending.Add((sheet.Id, table.Id, column.ColumnId, style));
                }
            }
        }

        if (pending.Count == 0)
            return new Dictionary<(SheetId, int, int), int>();

        var stylesEntry = archive.GetEntry("xl/styles.xml");
        var stylesXml = stylesEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(stylesEntry)
            : new XDocument(new XElement(workbookNs + "styleSheet"));
        var root = stylesXml.Root;
        if (root is null)
            return new Dictionary<(SheetId, int, int), int>();

        var dxfs = XlsxDifferentialStyleAllocator.GetOrCreateDxfsElement(root, workbookNs);
        var nextNumFmtId = XlsxDifferentialStyleAllocator.ComputeNextCustomNumFmtId(root, dxfs, workbookNs);

        var result = new Dictionary<(SheetId, int, int), int>();
        foreach (var (sheetId, tableId, columnId, style) in pending)
        {
            var dxfXml = XlsxAdvancedConditionalFormatWriter.ToDifferentialStyleXml(style, workbookNs, nextNumFmtId);
            var index = XlsxDifferentialStyleAllocator.AllocateOrReuse(dxfs, dxfXml, workbookNs);
            result[(sheetId, tableId, columnId)] = index;
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        return result;
    }

    private static bool NeedsAllocation(WorksheetAutoFilterColorFilterModel? colorFilter) =>
        colorFilter is not null &&
        colorFilter.DifferentialFormatIdRaw is null &&
        colorFilter.DifferentialFormatId is null;

    // colorFilter.Color is null for "No Fill" (CellNoFillColorFilterCommand never sets one) --
    // CellStyle.Default there yields a childless <dxf/>, which is schema-valid and resolves back to
    // Color: null on reload since it defines no fill/font color at all.
    private static CellStyle ToDxfStyle(WorksheetAutoFilterColorFilterModel colorFilter) =>
        colorFilter.Color is not { } color
            ? CellStyle.Default
            : colorFilter.CellColor
                ? new CellStyle { FillColor = color, FillPatternStyle = CellFillPatternStyle.Solid }
                : new CellStyle { FontColor = color };
}
