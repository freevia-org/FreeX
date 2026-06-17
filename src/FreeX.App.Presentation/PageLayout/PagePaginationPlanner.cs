using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// One contiguous run of rows or columns that belong to a single page along one axis, identified by
/// its first and last 1-based sheet index. Title (repeat) rows/columns that are reprinted on every
/// page are folded into the segment, so the segment spans the page's whole printed extent.
/// </summary>
public readonly record struct PageAxisSegment(uint Start, uint End);

/// <summary>
/// The per-axis page capacity used to slice a print range into pages: how many rows fit on one page
/// and how many columns fit on one page once paper size, margins, orientation, and the active
/// scale-to-fit setting have been applied.
/// </summary>
public readonly record struct PageCapacity(uint RowsPerPage, uint ColumnsPerPage);

/// <summary>
/// The result of slicing a print range into pages: the row and column segments along each axis (the
/// cross product of which forms the page grid), plus the effective scale percent that a fit-to-pages
/// request resolves to. <see cref="EffectiveScalePercent"/> is the explicit scale when one is set,
/// otherwise the smaller of the horizontal/vertical shrink ratios implied by the fit-to request, or
/// 100 when neither is set.
/// </summary>
public sealed record PagePaginationResult(
    IReadOnlyList<PageAxisSegment> RowSegments,
    IReadOnlyList<PageAxisSegment> ColumnSegments,
    double EffectiveScalePercent)
{
    public int RowPageCount => RowSegments.Count;
    public int ColumnPageCount => ColumnSegments.Count;
    public int PageCount => RowSegments.Count * ColumnSegments.Count;
}

/// <summary>
/// Pure pagination math shared by the desktop hosts and print/export. Given a print range, the active
/// page setup (paper size, margins, orientation, scale or fit-to N-wide × M-tall), the repeat
/// rows/columns, and any manual page breaks, it computes the page grid and the effective print scale.
///
/// The capacity math mirrors the source desktop layout: a nominal printable area is derived in pixels
/// from the paper size minus margins (at 96 dpi), divided by a nominal row height / minimum column
/// width to get a baseline rows/columns-per-page, then adjusted for the explicit scale percent or the
/// fit-to-pages request. Page slicing itself is delegated to <see cref="PrintLayoutPlanner"/> so this
/// layer never diverges from the export planner's row/column page plans.
/// </summary>
public static class PagePaginationPlanner
{
    /// <summary>Drawing surface resolution the source layout assumes for the printable area, in dots per inch.</summary>
    public const double Dpi = 96.0;

    /// <summary>Nominal printed column width (pixels) used to size a page when no explicit scale is set.</summary>
    public const double MinimumPrintColumnWidth = 40.0;

    /// <summary>Nominal printed row height (pixels) used to size a page when no explicit scale is set.</summary>
    public const double NominalRowHeight = 20.0;

    private const int MinScalePercent = 10;
    private const int MaxScalePercent = 400;

    /// <summary>
    /// Computes the baseline rows/columns that fit on one page from paper size, margins, and
    /// orientation, then applies the scale-to-fit setting (explicit percent or fit-to-pages) per axis.
    /// </summary>
    public static PageCapacity CalculatePageCapacity(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins)
    {
        var pageSize = WorksheetPageLayout.GetPageSizeInches(paperSize, orientation);
        var printableWidth = Math.Max(1.0, (pageSize.Width - margins.Left - margins.Right) * Dpi);
        var printableHeight = Math.Max(1.0, (pageSize.Height - margins.Top - margins.Bottom) * Dpi);
        var rowsPerPage = Math.Max(1u, (uint)Math.Floor(printableHeight / NominalRowHeight));
        var columnsPerPage = Math.Max(1u, (uint)Math.Floor(printableWidth / MinimumPrintColumnWidth));

        rowsPerPage = ApplyScaleToFitCapacity(
            rowsPerPage,
            printRange.Start.Row,
            printRange.End.Row,
            printTitleRows,
            CellAddress.MaxRow,
            scaleToFit.ScalePercent,
            scaleToFit.FitToPagesTall);
        columnsPerPage = ApplyScaleToFitCapacity(
            columnsPerPage,
            printRange.Start.Col,
            printRange.End.Col,
            printTitleColumns,
            CellAddress.MaxCol,
            scaleToFit.ScalePercent,
            scaleToFit.FitToPagesWide);

        return new PageCapacity(rowsPerPage, columnsPerPage);
    }

