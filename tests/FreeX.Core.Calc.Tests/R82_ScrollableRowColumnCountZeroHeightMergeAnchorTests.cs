using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R82-render-scroll-viewport-5-2: when the viewport has scrolled into a tall/wide merge whose
/// anchor row/column sits above/left of the visible window,
/// <c>PrependScrolledPastMergeAnchorRows</c>/<c>...Cols</c> (ViewportService.Metrics.cs) prepend
/// zero-height/zero-width <see cref="RowMetric"/>/<see cref="ColMetric"/> placeholder entries
/// (documented there) so the merge's still-visible remainder keeps drawing, clipped at the top/
/// left. Those placeholders occupy zero screen pixels and must never be counted as on-screen,
/// scrollable rows/columns -- doing so inflates the scrollbar's ViewportSize/LargeChange and the
/// Page Up/Down/Page Left/Right jump distance by one row/column per placeholder.
/// <see cref="ViewportService.CountScrollableRows"/>/<see cref="ViewportService.CountScrollableColumns"/>
/// are the fix: a shared, correctly-guarded (Height/Width &gt; 0) way to count scrollable metrics.
/// </summary>
public sealed class R82_ScrollableRowColumnCountZeroHeightMergeAnchorTests
{
    private static (Workbook Workbook, Sheet Sheet) MakeWorkbook()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    [Fact]
    public void CountScrollableRows_ExcludesZeroHeightScrolledPastMergeAnchorPlaceholders()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Merge B2:B40 (a 39-row "category label" merge next to a data table), anchored at row 2.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 40, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Category"));

        // Scroll down so the viewport's first visible row (25) lands inside the merge: the anchor
        // (row 2) is above the window, but End.Row (40) is still >= the window start.
        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(25, 1, 400, 400));

        // Sanity check on the documented placeholder behavior: rows 2..24 (23 rows) are prepended
        // as zero-height placeholders ahead of the genuinely visible rows.
        var placeholderRows = viewport.RowMetrics.Where(r => r.Height == 0).ToList();
        placeholderRows.Should().HaveCount(23);
        placeholderRows.Select(r => r.Row).Should().BeEquivalentTo(Enumerable.Range(2, 23).Select(r => (uint)r));

        var realRowCount = viewport.RowMetrics.Count(r => r.Height > 0);
        realRowCount.Should().BeGreaterThan(0);

        // The scrollable-row count must reflect only the real, on-screen rows -- NOT the 23
        // zero-pixel placeholders (which a naive `Row > frozenRows` count, with no Height guard,
        // would incorrectly include).
        ViewportService.CountScrollableRows(viewport.RowMetrics, sheet.FrozenRows)
            .Should().Be(realRowCount);
        ViewportService.CountScrollableRows(viewport.RowMetrics, sheet.FrozenRows)
            .Should().NotBe(viewport.RowMetrics.Count);
    }

    [Fact]
    public void CountScrollableColumns_ExcludesZeroWidthScrolledPastMergeAnchorPlaceholders()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Merge B2:AN2 (a wide horizontal banner spanning columns 2-40), anchored at column 2.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 40)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Banner"));

        // Scroll right so the viewport's first visible column (25) lands inside the merge.
        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 25, 400, 2000));

        var placeholderCols = viewport.ColMetrics.Where(c => c.Width == 0).ToList();
        placeholderCols.Should().HaveCount(23);
        placeholderCols.Select(c => c.Col).Should().BeEquivalentTo(Enumerable.Range(2, 23).Select(c => (uint)c));

        var realColCount = viewport.ColMetrics.Count(c => c.Width > 0);
        realColCount.Should().BeGreaterThan(0);

        ViewportService.CountScrollableColumns(viewport.ColMetrics, sheet.FrozenCols)
            .Should().Be(realColCount);
        ViewportService.CountScrollableColumns(viewport.ColMetrics, sheet.FrozenCols)
            .Should().NotBe(viewport.ColMetrics.Count);
    }

    // Sibling no-regression test: an ordinary scroll with no merges above/left of the window (the
    // overwhelmingly common case) must still count every row -- the Height guard must not exclude
    // genuinely visible rows.
    [Fact]
    public void CountScrollableRows_WithNoScrolledPastMergeAnchors_CountsEveryVisibleRow()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 20, 2), new TextValue("Plain"));

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(15, 1, 400, 400));

        viewport.RowMetrics.Should().NotBeEmpty();
        viewport.RowMetrics.Should().OnlyContain(r => r.Height > 0);

        ViewportService.CountScrollableRows(viewport.RowMetrics, sheet.FrozenRows)
            .Should().Be(viewport.RowMetrics.Count);
    }

    // Sibling no-regression test: frozen rows must still be excluded from the scrollable count,
    // exactly as before this fix -- the Height guard is additive, not a replacement for the
    // existing frozen-boundary exclusion.
    [Fact]
    public void CountScrollableRows_WithFrozenRows_StillExcludesFrozenBand()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.FrozenRows = 3;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        var scrollableCount = ViewportService.CountScrollableRows(viewport.RowMetrics, sheet.FrozenRows);
        var expected = viewport.RowMetrics.Count(r => r.Row > sheet.FrozenRows && r.Height > 0);

        scrollableCount.Should().Be(expected);
        scrollableCount.Should().BeLessThan(viewport.RowMetrics.Count);
    }
}
