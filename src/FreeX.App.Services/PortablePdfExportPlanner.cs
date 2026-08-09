using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum PortablePdfExportPlanStatus
{
    Ready,
    ExportPrintPlanNotReady,
    OutputKindUnavailable,
    InvalidPageGrid
}

public sealed record PortablePdfExportPageSpans(
    IReadOnlyList<uint> TitleRows,
    IReadOnlyList<uint> BodyRows,
    IReadOnlyList<uint> TitleColumns,
    IReadOnlyList<uint> BodyColumns);

public sealed record PortablePdfExportPageRequest(
    int ExportPageNumber,
    int SheetIndex,
    string SheetName,
    int SheetPageNumber,
    GridRange PrintRange,
    WorkbookExportPrintRangeSource RangeSource,
    int RowPageIndex,
    int ColumnPageIndex,
    int RowPageCount,
    int ColumnPageCount,
    PortablePdfExportPageSpans PageSpans,
    WorksheetPageOrder PageOrder,
    IReadOnlyList<WorksheetDisplayedComment>? DisplayedComments = null,
    bool IsCommentSummaryPage = false,
    IReadOnlyList<PrintCommentSummaryEntry>? CommentSummaryEntries = null)
{
    public int RowPageNumber => RowPageIndex + 1;

    public int ColumnPageNumber => ColumnPageIndex + 1;

    public IReadOnlyList<uint> TitleRows => PageSpans.TitleRows;

    public IReadOnlyList<uint> BodyRows => PageSpans.BodyRows;

    public IReadOnlyList<uint> TitleColumns => PageSpans.TitleColumns;

    public IReadOnlyList<uint> BodyColumns => PageSpans.BodyColumns;

    /// <summary>
    /// "As displayed" comment overlays (Sheet.PrintComments == AsDisplayed) anchored to cells on this
    /// grid page, in page-relative row/column index order. Empty for grid pages when the sheet's
    /// comments setting isn't AsDisplayed, and always empty for comment-summary pages (see
    /// <see cref="IsCommentSummaryPage"/>).
    /// </summary>
    public IReadOnlyList<WorksheetDisplayedComment> DisplayedComments { get; init; } =
        DisplayedComments ?? [];

    /// <summary>
    /// The paginated "at end of sheet" comment summary entries for this page when
    /// <see cref="IsCommentSummaryPage"/> is true (Sheet.PrintComments == AtEnd); empty otherwise.
    /// </summary>
    public IReadOnlyList<PrintCommentSummaryEntry> CommentSummaryEntries { get; init; } =
        CommentSummaryEntries ?? [];
}

public sealed record PortablePdfExportPlan(
    PortablePdfExportPlanStatus Status,
    string StatusText,
    WorkbookExportPrintPlan ExportPrintPlan,
    IReadOnlyList<PortablePdfExportPageRequest> PageRequests)
{
    public bool IsReady => Status == PortablePdfExportPlanStatus.Ready;

    public int TotalPageCount => PageRequests.Count;
}

public static class PortablePdfExportPlanner
{
    public static bool TryApplyOptions(
        PortablePdfExportPlan exportPlan,
        ExportOptions options,
        out PortablePdfExportPlan effectivePlan,
        out string? error,
        ExportPlannerTextResolver? textResolver = null)
    {
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentNullException.ThrowIfNull(options);

        effectivePlan = exportPlan;
        if (!ExportPlanner.TryValidatePublishOptions(options, ExportFormat.Pdf, out error, textResolver))
            return false;

        if (!ExportPlanner.TryValidatePageRange(options.PageRange, exportPlan.TotalPageCount, out error, textResolver))
            return false;

        effectivePlan = ApplyPageRange(exportPlan, options.PageRange);
        return true;
    }

    public static PortablePdfExportPlan ApplyPageRange(
        PortablePdfExportPlan exportPlan,
        ExportPageRange? pageRange)
    {
        ArgumentNullException.ThrowIfNull(exportPlan);
        if (pageRange is null)
            return exportPlan;

        var pageRequests = exportPlan.PageRequests
            .Where(page => page.ExportPageNumber >= pageRange.FromPage && page.ExportPageNumber <= pageRange.ToPage)
            .Select((page, index) => page with { ExportPageNumber = index + 1 })
            .ToArray();
        return exportPlan with
        {
            PageRequests = pageRequests,
            StatusText = $"Ready to export portable PDF: {pageRequests.Length} {(pageRequests.Length == 1 ? "page" : "pages")} from selected page range."
        };
    }