    /// <summary>
    /// Slices a print range into page segments along both axes and reports the effective scale.
    /// Title rows/columns are reprinted on every page; manual breaks force a new page; the explicit
    /// scale percent (when set) wins, otherwise a fit-to-pages request resolves to the shrink ratio
    /// that makes the body fit the requested page count.
    /// </summary>
    public static PagePaginationResult Paginate(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins,
        IReadOnlyCollection<uint>? rowPageBreaks = null,
        IReadOnlyCollection<uint>? columnPageBreaks = null)
    {
        var capacity = CalculatePageCapacity(
            printRange,
            scaleToFit,
            printTitleRows,
            printTitleColumns,
            paperSize,
            orientation,
            margins);

        var rowSegments = BuildSegments(PrintLayoutPlanner.BuildRowPlans(
            printRange,
            printTitleRows,
            capacity.RowsPerPage,
            rowPageBreaks));
        var columnSegments = BuildSegments(PrintLayoutPlanner.BuildColumnPlans(
            printRange,
            printTitleColumns,
            capacity.ColumnsPerPage,
            columnPageBreaks));

        var effectiveScale = CalculateEffectiveScalePercent(scaleToFit, rowSegments.Count, columnSegments.Count);
        return new PagePaginationResult(rowSegments, columnSegments, effectiveScale);
    }

    /// <summary>
    /// Resolves the scale-to-fit setting to a single scale percent. An explicit percent (clamped to
    /// the supported range) wins. Otherwise a fit-to N-wide × M-tall request resolves to
    /// <c>100 × min(wide / actualColumnPages, tall / actualRowPages)</c> — the same shrink the source
    /// layout applies so the sheet collapses onto the requested page count. With neither set the scale
    /// is 100.
    /// </summary>
    public static double CalculateEffectiveScalePercent(
        WorksheetScaleToFit scaleToFit,
        int actualRowPages,
        int actualColumnPages)
    {
        if (scaleToFit.ScalePercent is { } percent)
            return Math.Clamp(percent, MinScalePercent, MaxScalePercent);

        var ratio = 1.0;
        if (scaleToFit.FitToPagesWide is { } wide and >= 1 && actualColumnPages > wide)
            ratio = Math.Min(ratio, wide / (double)actualColumnPages);
        if (scaleToFit.FitToPagesTall is { } tall and >= 1 && actualRowPages > tall)
            ratio = Math.Min(ratio, tall / (double)actualRowPages);

        return 100.0 * ratio;
    }

    internal static uint ApplyScaleToFitCapacity(
        uint baseItemsPerPage,
        uint start,
        uint end,
        WorksheetRepeatRange? repeat,
        uint maxItem,
        int? scalePercent,
        int? fitToPages)
    {
        if (scalePercent is { } percent and >= MinScalePercent and <= MaxScalePercent)
            return Math.Max(1, (uint)Math.Floor(baseItemsPerPage * (100d / percent)));

        if (fitToPages is not { } pageCount || pageCount < 1)
            return baseItemsPerPage;

        var titleCount = CountRepeatItems(repeat, maxItem);
        var bodyCount = CountBodyItems(start, end, repeat);
        if (bodyCount == 0)
            return Math.Max(1, titleCount);

        var bodyItemsPerPage = (uint)Math.Ceiling(bodyCount / (double)pageCount);
        return Math.Max(1, bodyItemsPerPage + titleCount);
    }

    private static uint CountRepeatItems(WorksheetRepeatRange? repeat, uint maxItem)
    {
        if (repeat is not { } range || range.Start == 0 || range.Start > maxItem || range.End < range.Start)
            return 0;

        return Math.Min(range.End, maxItem) - range.Start + 1;
    }

    private static uint CountBodyItems(uint start, uint end, WorksheetRepeatRange? repeat)
    {
        if (end < start)
            return 0;

        var count = end - start + 1;
        if (repeat is not { } range || range.End < start || range.Start > end)
            return count;

        var overlapStart = Math.Max(start, range.Start);
        var overlapEnd = Math.Min(end, range.End);
        return overlapEnd >= overlapStart
            ? count - (overlapEnd - overlapStart + 1)
            : count;
    }

    private static IReadOnlyList<PageAxisSegment> BuildSegments(IReadOnlyList<PrintPageRowPlan> plans) =>
        BuildSegments(plans, static plan => plan.BodyRows, static plan => plan.TitleRows);

    private static IReadOnlyList<PageAxisSegment> BuildSegments(IReadOnlyList<PrintPageColumnPlan> plans) =>
        BuildSegments(plans, static plan => plan.BodyColumns, static plan => plan.TitleColumns);

    private static IReadOnlyList<PageAxisSegment> BuildSegments<TPlan>(
        IReadOnlyList<TPlan> plans,
        Func<TPlan, IReadOnlyList<uint>> getBodyIndexes,
        Func<TPlan, IReadOnlyList<uint>> getTitleIndexes)
    {
        var segments = new List<PageAxisSegment>(plans.Count);
        foreach (var plan in plans)
        {
            var indexes = getBodyIndexes(plan);
            if (indexes.Count == 0)
                indexes = getTitleIndexes(plan);
            if (indexes.Count == 0)
                continue;

            segments.Add(new PageAxisSegment(indexes[0], indexes[^1]));
        }

        return segments;
    }
}
