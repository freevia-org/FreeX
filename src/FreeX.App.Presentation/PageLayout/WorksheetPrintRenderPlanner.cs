using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Page dimensions and margins for a worksheet print render, expressed in the same 96-dpi device
/// independent units consumed by the WPF and preview renderers.
/// </summary>
public sealed record WorksheetPrintRenderMetrics(
    double PageWidth,
    double PageHeight,
    double MarginLeft,
    double MarginRight,
    double MarginTop,
    double MarginBottom,
    double HeaderMargin,
    double FooterMargin)
{
    public double PrintableWidth => PageWidth - MarginLeft - MarginRight;
    public double PrintableHeight => PageHeight - MarginTop - MarginBottom;
}

/// <summary>
/// The worksheet viewport extent needed to include all printed areas plus repeat-title rows/columns.
/// The host still owns the viewport service; the neutral planner owns how large the request must be.
/// </summary>
public sealed record WorksheetPrintViewportPlan(uint MaxRow, uint MaxColumn)
{
    public const double ExtentMultiplier = 9999.0;

    public double RequestHeight => MaxRow * ExtentMultiplier;
    public double RequestWidth => MaxColumn * ExtentMultiplier;
}

/// <summary>One printable page after resolving area order, page order, title rows/columns, and page numbering.</summary>
public sealed record WorksheetPrintPagePlan(
    int PageIndex,
    int AreaIndex,
    int AreaPageIndex,
    int SheetPageNumber,
    int PageNumber,
    GridRange PrintRange,
    PrintPageRowPlan RowPlan,
    PrintPageColumnPlan ColumnPlan,
    IReadOnlyList<uint> Rows,
    IReadOnlyList<uint> Columns);

/// <summary>Pagination and page entries for one configured print area.</summary>
public sealed record WorksheetPrintAreaPlan(
    int AreaIndex,
    GridRange PrintRange,
    PagePaginationPlan Pagination,
    IReadOnlyList<WorksheetPrintPagePlan> Pages);

/// <summary>
/// Complete neutral print-render plan for one worksheet. Platform renderers consume this data and
/// keep only device APIs, visuals, text measurement, and document primitives in the host.
/// </summary>
public sealed record WorksheetPrintRenderPlan(
    WorksheetPrintRenderMetrics Metrics,
    WorksheetPrintViewportPlan Viewport,
    IReadOnlyList<GridRange> PrintRanges,
    IReadOnlyList<WorksheetPrintAreaPlan> AreaPlans,
    IReadOnlyList<WorksheetPrintPagePlan> Pages,
    int FirstPageNumber)
{
    public int GridPageCount => Pages.Count;
}

/// <summary>
/// Neutral worksheet print-planning policy shared by desktop hosts. It resolves the print ranges,
/// multi-area pagination, page-order traversal, repeat-title rows/columns, first-page numbering, and
/// viewport extent before any renderer touches platform, printer, or PDF primitives.
/// </summary>
public static class WorksheetPrintRenderPlanner
{
    public static bool TryBuild(
        Sheet sheet,
        GridRange? printRangeOverride,
        bool ignorePrintArea,
        out WorksheetPrintRenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var printRanges = ResolvePrintRanges(sheet, printRangeOverride, ignorePrintArea);
        if (printRanges.Count == 0)
        {
            plan = null!;
            return false;
        }

        var metrics = BuildMetrics(sheet);
        var viewport = BuildViewportPlan(sheet, printRanges);
        var firstPageNumber = sheet.FirstPageNumber ?? 1;
        var nextPageNumber = firstPageNumber;
        var allPages = new List<WorksheetPrintPagePlan>();
        var areaPlans = new List<WorksheetPrintAreaPlan>(printRanges.Count);

        for (var areaIndex = 0; areaIndex < printRanges.Count; areaIndex++)
        {
            var printRange = printRanges[areaIndex];
            var pagination = PagePaginationPlanner.BuildPlan(
                printRange,
                sheet.ScaleToFit,
                sheet.PrintTitleRows,
                sheet.PrintTitleColumns,
                sheet.PaperSize,
                sheet.PageOrientation,
                sheet.PageMargins,
                sheet.RowHeights,
                sheet.DefaultRowHeight,
                sheet.ColumnWidths,
                sheet.DefaultColumnWidth,
                sheet.HeaderMargin,
                sheet.FooterMargin,
                sheet.RowPageBreaks,
                sheet.ColumnPageBreaks,
                sheet.IsRowEffectivelyHidden,
                sheet.IsColEffectivelyHidden);

            var areaPages = new List<WorksheetPrintPagePlan>(pagination.PageCount);
            foreach (var gridPage in PrintPageGridPlanner.Build(pagination.RowPlans, pagination.ColumnPlans, sheet.PageOrder))
            {
                var rows = BuildPageIndexes(gridPage.RowPlan.TitleRows, gridPage.RowPlan.BodyRows);
                var columns = BuildPageIndexes(gridPage.ColumnPlan.TitleColumns, gridPage.ColumnPlan.BodyColumns);
                if (rows.Count == 0 || columns.Count == 0)
                    continue;

                var page = new WorksheetPrintPagePlan(
                    allPages.Count,
                    areaIndex,
                    gridPage.PageIndex,
                    gridPage.SheetPageNumber,
                    nextPageNumber++,
                    printRange,
                    gridPage.RowPlan,
                    gridPage.ColumnPlan,
                    rows,
                    columns);
                areaPages.Add(page);
                allPages.Add(page);
            }

            areaPlans.Add(new WorksheetPrintAreaPlan(areaIndex, printRange, pagination, areaPages));
        }

        if (allPages.Count == 0)
        {
            plan = null!;
            return false;
        }

        plan = new WorksheetPrintRenderPlan(
            metrics,
            viewport,
            printRanges,
            areaPlans,
            allPages,
            firstPageNumber);
        return true;
    }

