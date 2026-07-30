using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum WorkbookExportPrintOutputKind
{
    Pdf,
    Xps
}

public enum WorkbookExportPrintScope
{
    ActiveSheet,
    SelectedRange,
    VisibleWorkbook
}

public enum WorkbookExportPrintRangeSource
{
    SelectedRange,
    PrintArea,
    UsedRange
}

public enum WorkbookExportPrintValidationStatus
{
    Ready,
    ExportUnavailable,
    OutputKindUnavailable,
    SelectedRangeRequired,
    SelectedRangeUnavailable,
    ActiveSheetUnavailable,
    InvalidPageCapacity,
    NoPrintableRanges
}

public sealed record WorkbookExportPrintSurface(
    string Label,
    bool SupportsPdf = true,
    bool SupportsXps = false)
{
    public static WorkbookExportPrintSurface PortablePdf { get; } = new("Portable PDF");
    public static WorkbookExportPrintSurface WindowsDesktop { get; } = new("Windows desktop", SupportsXps: true);
    public static WorkbookExportPrintSurface MacOs { get; } = new("macOS");

    // App-specific output-kind vocabulary maps onto the neutral shared capability core, so the
    // label normalization and supported-kind selection live in one place reusable by FreeP/FreeW.
    private ExportSurfaceCapability Capability { get; } =
        new(Label, SupportsPdf, SupportsXps);

    public string Label { get; init; } = ExportSurfaceCapability.Normalize(Label);

    public IReadOnlyList<WorkbookExportPrintOutputKind> SupportedOutputKinds
    {
        get
        {
            var kinds = Capability.SupportedKinds;
            var outputKinds = new List<WorkbookExportPrintOutputKind>(kinds.Count);
            foreach (var kind in kinds)
                outputKinds.Add(FromDocumentKind(kind));

            return outputKinds;
        }
    }

    public bool Supports(WorkbookExportPrintOutputKind outputKind) =>
        Capability.Supports(ToDocumentKind(outputKind));

    private static ExportDocumentKind ToDocumentKind(WorkbookExportPrintOutputKind outputKind) =>
        outputKind switch
        {
            WorkbookExportPrintOutputKind.Xps => ExportDocumentKind.Xps,
            _ => ExportDocumentKind.Pdf
        };

    private static WorkbookExportPrintOutputKind FromDocumentKind(ExportDocumentKind kind) =>
        kind switch
        {
            ExportDocumentKind.Xps => WorkbookExportPrintOutputKind.Xps,
            _ => WorkbookExportPrintOutputKind.Pdf
        };
}

public sealed record WorkbookExportPrintIntent(
    WorkbookExportPrintScope Scope,
    WorkbookExportPrintOutputKind OutputKind,
    int? ActiveSheetIndex = null,
    GridRange? SelectedRange = null,
    bool IgnorePrintAreas = false);

public sealed record WorkbookExportPrintPageCapacity(
    uint RowsPerPage,
    uint ColumnsPerPage);

public sealed record WorkbookSheetExportPrintPlanSummary(
    string SheetName,
    GridRange PrintRange,
    WorkbookExportPrintRangeSource RangeSource,
    int RowPageCount,
    int ColumnPageCount,
    int PageCount,
    IReadOnlyList<PrintPageRowPlan> RowPagePlans,
    IReadOnlyList<PrintPageColumnPlan> ColumnPagePlans,
    WorksheetPageOrder PageOrder)
{
    public uint RowCount => PrintRange.RowCount;
    public uint ColumnCount => PrintRange.ColCount;
}

public sealed record WorkbookExportPrintPlan(
    WorkbookExportPrintIntent Intent,
    WorkbookExportPrintSurface Surface,
    WorkbookExportReadinessPlan ExportReadiness,
    WorkbookExportPrintValidationStatus ValidationStatus,
    string StatusText,
    IReadOnlyList<WorkbookExportPrintOutputKind> SupportedOutputKinds,
    IReadOnlyList<WorkbookSheetExportPrintPlanSummary> SheetPlans)
{
    public bool IsReady =>
        ExportReadiness.IsReady &&
        ValidationStatus == WorkbookExportPrintValidationStatus.Ready;

    public int TotalPageCount => SheetPlans.Sum(sheet => sheet.PageCount);
}