    /// <summary>
    /// Builds the portable PDF export plan. When <paramref name="workbook"/> is supplied, each
    /// sheet's <see cref="Sheet.PrintComments"/> setting is honored the same way the WPF
    /// PrintRenderer does: "As displayed" attaches cell-anchored comment overlays to the grid pages
    /// that contain them, and "At end of sheet" appends extra comment-summary page requests after
    /// that sheet's grid pages (see <see cref="PrintCommentSummaryPlanner"/>). Without a workbook
    /// (legacy callers), comments are omitted exactly as before.
    /// </summary>
    public static PortablePdfExportPlan CreatePlan(WorkbookExportPrintPlan exportPrintPlan, Workbook? workbook = null)
    {
        ArgumentNullException.ThrowIfNull(exportPrintPlan);

        if (!exportPrintPlan.IsReady)
        {
            return new PortablePdfExportPlan(
                PortablePdfExportPlanStatus.ExportPrintPlanNotReady,
                $"Portable PDF export cannot start because the export print plan is not ready: {exportPrintPlan.StatusText}",
                exportPrintPlan,
                []);
        }

        if (exportPrintPlan.Intent.OutputKind != WorkbookExportPrintOutputKind.Pdf)
        {
            return new PortablePdfExportPlan(
                PortablePdfExportPlanStatus.OutputKindUnavailable,
                "Portable PDF export only accepts PDF export print plans; XPS remains Windows-only.",
                exportPrintPlan,
                []);
        }

        if (!HasValidPageGrid(exportPrintPlan))
        {
            return new PortablePdfExportPlan(
                PortablePdfExportPlanStatus.InvalidPageGrid,
                "Portable PDF export requires positive row and column page counts for every planned sheet.",
                exportPrintPlan,
                []);
        }

        var pageRequests = BuildPageRequests(exportPrintPlan.SheetPlans, workbook);
        return new PortablePdfExportPlan(
            PortablePdfExportPlanStatus.Ready,
            $"Ready to export portable PDF: {pageRequests.Count} {Pluralize(pageRequests.Count, "page")} across {exportPrintPlan.SheetPlans.Count} {Pluralize(exportPrintPlan.SheetPlans.Count, "sheet")}.",
            exportPrintPlan,
            pageRequests);
    }

