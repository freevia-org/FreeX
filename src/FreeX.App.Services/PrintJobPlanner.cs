using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// What range of the document's pages to send to the printer.
/// </summary>
public enum PrintJobPageRangeKind
{
    /// <summary>Print every page produced by the chosen scope.</summary>
    AllPages,

    /// <summary>Print only the inclusive <c>[FromPage, ToPage]</c> subset of the produced pages.</summary>
    PageRange,
}

/// <summary>
/// Why a requested print job could not be turned into a ready-to-spool plan.
/// </summary>
public enum PrintJobValidationStatus
{
    Ready,

    /// <summary>The underlying export-print plan was not ready (no printable content / unsupported output).</summary>
    DocumentNotPrintable,

    /// <summary>The requested number of copies was below 1.</summary>
    InvalidCopyCount,

    /// <summary>A page range was requested but <c>FromPage</c>/<c>ToPage</c> were not a valid 1-based subset.</summary>
    InvalidPageRange,
}

/// <summary>
/// A framework-neutral description of a print request: which scope to render, how many copies,
/// whether to collate, and which produced pages to actually send. This is the shared seam macOS,
/// Linux, and the Windows desktop all build their OS print call from, so all selection/validation
/// logic lives here (the rendering shell only collects the user's choices and renders the picked
/// pages to a print-ready document).
/// </summary>
public sealed record PrintJobRequest(
    WorkbookExportPrintScope Scope,
    int Copies = 1,
    bool Collate = true,
    PrintJobPageRangeKind PageRangeKind = PrintJobPageRangeKind.AllPages,
    int? FromPage = null,
    int? ToPage = null,
    int? ActiveSheetIndex = null,
    GridRange? SelectedRange = null,
    bool IgnorePrintAreas = false);

/// <summary>
/// The validated, ready-to-spool plan for a print job. Carries the underlying page plan (so the shell
/// knows exactly which pages exist and how many there are) plus the resolved 1-based page window and
/// copy/collate settings the platform print path consumes.
/// </summary>
public sealed record PrintJobPlan(
    PrintJobRequest Request,
    WorkbookExportPrintPlan ExportPrintPlan,
    PrintJobValidationStatus ValidationStatus,
    string StatusText,
    int Copies,
    bool Collate,
    int FirstPage,
    int LastPage)
{
    public bool IsReady => ValidationStatus == PrintJobValidationStatus.Ready;

    /// <summary>Total pages the chosen scope produces (across all sheets), before any page-range trim.</summary>
    public int TotalPageCount => ExportPrintPlan.TotalPageCount;

    /// <summary>Pages actually sent to the printer (inclusive window), before copy multiplication.</summary>
    public int SelectedPageCount => IsReady ? Math.Max(0, LastPage - FirstPage + 1) : 0;

    /// <summary>Total sheets of paper this job emits: selected pages times copies.</summary>
    public int TotalSheetsToPrint => SelectedPageCount * Copies;
}

/// <summary>
/// Turns a <see cref="PrintJobRequest"/> into a validated <see cref="PrintJobPlan"/>. Reuses the existing
/// <see cref="WorkbookExportPrintPlanner"/> for scope/page planning, then layers print-only concerns on
/// top: a copy count of at least one, an optional 1-based page-range window clamped to the produced pages,
/// and collation. Pure data shaping — no I/O, no platform dependency — so every platform inherits identical
/// behaviour and it is fully unit-testable headlessly.
/// </summary>
public static class PrintJobPlanner
{
    /// <summary>
    /// Creates a print job plan where pagination is derived from each sheet's own page setup (paper
    /// size, orientation, margins, scale-to-fit, and actual row/column sizes). Prefer this overload
    /// for the Avalonia/Skia PDF export path to ensure pages match Excel's layout.
    /// </summary>
    public static PrintJobPlan CreatePlanFromPageSetup(
        Workbook workbook,
        PrintJobRequest request,
        WorkbookExportPrintSurface? surface = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(request);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                request.Scope,
                WorkbookExportPrintOutputKind.Pdf,
                request.ActiveSheetIndex,
                request.SelectedRange,
                request.IgnorePrintAreas),
            surface);

        return BuildJobPlan(request, exportPlan);
    }

    public static PrintJobPlan CreatePlan(
        Workbook workbook,
        PrintJobRequest request,
        WorkbookExportPrintPageCapacity pageCapacity,
        WorkbookExportPrintSurface? surface = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pageCapacity);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                request.Scope,
                WorkbookExportPrintOutputKind.Pdf,
                request.ActiveSheetIndex,
                request.SelectedRange,
                request.IgnorePrintAreas),
            pageCapacity,
            surface);

        return BuildJobPlan(request, exportPlan);
    }

    private static PrintJobPlan BuildJobPlan(PrintJobRequest request, WorkbookExportPrintPlan exportPlan)
    {
        if (!exportPlan.IsReady)
        {
            return Invalid(
                request,
                exportPlan,
                PrintJobValidationStatus.DocumentNotPrintable,
                exportPlan.StatusText);
        }

        if (request.Copies < 1)
        {
            return Invalid(
                request,
                exportPlan,
                PrintJobValidationStatus.InvalidCopyCount,
                "Print requires at least one copy.");
        }

        var totalPages = exportPlan.TotalPageCount;
        if (!TryResolvePageWindow(request, totalPages, out var firstPage, out var lastPage))
        {
            return Invalid(
                request,
                exportPlan,
                PrintJobValidationStatus.InvalidPageRange,
                $"Enter a page range between 1 and {totalPages}.");
        }

        return new PrintJobPlan(
            request,
            exportPlan,
            PrintJobValidationStatus.Ready,
            FormatReadyStatus(request, firstPage, lastPage, totalPages),
            request.Copies,
            request.Collate,
            firstPage,
            lastPage);
    }

    private static bool TryResolvePageWindow(
        PrintJobRequest request,
        int totalPages,
        out int firstPage,
        out int lastPage)
    {
        // The 1-based page-window math is framework-neutral and lives in the shared resolver so FreeP/FreeW
        // inherit identical "all pages" / clamped-range / open-ended-extends / reject-out-of-bounds behaviour.
        var window = request.PageRangeKind == PrintJobPageRangeKind.AllPages
            ? PrintPageWindowResolver.ResolveAllPages(totalPages)
            : PrintPageWindowResolver.ResolveRange(request.FromPage, request.ToPage, totalPages);

        firstPage = window.FirstPage;
        lastPage = window.LastPage;
        return window.IsValid;
    }

    private static PrintJobPlan Invalid(
        PrintJobRequest request,
        WorkbookExportPrintPlan exportPlan,
        PrintJobValidationStatus status,
        string statusText) =>
        new(request, exportPlan, status, statusText, Copies: 0, Collate: request.Collate, FirstPage: 0, LastPage: 0);

    private static string FormatReadyStatus(
        PrintJobRequest request,
        int firstPage,
        int lastPage,
        int totalPages)
    {
        var pageCount = lastPage - firstPage + 1;
        var pages = request.PageRangeKind == PrintJobPageRangeKind.AllPages || (firstPage == 1 && lastPage == totalPages)
            ? $"all {totalPages} {Pluralize(totalPages, "page")}"
            : $"{Pluralize(pageCount, "page")} {firstPage}-{lastPage} of {totalPages}";

        var copies = request.Copies == 1
            ? "1 copy"
            : $"{request.Copies} copies ({(request.Collate ? "collated" : "uncollated")})";

        return $"Ready to print {pages}, {copies}.";
    }

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";
}
