using Freexcel.Core.Calc;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Calc.Tests;

public class ViewportStyleTests
{
    [Fact]
    public void GetViewport_CellWithBoldStyle_PopulatesStyleOnDisplayCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var style = new CellStyle { Bold = true };
        var styleId = workbook.RegisterStyle(style);

        var cell = Cell.FromValue(new NumberValue(1));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id,
            new ViewportRequest(1, 1, 500, 500));

        var dc = vp.Cells.Single(c => c.Row == 1 && c.Col == 1);
        Assert.NotNull(dc.Style);
        Assert.True(dc.Style!.Bold);
    }

    [Fact]
    public void GetViewport_CellWithDefaultStyle_StyleIsDefault()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1),
            Cell.FromValue(new NumberValue(42)));

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id,
            new ViewportRequest(1, 1, 500, 500));

        var dc = vp.Cells.Single(c => c.Row == 1 && c.Col == 1);
        Assert.NotNull(dc.Style);
        Assert.False(dc.Style!.Bold);
    }
}