    private static bool HasValidPageGrid(WorkbookExportPrintPlan exportPrintPlan)
    {
        if (exportPrintPlan.SheetPlans.Count == 0)
            return false;

        foreach (var sheetPlan in exportPrintPlan.SheetPlans)
        {
            if (sheetPlan.RowPageCount <= 0 ||
                sheetPlan.ColumnPageCount <= 0 ||
                sheetPlan.RowPagePlans.Count != sheetPlan.RowPageCount ||
                sheetPlan.ColumnPagePlans.Count != sheetPlan.ColumnPageCount ||
                sheetPlan.PageCount != (long)sheetPlan.RowPageCount * sheetPlan.ColumnPageCount)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<PortablePdfExportPageRequest> BuildPageRequests(
        IReadOnlyList<WorkbookSheetExportPrintPlanSummary> sheetPlans,
        Workbook? workbook)
    {
        var pageRequests = new List<PortablePdfExportPageRequest>();
        for (var sheetIndex = 0; sheetIndex < sheetPlans.Count; sheetIndex++)
            AddSheetPageRequests(sheetPlans[sheetIndex], sheetIndex, workbook, pageRequests);

        return pageRequests;
    }

    private static void AddSheetPageRequests(
        WorkbookSheetExportPrintPlanSummary sheetPlan,
        int sheetIndex,
        Workbook? workbook,
        List<PortablePdfExportPageRequest> pageRequests)
    {
        // The plan's flattened sheetIndex is the print AREA's position within the export, not the
        // sheet's own index in the workbook (a sheet can contribute more than one print area) --
        // resolve the actual Sheet by the SheetId the print range belongs to instead (matches
        // WorkbookPdfContentBuilder's N45/N46 resolution).
        var sheet = workbook?.GetSheet(sheetPlan.PrintRange.Start.Sheet);

        foreach (var page in PrintPageGridPlanner.Build(
                     sheetPlan.RowPagePlans,
                     sheetPlan.ColumnPagePlans,
                     sheetPlan.PageOrder))
            AddPageRequest(sheetPlan, sheetIndex, sheet, page, pageRequests);

        if (sheet is { PrintComments: WorksheetPrintComments.AtEnd })
            AddCommentSummaryPageRequests(sheetPlan, sheetIndex, sheet, pageRequests);
    }

    private static void AddPageRequest(
        WorkbookSheetExportPrintPlanSummary sheetPlan,
        int sheetIndex,
        Sheet? sheet,
        PrintPageGridEntry page,
        List<PortablePdfExportPageRequest> pageRequests)
    {
        var displayedComments = sheet is { PrintComments: WorksheetPrintComments.AsDisplayed }
            ? WorksheetPageLayout.GetDisplayedCommentOverlays(
                sheet.Comments,
                sheet.ThreadedComments,
                CombineSpan(page.RowPlan.TitleRows, page.RowPlan.BodyRows),
                CombineSpan(page.ColumnPlan.TitleColumns, page.ColumnPlan.BodyColumns),
                sheet.ShownComments)
            : [];

        pageRequests.Add(new PortablePdfExportPageRequest(
            pageRequests.Count + 1,
            sheetIndex,
            sheetPlan.SheetName,
            page.SheetPageNumber,
            sheetPlan.PrintRange,
            sheetPlan.RangeSource,
            page.RowPageIndex,
            page.ColumnPageIndex,
            sheetPlan.RowPageCount,
            sheetPlan.ColumnPageCount,
            new PortablePdfExportPageSpans(
                page.RowPlan.TitleRows.ToArray(),
                page.RowPlan.BodyRows.ToArray(),
                page.ColumnPlan.TitleColumns.ToArray(),
                page.ColumnPlan.BodyColumns.ToArray()),
            sheetPlan.PageOrder,
            DisplayedComments: displayedComments));
    }

    /// <summary>
    /// Appends "at end of sheet" comment-summary page requests for one sheet, mirroring
    /// PrintRenderer's AddCommentSummaryPage: paginate every note/threaded-comment on the sheet via
    /// <see cref="PrintCommentSummaryPlanner.BuildPages"/> and add one page request per resulting
    /// summary page, right after that sheet's grid pages.
    /// </summary>
    private static void AddCommentSummaryPageRequests(
        WorkbookSheetExportPrintPlanSummary sheetPlan,
        int sheetIndex,
        Sheet sheet,
        List<PortablePdfExportPageRequest> pageRequests)
    {
        var (_, pageHeightPt) = SheetPdfPageSetupResolver.ResolvePageSizePoints(sheet);
        var marginTopPt = sheet.PageMargins.Top * SheetPdfPageSetupResolver.PdfPointsPerInch;
        var summaryPages = PrintCommentSummaryPlanner.BuildPages(
            sheet.Comments,
            sheet.ThreadedComments,
            pageHeightPt,
            marginTopPt);

        var emptySpans = new PortablePdfExportPageSpans([], [], [], []);
        foreach (var summaryPage in summaryPages)
        {
            pageRequests.Add(new PortablePdfExportPageRequest(
                pageRequests.Count + 1,
                sheetIndex,
                sheetPlan.SheetName,
                sheetPlan.PageCount + summaryPage.PageIndex + 1,
                sheetPlan.PrintRange,
                sheetPlan.RangeSource,
                sheetPlan.RowPageCount,
                sheetPlan.ColumnPageCount,
                sheetPlan.RowPageCount,
                sheetPlan.ColumnPageCount,
                emptySpans,
                sheetPlan.PageOrder,
                IsCommentSummaryPage: true,
                CommentSummaryEntries: summaryPage.Entries));
        }
    }

    private static IReadOnlyList<uint> CombineSpan(IReadOnlyList<uint> title, IReadOnlyList<uint> body)
    {
        if (title.Count == 0)
            return body;
        if (body.Count == 0)
            return title;

        var combined = new List<uint>(title.Count + body.Count);
        combined.AddRange(title);
        combined.AddRange(body);
        return combined;
    }

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";
}
