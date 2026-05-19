using Freexcel.Core.Calc;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public class ViewportLayoutTests
{
    [Fact]
    public void GetViewport_SkipsHiddenColumns()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.HiddenCols.Add(2);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 100, 300));

        viewport.ColMetrics.Select(c => c.Col).Should().StartWith([1u, 3u, 4u]);
    }

    [Fact]
    public void GetViewport_NearLastRowAlignsBottomEdgeToWorksheetBoundary()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 20;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(CellAddress.MaxRow - 3, 1, 60, 300));

        viewport.RowMetrics.Select(r => r.Row)
            .Should().Equal(CellAddress.MaxRow - 2, CellAddress.MaxRow - 1, CellAddress.MaxRow);
        viewport.RowMetrics[^1].TopOffset.Should().Be(40);
        (viewport.RowMetrics[^1].TopOffset + viewport.RowMetrics[^1].Height).Should().Be(60);
    }

    [Fact]
    public void GetViewport_NearLastRowKeepsLastRowFullyVisibleWhenHeightsDoNotFitExactly()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 25;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(CellAddress.MaxRow - 3, 1, 60, 300));

        viewport.RowMetrics.Select(r => r.Row)
            .Should().Equal(CellAddress.MaxRow - 2, CellAddress.MaxRow - 1, CellAddress.MaxRow);
        viewport.RowMetrics[0].TopOffset.Should().Be(-15);
        (viewport.RowMetrics[^1].TopOffset + viewport.RowMetrics[^1].Height).Should().Be(60);
    }

    [Fact]
    public void GetViewport_NearLastColumnAlignsRightEdgeToWorksheetBoundary()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 10;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, CellAddress.MaxCol - 2, 100, 160));

        viewport.ColMetrics.Select(c => c.Col)
            .Should().Equal(CellAddress.MaxCol - 1, CellAddress.MaxCol);
        viewport.ColMetrics[^1].LeftOffset.Should().Be(80);
        (viewport.ColMetrics[^1].LeftOffset + viewport.ColMetrics[^1].Width).Should().Be(160);
    }

    [Fact]
    public void GetViewport_NearLastColumnKeepsLastColumnFullyVisibleWhenWidthsDoNotFitExactly()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 8.75;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, CellAddress.MaxCol - 3, 100, 160));

        viewport.ColMetrics.Select(c => c.Col)
            .Should().Equal(CellAddress.MaxCol - 2, CellAddress.MaxCol - 1, CellAddress.MaxCol);
        viewport.ColMetrics[0].LeftOffset.Should().Be(-50);
        (viewport.ColMetrics[^1].LeftOffset + viewport.ColMetrics[^1].Width).Should().Be(160);
    }

    [Fact]
    public void GetViewport_WithFrozenRowsKeepsFrozenRowsFixedWhileBodyScrolls()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 20;
        sheet.FrozenRows = 1;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(20, 1, 60, 300));

        viewport.RowMetrics.Select(r => r.Row).Should().StartWith([1u, 20u, 21u]);
        viewport.RowMetrics[0].TopOffset.Should().Be(0);
        viewport.RowMetrics[1].TopOffset.Should().Be(20);
        viewport.FrozenPanes.Should().Be(new FrozenPaneState(1, 0));
    }

    [Fact]
    public void GetViewport_WithFrozenColumnsKeepsFrozenColumnsFixedWhileBodyScrolls()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 10;
        sheet.FrozenCols = 1;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 20, 100, 240));

        viewport.ColMetrics.Select(c => c.Col).Should().StartWith([1u, 20u, 21u]);
        viewport.ColMetrics[0].LeftOffset.Should().Be(0);
        viewport.ColMetrics[1].LeftOffset.Should().Be(80);
        viewport.FrozenPanes.Should().Be(new FrozenPaneState(0, 1));
    }

    [Fact]
    public void HitTest_SkipsHiddenRowsAndColumns()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.HiddenRows.Add(1);
        sheet.HiddenCols.Add(2);

        var address = new ViewportService().HitTest(
            workbook,
            sheet.Id,
            x: sheet.DefaultColumnWidth * 8 + 1,
            y: 1,
            zoom: 1);

        address.Should().NotBeNull();
        address!.Value.Row.Should().Be(2);
        address.Value.Col.Should().Be(3);
    }

    [Fact]
    public void HitTest_UsesCustomRowHeightsAndColumnWidths()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.RowHeights[1] = 40;
        sheet.ColumnWidths[1] = 20;

        var address = new ViewportService().HitTest(
            workbook,
            sheet.Id,
            x: 170,
            y: 45,
            zoom: 1);

        address.Should().NotBeNull();
        address!.Value.Row.Should().Be(2);
        address.Value.Col.Should().Be(2);
    }

    [Fact]
    public void GetViewport_ReportsSplitPaneState()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SplitRow = 12;
        sheet.SplitColumn = 4;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(5, 2, 500, 500));

        viewport.SplitPanes.Should().NotBeNull();
        viewport.SplitPanes!.Row.Should().Be(12);
        viewport.SplitPanes.Column.Should().Be(4);
    }

    [Fact]
    public void GetViewport_WithSplitPaneIncludesPinnedPaneMetrics()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.HiddenRows.Add(2);
        sheet.HiddenCols.Add(2);
        sheet.SplitRow = 4;
        sheet.SplitColumn = 4;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(20, 10, 500, 500));

        viewport.RowMetrics.Select(r => r.Row).Should().StartWith([20u]);
        viewport.ColMetrics.Select(c => c.Col).Should().StartWith([10u]);
        viewport.SplitPanes.Should().NotBeNull();
        viewport.SplitPanes!.TopRows.Select(r => r.Row).Should().Equal(1u, 3u);
        viewport.SplitPanes.LeftColumns.Select(c => c.Col).Should().Equal(1u, 3u);
    }

    [Fact]
    public void GetViewport_WithSplitPaneIncludesPinnedPaneCells()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SplitRow = 4;
        sheet.SplitColumn = 4;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("top-left"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 10), new TextValue("top"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("left"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 10), new TextValue("main"));

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(20, 10, 120, 160));

        viewport.Cells.Select(cell => (cell.Row, cell.Col, cell.DisplayText))
            .Should().Contain((20u, 10u, "main"));
        viewport.SplitPanes.Should().NotBeNull();
        viewport.SplitPanes!.Cells.Select(cell => (cell.Row, cell.Col, cell.DisplayText))
            .Should().Equal(
                (1u, 1u, "top-left"),
                (1u, 10u, "top"),
                (20u, 1u, "left"));
    }

    [Fact]
    public void GetViewport_WithSplitPaneOffsetsUsesIndependentTopRightAndBottomLeftStarts()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SplitRow = 4;
        sheet.SplitColumn = 4;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 12), new TextValue("top-offset"));
        sheet.SetCell(new CellAddress(sheet.Id, 30, 1), new TextValue("left-offset"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 10), new TextValue("main"));

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(
                20,
                10,
                120,
                160,
                SplitPaneOffsets: new SplitPaneViewportOffsets(TopRightLeftCol: 12, BottomLeftTopRow: 30)));

        viewport.Cells.Select(cell => (cell.Row, cell.Col, cell.DisplayText))
            .Should().Contain((20u, 10u, "main"));
        viewport.SplitPanes.Should().NotBeNull();
        viewport.SplitPanes!.TopRightColumns.Select(column => column.Col).Should().StartWith([12u]);
        viewport.SplitPanes.BottomLeftRows.Select(row => row.Row).Should().StartWith([30u]);
        viewport.SplitPanes.Cells.Select(cell => (cell.Row, cell.Col, cell.DisplayText))
            .Should().Equal((1u, 12u, "top-offset"), (30u, 1u, "left-offset"));
    }

    [Fact]
    public void GetViewport_ShowFormulasDisplaysFormulaTextWithEqualsPrefix()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var formulaCell = Cell.FromFormula("A1+1");
        formulaCell.Value = new NumberValue(3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), formulaCell);
        sheet.ShowFormulas = true;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 100, 300));

        viewport.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("=A1+1");
    }

    [Fact]
    public void GetViewport_ShowFormulasOffDisplaysFormattedValue()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var formulaCell = Cell.FromFormula("A1+1");
        formulaCell.Value = new NumberValue(3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), formulaCell);
        sheet.ShowFormulas = false;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 100, 300));

        viewport.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("3");
    }

    [Fact]
    public void HitTest_UniformRowAndColumnSizes_FastPathMatchesExpectedCell()
    {
        // DefaultRowHeight = 20.0 px, DefaultColumnWidth = 8.43 chars → 8.43*8 = 67.44 px.
        // No custom heights/widths or hidden rows/cols → fast path is taken.
        // y = 199.0 → row = floor(199/20)+1 = 9+1 = 10
        // x = 135.0 → col = floor(135/67.44)+1 = 2+1 = 3  (2*67.44=134.88 < 135 < 3*67.44=202.32)
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("S");

        var address = new ViewportService().HitTest(
            workbook,
            sheet.Id,
            x: 135.0,
            y: 199.0,
            zoom: 1.0);

        address.Should().NotBeNull();
        address!.Value.Row.Should().Be(10);
        address.Value.Col.Should().Be(3);
    }

    [Fact]
    public void HitTest_UniformSizes_FastPathAgreesWithSlowPath()
    {
        // Verify fast and slow paths return the same row/col at many coordinates.
        // Fast sheet: no custom heights/widths → fast path.
        // Slow sheet: custom height/width at a far-away row/col (999) equal to the
        // default → slow path, but same layout as the fast sheet for all reachable cells.
        var workbook = new Workbook("test");
        var fastSheet = workbook.AddSheet("Fast");
        var slowSheet = workbook.AddSheet("Slow");
        slowSheet.RowHeights[999] = slowSheet.DefaultRowHeight;
        slowSheet.ColumnWidths[999] = slowSheet.DefaultColumnWidth;

        var svc = new ViewportService();

        for (double y = 0; y <= 400; y += 19.5)
        {
            for (double x = 0; x <= 400; x += 33.7)
            {
                var fast = svc.HitTest(workbook, fastSheet.Id, x, y, zoom: 1.0);
                var slow = svc.HitTest(workbook, slowSheet.Id, x, y, zoom: 1.0);

                // Compare only row/col (not sheet ID, which differs between the two sheets).
                fast.HasValue.Should().Be(slow.HasValue,
                    $"fast and slow paths should agree (null vs non-null) at x={x}, y={y}");
                if (fast.HasValue && slow.HasValue)
                {
                    fast.Value.Row.Should().Be(slow.Value.Row,
                        $"rows should agree at x={x}, y={y}");
                    fast.Value.Col.Should().Be(slow.Value.Col,
                        $"cols should agree at x={x}, y={y}");
                }
            }
        }
    }
}