    public static IReadOnlyList<GridRange> ResolvePrintRanges(
        Sheet sheet,
        GridRange? printRangeOverride,
        bool ignorePrintArea)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (printRangeOverride is { } rangeOverride &&
            rangeOverride.Start.Sheet == sheet.Id &&
            rangeOverride.End.Sheet == sheet.Id)
        {
            return [rangeOverride];
        }

        if (ignorePrintArea)
            return ResolveUsedRange(sheet);

        if (sheet.PrintAreas.Count > 0)
        {
            return sheet.PrintAreas
                .Where(area => area.Start.Sheet == sheet.Id && area.End.Sheet == sheet.Id)
                .ToList();
        }

        return ResolveUsedRange(sheet);
    }

    public static WorksheetPrintRenderMetrics BuildMetrics(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var pageSize = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation);
        var margins = sheet.PageMargins;
        return new WorksheetPrintRenderMetrics(
            pageSize.Width * PagePaginationPlanner.Dpi,
            pageSize.Height * PagePaginationPlanner.Dpi,
            margins.Left * PagePaginationPlanner.Dpi,
            margins.Right * PagePaginationPlanner.Dpi,
            margins.Top * PagePaginationPlanner.Dpi,
            margins.Bottom * PagePaginationPlanner.Dpi,
            sheet.HeaderMargin * PagePaginationPlanner.Dpi,
            sheet.FooterMargin * PagePaginationPlanner.Dpi);
    }

    private static WorksheetPrintViewportPlan BuildViewportPlan(Sheet sheet, IReadOnlyList<GridRange> printRanges)
    {
        var maxPrintRow = printRanges.Max(range => range.End.Row);
        var maxPrintCol = printRanges.Max(range => range.End.Col);
        return new WorksheetPrintViewportPlan(
            Math.Max(maxPrintRow, sheet.PrintTitleRows?.End ?? 0),
            Math.Max(maxPrintCol, sheet.PrintTitleColumns?.End ?? 0));
    }

    private static IReadOnlyList<uint> BuildPageIndexes(
        IReadOnlyList<uint> titleIndexes,
        IReadOnlyList<uint> bodyIndexes)
    {
        if (titleIndexes.Count == 0)
            return bodyIndexes;
        if (bodyIndexes.Count == 0)
            return titleIndexes;

        var indexes = new List<uint>(titleIndexes.Count + bodyIndexes.Count);
        indexes.AddRange(titleIndexes);
        indexes.AddRange(bodyIndexes);
        return indexes;
    }

    private static IReadOnlyList<GridRange> ResolveUsedRange(Sheet sheet) =>
        sheet.GetUsedRange() is { } usedRange ? [usedRange] : [];
}
