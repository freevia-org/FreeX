using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R15-print-preview-interaction-2: PageBreakPreviewLayoutPlanner.Calculate must forward the sheet's
/// row/column hidden predicates to PagePaginationPlanner.Paginate, exactly like the real print path
/// (PrintPreviewPaginationContext). Before the fix, Calculate had no way to receive these predicates at
/// all, so the page-break-preview overlay counted hidden/filtered rows toward page capacity and produced
/// a different (larger) page count than the print output for the same sheet.
/// </summary>
public sealed class R15_pagebreak_pagination_Tests
{
    // A4 portrait, Narrow margins, 11px rows -> ~93 rows/page (printable height ~1026px / 11px).
    private const double RowHeightPx = 11.0;
    private const uint TotalRows = 100;
    private const uint FirstHiddenRow = 40;
    private const uint LastHiddenRow = 60;

    private static bool IsHiddenRow(uint row) => row is >= FirstHiddenRow and <= LastHiddenRow;

    private static ViewportModel CreateFullHeightViewport() =>
        new(
            [],
            Enumerable.Range(1, (int)TotalRows)
                .Select(row => new RowMetric((uint)row, RowHeightPx, (row - 1) * RowHeightPx))
                .ToList(),
            [new ColMetric(1, 40, 0)],
            null,
            []);

    [Fact]
    public void Calculate_WithHiddenRowPredicate_YieldsFewerPagesThanCountingAllRows()
    {
        var sheetId = SheetId.New();
        var viewport = CreateFullHeightViewport();
        var printArea = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, TotalRows, 1));

        var layoutWithoutHiddenRows = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printArea,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 100,
            actualHeight: 1200,
            defaultRowHeight: RowHeightPx);

        var layoutWithHiddenRows = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printArea,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 100,
            actualHeight: 1200,
            defaultRowHeight: RowHeightPx,
            isRowHidden: IsHiddenRow);

        // Without the fix, hidden rows 40-60 still count toward page capacity, so the 100-row print
        // area needs 2 pages. With the fix, those 21 rows are excluded (79 effective rows), which fits
        // on a single page - matching what actually prints.
        layoutWithoutHiddenRows.Pages.Should().HaveCount(2);
        layoutWithHiddenRows.Pages.Should().HaveCount(1);
        layoutWithHiddenRows.Pages.Count.Should().BeLessThan(layoutWithoutHiddenRows.Pages.Count);
    }

    [Fact]
    public void Calculate_WithHiddenRowPredicate_MatchesRealPrintPaginationPageCount()
    {
        var sheetId = SheetId.New();
        var viewport = CreateFullHeightViewport();
        var printArea = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, TotalRows, 1));

        var overlayLayout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printArea,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 100,
            actualHeight: 1200,
            defaultRowHeight: RowHeightPx,
            isRowHidden: IsHiddenRow);

        // Same predicate through the real print pagination path (PagePaginationPlanner.Paginate, as
        // used by PrintPreviewPaginationContext.TryCreate for the actual print/PDF job).
        var printPagination = PagePaginationPlanner.Paginate(
            printArea,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeights: new Dictionary<uint, double>(),
            defaultRowHeight: RowHeightPx,
            columnWidths: new Dictionary<uint, double>(),
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0,
            isRowHidden: IsHiddenRow);

        overlayLayout.Pages.Should().HaveCount(printPagination.PageCount);
    }
}
