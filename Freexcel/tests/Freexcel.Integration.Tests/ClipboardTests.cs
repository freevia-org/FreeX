using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Integration.Tests;

/// <summary>Tests clipboard logic without WPF clipboard — exercises tab-separated serialisation.</summary>
public class ClipboardTests
{
    [Fact]
    public void SerialiseRange_SingleCell_ReturnsDisplayText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));
        var cell = vp.Cells.Single(c => c.Row == 1 && c.Col == 1);

        Assert.Equal("42", cell.DisplayText);
    }

    [Fact]
    public void SerialiseRange_TwoColumns_TabSeparated()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("A")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("B")));

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var text = ClipboardSerializer.Serialize(vp, new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2)));

        Assert.Equal("A\tB", text);
    }

    [Fact]
    public void SerialiseRange_TwoRows_NewlineSeparated()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("R1")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("R2")));

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var text = ClipboardSerializer.Serialize(vp, new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1)));

        Assert.Equal("R1\r\nR2", text);
    }

    [Fact]
    public void Deserialize_ExcelTrailingNewline_DoesNotCreateExtraBlankRow()
    {
        var rows = ClipboardSerializer.Deserialize("A\tB\r\nC\tD\r\n");

        Assert.Equal(2, rows.Length);
        Assert.Equal(["A", "B"], rows[0]);
        Assert.Equal(["C", "D"], rows[1]);
    }
}
