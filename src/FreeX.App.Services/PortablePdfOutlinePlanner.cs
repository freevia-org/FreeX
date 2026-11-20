using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class PortablePdfOutlinePlanner
{
    /// <summary>
    /// Builds bookmarks in exported page order. Filtered requests retain their original page
    /// labels and identify whether the first page of each worksheet survived the filter.
    /// </summary>
    public static IReadOnlyList<PdfOutlineEntry> Build(
        Workbook workbook,
        IReadOnlyList<PortablePdfExportPageRequest> exportedPages,
        PdfBookmarkMode mode)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportedPages);

        if (mode == PdfBookmarkMode.None || exportedPages.Count == 0)
            return [];

        var outlines = new List<PdfOutlineEntry>();
        var seenSheets = new HashSet<SheetId>();
        for (var index = 0; index < exportedPages.Count; index++)
        {
            var page = exportedPages[index];
            var sheetId = page.PrintRange.Start.Sheet;
            // If the worksheet's first page was filtered out, no remaining page takes its place.
            if (mode != PdfBookmarkMode.PageNumbers &&
                (page.OriginalSheetPagination is { PageIndex: > 0 } || !seenSheets.Add(sheetId)))
            {
                continue;
            }

            var title = mode == PdfBookmarkMode.PageNumbers
                ? $"Page {page.OriginalDocumentPagination?.PageNumber ?? page.ExportPageNumber}"
                : BuildSheetTitle(workbook.GetSheet(sheetId), page.SheetName, mode);
            if (!string.IsNullOrWhiteSpace(title))
                outlines.Add(new PdfOutlineEntry(title.Trim(), index));
        }

        return outlines;
    }

    private static string BuildSheetTitle(Sheet? sheet, string plannedName, PdfBookmarkMode mode)
    {
        if (sheet is null)
            return plannedName;

        if (mode != PdfBookmarkMode.PrintTitles)
            return sheet.Name;

        var parts = new List<string>();
        if (sheet.PrintTitleRows is { } rows)
            parts.Add(rows.Start == rows.End ? $"Rows {rows.Start}" : $"Rows {rows.Start}-{rows.End}");
        if (sheet.PrintTitleColumns is { } columns)
            parts.Add(columns.Start == columns.End ? $"Columns {columns.Start}" : $"Columns {columns.Start}-{columns.End}");

        return parts.Count == 0
            ? sheet.Name
            : $"{sheet.Name} ({string.Join(", ", parts)})";
    }
}
