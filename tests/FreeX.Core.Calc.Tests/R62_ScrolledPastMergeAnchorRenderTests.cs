using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R62-render-merged-cell-6-1: a merge whose ANCHOR row/column has scrolled past the top/left of
/// the visible viewport window (not hidden -- just scrolled off-screen) must keep drawing its
/// still-visible remainder (fill/border/text), simply clipped at the window's edge exactly like
/// Excel clips a very tall single row/wide single column. Before the fix,
/// <c>ViewportService.BuildFrozenAwareRowMetrics</c>/<c>...ColMetrics</c> only ever emitted
/// RowMetric/ColMetric entries for rows/columns at-or-after the requested TopRow/LeftCol, so the
/// merge's anchor cell (which alone carries the merge's value/style) was completely unreachable by
/// <c>rowLookup</c>/<c>colLookup</c> in GridView's merge-surface render pass, and the entire merge
/// vanished -- no fill, no border, no text -- for its visible remainder.
/// </summary>
public sealed class R62_ScrolledPastMergeAnchorRenderTests
{
    private static (Workbook Workbook, Sheet Sheet) MakeWorkbook()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    [Fact]
    public void GetViewport_VerticalMergeAnchorScrolledAboveWindow_StaysAddressableForVisibleRemainder()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Merge B2:B30 (tall vertical "section header" banner), anchored at B2 with a yellow fill.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 30, 2)));
        var fillStyle = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        var styleId = workbook.RegisterStyle(fillStyle);
        var anchorCell = Cell.FromValue(new TextValue("Section"));
        anchorCell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), anchorCell);

        // Scroll the sheet down (no freeze panes) so the visible window is rows 15-40: the merge's
        // anchor row (2) is now above the top of the screen, but rows 15-30 (part of the merge) are
        // still squarely on-screen.
        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(15, 1, 400, 400));

        // The anchor row must stay addressable (as a zero-height metric) so the merge's rect can
        // still be computed for the still-visible remainder.
        viewport.RowMetrics.Should().ContainSingle(r => r.Row == 2)
            .Which.Height.Should().Be(0);

        // The merge's value/style live only on the anchor cell, so it must still surface.
        viewport.Cells.Should().ContainSingle(c => c.Row == 2 && c.Col == 2)
            .Which.DisplayText.Should().Be("Section");
        viewport.Cells.First(c => c.Row == 2 && c.Col == 2)
            .Style!.FillColor.Should().Be(new CellColor(255, 235, 132));

        // The still-visible remainder rows (15-30) stay in the viewport with real heights.
        viewport.RowMetrics.Should().Contain(r => r.Row == 15 && r.Height > 0);
        viewport.RowMetrics.Should().Contain(r => r.Row == 30 && r.Height > 0);
    }

    [Fact]
    public void GetViewport_HorizontalMergeAnchorScrolledLeftOfWindow_StaysAddressableForVisibleRemainder()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Merge B2:AD2 (wide horizontal banner spanning columns 2-30), anchored at column 2.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 30)));
        var fillStyle = new CellStyle { FillColor = new CellColor(132, 200, 255) };
        var styleId = workbook.RegisterStyle(fillStyle);
        var anchorCell = Cell.FromValue(new TextValue("Banner"));
        anchorCell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), anchorCell);

        // Scroll the sheet right so the visible window starts at column 15.
        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 15, 400, 2000));

        viewport.ColMetrics.Should().ContainSingle(c => c.Col == 2)
            .Which.Width.Should().Be(0);

        viewport.Cells.Should().ContainSingle(c => c.Row == 2 && c.Col == 2)
            .Which.DisplayText.Should().Be("Banner");

        viewport.ColMetrics.Should().Contain(c => c.Col == 15 && c.Width > 0);
        viewport.ColMetrics.Should().Contain(c => c.Col == 30 && c.Width > 0);
    }

    [Fact]
    public void GetViewport_FrozenRowsWithScrollPastMerge_DoesNotDuplicateFrozenAnchorMetric()
    {
        // Guards against the crash risk in the fix: when frozen rows are active, a merge anchored
        // WITHIN the frozen band must not also get re-added as a lookback anchor for the scrolled
        // body band (that would create two RowMetric entries for the same row and crash the
        // render-side row-metric dictionary, which requires unique keys).
        var (workbook, sheet) = MakeWorkbook();
        sheet.FrozenRows = 3;

        // Merge anchored at row 2 -- INSIDE the frozen band (rows 1-3) -- extends down to row 10,
        // well past the frozen boundary but not reaching the scrolled body window at all.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 10, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Frozen"));

        // A second merge anchored between the frozen band and the scroll window (row 5), extending
        // into the scrolled body window (rows 20-40) -- this ONE should get the lookback fix.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 5, 3), new CellAddress(sheet.Id, 25, 3)));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("BodyBanner"));

        var act = () => new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(20, 1, 400, 400));

        var viewport = act.Should().NotThrow().Which;

        // Row 2 (frozen-band anchor) appears exactly once, via the pinned frozen band itself.
        viewport.RowMetrics.Should().ContainSingle(r => r.Row == 2);

        // Row 5 (body-scroll-past anchor) is now addressable too, via the lookback fix.
        viewport.RowMetrics.Should().ContainSingle(r => r.Row == 5)
            .Which.Height.Should().Be(0);

        viewport.Cells.Should().ContainSingle(c => c.Row == 5 && c.Col == 3)
            .Which.DisplayText.Should().Be("BodyBanner");
    }

    // Sibling no-regression test: an ordinary scroll with no merge anchored above the window must
    // not gain any spurious extra RowMetric/ColMetric entries.
    [Fact]
    public void GetViewport_ScrolledWithNoMergesAboveWindow_RowMetricsStartsExactlyAtTopRow()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 20, 2), new TextValue("Plain"));

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(15, 1, 400, 400));

        viewport.RowMetrics.Should().NotBeEmpty();
        viewport.RowMetrics[0].Row.Should().Be(15);
        viewport.RowMetrics.Should().OnlyContain(r => r.Row >= 15);
    }

    // Sibling no-regression test: the existing hidden-anchor-row behavior (R23) must be unaffected
    // by the new scrolled-past lookback logic.
    [Fact]
    public void GetViewport_HiddenMergeAnchorRowWithVisibleRemainder_StillWorks()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Total"));
        sheet.HiddenRows.Add(2);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        viewport.Cells.Should().ContainSingle(c => c.Row == 2 && c.Col == 2)
            .Which.DisplayText.Should().Be("Total");
    }
}