public static class WorkbookExportPrintPlanner
{
    /// <summary>
    /// Creates an export print plan where the row/column page capacity is derived from each sheet's
    /// own page setup (paper size, orientation, margins, scale-to-fit, and actual row/column sizes)
    /// via <see cref="SheetPdfPageSetupResolver.ResolveCapacity"/>. This is the page-setup-aware
    /// path — prefer it over the fixed-capacity overload for PDF export.
    /// </summary>
    public static WorkbookExportPrintPlan CreatePlanFromPageSetup(
        Workbook workbook,
        WorkbookExportPrintIntent intent,
        WorkbookExportPrintSurface? surface = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(intent);

        surface ??= WorkbookExportPrintSurface.PortablePdf;
        var supportedOutputKinds = surface.SupportedOutputKinds;
        var readiness = WorkbookExportReadinessPlanner.Create(
            workbook,
            hasSelection: intent.SelectedRange.HasValue);

        if (!readiness.IsReady)
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                WorkbookExportPrintValidationStatus.ExportUnavailable,
                readiness.StatusText,
                supportedOutputKinds,
                []);
        }

        if (!surface.Supports(intent.OutputKind))
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                WorkbookExportPrintValidationStatus.OutputKindUnavailable,
                FormatUnsupportedOutputStatus(surface, intent.OutputKind, supportedOutputKinds),
                supportedOutputKinds,
                []);
        }

        var requestedRanges = ResolveRequestedRanges(workbook, intent, out var invalidStatus, out var invalidStatusText);
        if (invalidStatus is not null)
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                invalidStatus.Value,
                invalidStatusText,
                supportedOutputKinds,
                []);
        }

        var sheetPlans = BuildSheetPlansFromPageSetup(requestedRanges);
        if (sheetPlans.Count == 0)
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                WorkbookExportPrintValidationStatus.NoPrintableRanges,
                "No printable sheet ranges were found for the requested export scope.",
                supportedOutputKinds,
                sheetPlans);
        }

        return CreatePlan(
            intent,
            surface,
            readiness,
            WorkbookExportPrintValidationStatus.Ready,
            FormatReadyStatus(surface, intent, sheetPlans.Count, sheetPlans.Sum(sheet => sheet.PageCount)),
            supportedOutputKinds,
            sheetPlans);
    }

    public static WorkbookExportPrintPlan CreatePlan(
        Workbook workbook,
        WorkbookExportPrintIntent intent,
        WorkbookExportPrintPageCapacity pageCapacity,
        WorkbookExportPrintSurface? surface = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(pageCapacity);

        surface ??= WorkbookExportPrintSurface.PortablePdf;
        var supportedOutputKinds = surface.SupportedOutputKinds;
        var readiness = WorkbookExportReadinessPlanner.Create(
            workbook,
            hasSelection: intent.SelectedRange.HasValue);

        if (!readiness.IsReady)
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                WorkbookExportPrintValidationStatus.ExportUnavailable,
                readiness.StatusText,
                supportedOutputKinds,
                []);
        }

        if (!surface.Supports(intent.OutputKind))
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                WorkbookExportPrintValidationStatus.OutputKindUnavailable,
                FormatUnsupportedOutputStatus(surface, intent.OutputKind, supportedOutputKinds),
                supportedOutputKinds,
                []);
        }

        if (pageCapacity.RowsPerPage == 0 || pageCapacity.ColumnsPerPage == 0)
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                WorkbookExportPrintValidationStatus.InvalidPageCapacity,
                "Export print planning requires at least one row and one column per page.",
                supportedOutputKinds,
                []);
        }

        var requestedRanges = ResolveRequestedRanges(workbook, intent, out var invalidStatus, out var invalidStatusText);
        if (invalidStatus is not null)
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                invalidStatus.Value,
                invalidStatusText,
                supportedOutputKinds,
                []);
        }

        var sheetPlans = BuildSheetPlans(requestedRanges, pageCapacity);
        if (sheetPlans.Count == 0)
        {
            return CreatePlan(
                intent,
                surface,
                readiness,
                WorkbookExportPrintValidationStatus.NoPrintableRanges,
                "No printable sheet ranges were found for the requested export scope.",
                supportedOutputKinds,
                sheetPlans);
        }

        return CreatePlan(
            intent,
            surface,
            readiness,
            WorkbookExportPrintValidationStatus.Ready,
            FormatReadyStatus(surface, intent, sheetPlans.Count, sheetPlans.Sum(sheet => sheet.PageCount)),
            supportedOutputKinds,
            sheetPlans);
    }

    private static WorkbookExportPrintPlan CreatePlan(
        WorkbookExportPrintIntent intent,
        WorkbookExportPrintSurface surface,
        WorkbookExportReadinessPlan readiness,
        WorkbookExportPrintValidationStatus status,
        string statusText,
        IReadOnlyList<WorkbookExportPrintOutputKind> supportedOutputKinds,
        IReadOnlyList<WorkbookSheetExportPrintPlanSummary> sheetPlans) =>
        new(
            intent,
            surface,
            readiness,
            status,
            statusText,
            supportedOutputKinds,
            sheetPlans);

    private static IReadOnlyList<SheetRangeRequest> ResolveRequestedRanges(
        Workbook workbook,
        WorkbookExportPrintIntent intent,
        out WorkbookExportPrintValidationStatus? invalidStatus,
        out string invalidStatusText)
    {
        invalidStatus = null;
        invalidStatusText = "";

        return intent.Scope switch
        {
            WorkbookExportPrintScope.SelectedRange =>
                ResolveSelectedRange(workbook, intent.SelectedRange, out invalidStatus, out invalidStatusText),
            WorkbookExportPrintScope.VisibleWorkbook =>
                ResolveVisibleWorkbookRanges(workbook, intent.IgnorePrintAreas),
            _ =>
                ResolveActiveSheetRange(workbook, intent, out invalidStatus, out invalidStatusText)
        };
    }

    private static IReadOnlyList<SheetRangeRequest> ResolveSelectedRange(
        Workbook workbook,
        GridRange? selectedRange,
        out WorkbookExportPrintValidationStatus? invalidStatus,
        out string invalidStatusText)
    {
        invalidStatus = null;
        invalidStatusText = "";

        if (selectedRange is not { } range)
        {
            invalidStatus = WorkbookExportPrintValidationStatus.SelectedRangeRequired;
            invalidStatusText = "Select a range before planning selected-range export.";
            return [];
        }

        var sheet = workbook.GetSheet(range.Start.Sheet);
        if (sheet is null || sheet.IsHidden)
        {
            invalidStatus = WorkbookExportPrintValidationStatus.SelectedRangeUnavailable;
            invalidStatusText = "The selected range does not belong to a visible worksheet in this workbook.";
            return [];
        }

        return [new SheetRangeRequest(sheet, range, WorkbookExportPrintRangeSource.SelectedRange)];
    }

    private static IReadOnlyList<SheetRangeRequest> ResolveVisibleWorkbookRanges(
        Workbook workbook,
        bool ignorePrintAreas)
    {
        var requests = new List<SheetRangeRequest>();
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden)
                continue;

            requests.AddRange(ResolveSheetPrintRanges(sheet, ignorePrintAreas));
        }

        return requests;
    }

    private static IReadOnlyList<SheetRangeRequest> ResolveActiveSheetRange(
        Workbook workbook,
        WorkbookExportPrintIntent intent,
        out WorkbookExportPrintValidationStatus? invalidStatus,
        out string invalidStatusText)
    {
        invalidStatus = null;
        invalidStatusText = "";

        var sheet = ResolveActiveSheet(workbook, intent.ActiveSheetIndex);
        if (sheet is null)
        {
            invalidStatus = WorkbookExportPrintValidationStatus.ActiveSheetUnavailable;
            invalidStatusText = "No visible active worksheet is available for export print planning.";
            return [];
        }

        var ranges = ResolveSheetPrintRanges(sheet, intent.IgnorePrintAreas);
        return ranges.Count > 0 ? ranges : [];
    }

    private static Sheet? ResolveActiveSheet(Workbook workbook, int? requestedIndex)
    {
        if (requestedIndex is { } index)
            return GetVisibleSheetAt(workbook, index);

        if (workbook.ActiveSheetIndex is { } activeIndex &&
            GetVisibleSheetAt(workbook, activeIndex) is { } activeSheet)
        {
            return activeSheet;
        }

        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.IsHidden)
                return sheet;
        }

        return null;
    }

    private static Sheet? GetVisibleSheetAt(Workbook workbook, int sheetIndex)
    {
        if (sheetIndex < 0 || sheetIndex >= workbook.SheetCount)
            return null;

        var sheet = workbook.GetSheetAt(sheetIndex);
        return sheet.IsHidden ? null : sheet;
    }

    private static bool TryResolvePrintRange(
        Sheet sheet,
        bool ignorePrintAreas,
        out GridRange printRange,
        out WorkbookExportPrintRangeSource rangeSource)
    {
        // For single-range callers: use the first area when multiple are configured.
        if (!ignorePrintAreas && sheet.PrintAreas.Count > 0)
        {
            printRange = sheet.PrintAreas[0];
            rangeSource = WorkbookExportPrintRangeSource.PrintArea;
            return true;
        }

        if (sheet.GetUsedRange() is { } usedRange)
        {
            printRange = usedRange;
            rangeSource = WorkbookExportPrintRangeSource.UsedRange;
            return true;
        }

        printRange = default;
        rangeSource = WorkbookExportPrintRangeSource.UsedRange;
        return false;
    }

    /// <summary>
    /// Resolves print ranges for a sheet, returning one <see cref="SheetRangeRequest"/> per configured
    /// print area (each area prints on its own page). Falls back to the used range when no print area
    /// is configured.
    /// </summary>
    private static IReadOnlyList<SheetRangeRequest> ResolveSheetPrintRanges(
        Sheet sheet,
        bool ignorePrintAreas)
    {
        if (!ignorePrintAreas && sheet.PrintAreas.Count > 0)
        {
            var areas = sheet.PrintAreas
                .Where(a => a.Start.Sheet == sheet.Id)
                .Select(a => new SheetRangeRequest(sheet, a, WorkbookExportPrintRangeSource.PrintArea))
                .ToList();
            if (areas.Count > 0)
                return areas;
        }

        if (sheet.GetUsedRange() is { } usedRange)
            return [new SheetRangeRequest(sheet, usedRange, WorkbookExportPrintRangeSource.UsedRange)];

        return [];
    }

    private static IReadOnlyList<WorkbookSheetExportPrintPlanSummary> BuildSheetPlans(
        IReadOnlyList<SheetRangeRequest> requestedRanges,
        WorkbookExportPrintPageCapacity pageCapacity)
    {
        var sheetPlans = new List<WorkbookSheetExportPrintPlanSummary>(requestedRanges.Count);
        foreach (var request in requestedRanges)
        {
            var rowPlans = PrintLayoutPlanner.BuildRowPlans(
                request.PrintRange,
                request.Sheet.PrintTitleRows,
                pageCapacity.RowsPerPage,
                request.Sheet.RowPageBreaks,
                request.Sheet.IsRowEffectivelyHidden);
            var columnPlans = PrintLayoutPlanner.BuildColumnPlans(
                request.PrintRange,
                request.Sheet.PrintTitleColumns,
                pageCapacity.ColumnsPerPage,
                request.Sheet.ColumnPageBreaks,
                request.Sheet.IsColEffectivelyHidden);

            sheetPlans.Add(new WorkbookSheetExportPrintPlanSummary(
                request.Sheet.Name,
                request.PrintRange,
                request.RangeSource,
                rowPlans.Count,
                columnPlans.Count,
                rowPlans.Count * columnPlans.Count,
                rowPlans,
                columnPlans,
                request.Sheet.PageOrder));
        }

        return sheetPlans;
    }

    /// <summary>
    /// Builds per-sheet plans where each sheet's row/column pagination is derived from its own page
    /// setup (paper, orientation, margins, scale, actual row/column sizes) via
    /// <see cref="SheetPdfPageSetupResolver.ResolvePagination"/>.
    ///
    /// <para>
    /// R96-services-print-pagination-exact: pages are sliced on the real ACCUMULATED per-row height /
    /// per-column width (the manual break points <c>ResolvePagination</c> computes), not a fixed count
    /// derived from the AVERAGE row height / column width across the whole print range -- mirroring
    /// what <c>WorksheetPrintRenderPlanner</c> already does for the WPF print path via
    /// <c>PagePaginationPlanner.BuildPlan</c>. A fixed average-based count over/under-shoots the real
    /// printable area whenever the range has non-uniform row heights or column widths (e.g. a few
    /// wrapped-text or picture-anchor rows mixed with many short ones).
    /// </para>
    /// </summary>
    private static IReadOnlyList<WorkbookSheetExportPrintPlanSummary> BuildSheetPlansFromPageSetup(
        IReadOnlyList<SheetRangeRequest> requestedRanges)
    {
        var sheetPlans = new List<WorkbookSheetExportPrintPlanSummary>(requestedRanges.Count);
        foreach (var request in requestedRanges)
        {
            var (pageCapacity, rowBreaks, columnBreaks) =
                SheetPdfPageSetupResolver.ResolvePagination(request.Sheet, request.PrintRange);

            var rowPlans = PrintLayoutPlanner.BuildRowPlans(
                request.PrintRange,
                request.Sheet.PrintTitleRows,
                pageCapacity.RowsPerPage,
                rowBreaks,
                request.Sheet.IsRowEffectivelyHidden);
            var columnPlans = PrintLayoutPlanner.BuildColumnPlans(
                request.PrintRange,
                request.Sheet.PrintTitleColumns,
                pageCapacity.ColumnsPerPage,
                columnBreaks,
                request.Sheet.IsColEffectivelyHidden);

            sheetPlans.Add(new WorkbookSheetExportPrintPlanSummary(
                request.Sheet.Name,
                request.PrintRange,
                request.RangeSource,
                rowPlans.Count,
                columnPlans.Count,
                rowPlans.Count * columnPlans.Count,
                rowPlans,
                columnPlans,
                request.Sheet.PageOrder));
        }

        return sheetPlans;
    }

    private static string FormatUnsupportedOutputStatus(
        WorkbookExportPrintSurface surface,
        WorkbookExportPrintOutputKind outputKind,
        IReadOnlyList<WorkbookExportPrintOutputKind> supportedOutputKinds)
    {
        if (supportedOutputKinds.Count == 0)
            return $"{surface.Label} does not support export print planning output kinds.";

        return $"{surface.Label} supports {FormatOutputKinds(supportedOutputKinds)} export print planning; {FormatOutputKind(outputKind)} is not available on this platform.";
    }

    private static string FormatReadyStatus(
        WorkbookExportPrintSurface surface,
        WorkbookExportPrintIntent intent,
        int sheetCount,
        int pageCount) =>
        $"Ready to plan {FormatOutputKind(intent.OutputKind)} export on {surface.Label} for {FormatScope(intent.Scope)}: {sheetCount} {Pluralize(sheetCount, "sheet")} and {pageCount} {Pluralize(pageCount, "page")}.";

    private static string FormatOutputKinds(IReadOnlyList<WorkbookExportPrintOutputKind> outputKinds) =>
        outputKinds.Count == 1
            ? FormatOutputKind(outputKinds[0])
            : string.Join(", ", outputKinds.Select(FormatOutputKind));

    private static string FormatOutputKind(WorkbookExportPrintOutputKind outputKind) =>
        outputKind switch
        {
            WorkbookExportPrintOutputKind.Pdf => "PDF",
            WorkbookExportPrintOutputKind.Xps => "XPS",
            _ => outputKind.ToString()
        };

    private static string FormatScope(WorkbookExportPrintScope scope) =>
        scope switch
        {
            WorkbookExportPrintScope.SelectedRange => "selected range",
            WorkbookExportPrintScope.VisibleWorkbook => "visible workbook",
            _ => "active sheet"
        };

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";

    private sealed record SheetRangeRequest(
        Sheet Sheet,
        GridRange PrintRange,
        WorkbookExportPrintRangeSource RangeSource);
}
