using Freexcel.Core.Calc;
using Freexcel.Core.Model;
using FluentAssertions;
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

    [Fact]
    public void GetViewport_CommentOnlyCell_PopulatesDisplayCellWithCommentIndicator()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.Comments[address] = "Review total";

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var dc = vp.Cells.Single(c => c.Row == 2 && c.Col == 2);
        dc.HasComment.Should().BeTrue();
        dc.DisplayText.Should().BeEmpty();
    }

    [Fact]
    public void GetViewport_AboveAverageCF_HighlightsCellsAboveAverage()
    {
        // Arrange: three cells with values 10, 20, 30 — average = 20
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        var sheetId = sheet.Id;

        sheet.SetCell(new CellAddress(sheetId, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(new CellAddress(sheetId, 3, 1), Cell.FromValue(new NumberValue(30)));

        var boldStyle = new CellStyle { Bold = true };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 3, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.AboveAverage,
            AboveAverage = true,
            FormatIfTrue = boldStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheetId, new ViewportRequest(1, 1, 500, 500));

        // Assert: 30 > 20 (above average) → bold; 10 < 20 → not bold; 20 == 20 → not bold (not strictly above)
        vp.Cells.Single(c => c.Row == 3 && c.Col == 1).Style!.Bold
            .Should().BeTrue("value 30 is above the average of 20");
        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).Style!.Bold
            .Should().BeFalse("value 10 is below the average of 20");
        vp.Cells.Single(c => c.Row == 2 && c.Col == 1).Style!.Bold
            .Should().BeFalse("value 20 equals the average, not strictly above");
    }
}
