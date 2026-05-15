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
}
