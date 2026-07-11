using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for round-22 finding R22-merged-cells-view-state-2: a merge whose anchor
/// (top-left) row/column is hidden but whose remaining rows/columns stay visible must still render
/// its value/fill, the same way Excel simply collapses the hidden row/column to zero size instead of
/// hiding the whole merged block. ViewportService.Metrics.cs must therefore keep the anchor row/col
/// addressable (a zero-size RowMetric/ColMetric entry) even though it is hidden, so the merge's data
/// -- which lives on the anchor cell -- is still emitted as a DisplayCell that the render layer's
/// merge-surface lookup (keyed on the merge's Start row/col) can find.
/// </summary>
public sealed class R22_MergedCellsHiddenAnchorViewportTests
{
    [Fact]
    public void GetViewport_VerticalMergeWithHiddenAnchorRow_StillExposesAnchorValueAndStyle()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Merge B2:B5 (vertical, 4 rows). Anchor is B2.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)));

        var style = new CellStyle { FillColor = new CellColor(255, 255, 0) };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new TextValue("Total"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), cell);

        // Hide only the anchor row (row 2). Rows 3-5 remain visible, same as Format > Hide Rows on
        // just row 2, or an AutoFilter/outline-collapse that hides only the merge's top row.
        sheet.HiddenRows.Add(2);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        // The hidden anchor row must still be present (zero height) so downstream row/merge lookups
        // that key on the exact anchor row succeed, while the visible rows 3-5 are unaffected.
        viewport.RowMetrics.Select(r => r.Row).Should().Contain(2u);
        viewport.RowMetrics.Single(r => r.Row == 2u).Height.Should().Be(0);
        viewport.RowMetrics.Select(r => r.Row).Should().Contain([1u, 3u, 4u, 5u]);

        // The merge's value/style must be surfaced as a DisplayCell at the anchor coordinate.
        var anchorCell = viewport.Cells.Should().ContainSingle(c => c.Row == 2 && c.Col == 2).Subject;
        anchorCell.DisplayText.Should().Be("Total");
        anchorCell.Style.Should().NotBeNull();
        anchorCell.Style!.FillColor.Should().Be(new CellColor(255, 255, 0));
    }

    [Fact]
    public void GetViewport_HorizontalMergeWithHiddenAnchorColumn_StillExposesAnchorValueAndStyle()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Merge B2:E2 (horizontal, 4 columns). Anchor is B2.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 5)));

        var style = new CellStyle { FillColor = new CellColor(0, 255, 0) };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new TextValue("Header"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), cell);

        // Hide only the anchor column (col B / col 2). Columns C-E remain visible.
        sheet.HiddenCols.Add(2);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        viewport.ColMetrics.Select(c => c.Col).Should().Contain(2u);
        viewport.ColMetrics.Single(c => c.Col == 2u).Width.Should().Be(0);
        viewport.ColMetrics.Select(c => c.Col).Should().Contain([1u, 3u, 4u, 5u]);

        var anchorCell = viewport.Cells.Should().ContainSingle(c => c.Row == 2 && c.Col == 2).Subject;
        anchorCell.DisplayText.Should().Be("Header");
        anchorCell.Style.Should().NotBeNull();
        anchorCell.Style!.FillColor.Should().Be(new CellColor(0, 255, 0));
    }

    [Fact]
    public void GetViewport_HiddenRowNotAMergeAnchor_IsStillSkippedEntirely()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("plain"));

        // Plain hidden row with no merge anchored on it must behave exactly as before: no metric
        // entry at all (not even a zero-height one).
        sheet.HiddenRows.Add(2);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        viewport.RowMetrics.Select(r => r.Row).Should().NotContain(2u);
    }

    [Fact]
    public void GetViewport_MergeWithAllRowsHidden_NoAnchorMetricSurfaced()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Merge B2:B3, but BOTH rows are hidden - nothing in the merge is visible, so Excel would
        // show nothing for it either (the whole block collapses, same as before this fix).
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Total"));
        sheet.HiddenRows.Add(2);
        sheet.HiddenRows.Add(3);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        viewport.RowMetrics.Select(r => r.Row).Should().NotContain(2u);
        viewport.RowMetrics.Select(r => r.Row).Should().NotContain(3u);
    }
}
