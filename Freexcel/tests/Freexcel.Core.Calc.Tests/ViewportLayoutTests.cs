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
}
