using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void ApplyStructuredTableStyleCommand_UsesStyleOptionFlagsForHeaderTotalsColumnsAndStripes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(3));
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            HeaderRowCount = 1,
            TotalsRowShown = true,
            ShowFirstColumn = true,
            ShowLastColumn = true,
            ShowRowStripes = false,
            ShowColumnStripes = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Sales"),
                new StructuredTableColumnModel(3, "Orders")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new ApplyStructuredTableStyleCommand(
            sheet.Id,
            table.Id,
            new StructuredTableStyleBanding(
                HeaderFill: new CellColor(31, 78, 121),
                OddRowFill: new CellColor(222, 235, 247),
                EvenRowFill: CellColor.White,
                HeaderFontColor: CellColor.White),
            "TableStyleMedium2",
            updateStyleName: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Single().StyleName.Should().Be("TableStyleMedium2");
        var headerStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId);
        headerStyle.FillColor.Should().Be(new CellColor(31, 78, 121));
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.Bold.Should().BeTrue();
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.StyleId)
            .FillColor.Should().Be(CellColor.White);
        var stripedColumnStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.StyleId);
        stripedColumnStyle.FillColor.Should().Be(new CellColor(222, 235, 247));
        stripedColumnStyle.Bold.Should().BeFalse();
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 3))!.StyleId)
            .Bold.Should().BeTrue();
        var totalsStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 5, 2))!.StyleId);
        totalsStyle.FillColor.Should().Be(new CellColor(31, 78, 121));
        totalsStyle.FontColor.Should().Be(CellColor.White);
        totalsStyle.Bold.Should().BeTrue();

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId)
            .Should().Be(wb.GetStyle(StyleId.Default));
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.StyleId)
            .Should().Be(wb.GetStyle(StyleId.Default));
    }

    [Fact]
    public void ApplyStructuredTableStyleCommand_SkipsHeaderStylingWhenHeaderRowIsHidden()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        table = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 0,
            ShowRowStripes = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Status")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new ApplyStructuredTableStyleCommand(
            sheet.Id,
            table.Id,
            new StructuredTableStyleBanding(
                HeaderFill: new CellColor(31, 78, 121),
                OddRowFill: new CellColor(222, 235, 247),
                EvenRowFill: CellColor.White,
                HeaderFontColor: CellColor.White));

        command.Apply(ctx).Success.Should().BeTrue();

        var firstRowStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId);
        firstRowStyle.FillColor.Should().Be(CellColor.White);
        firstRowStyle.Bold.Should().BeFalse();
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.StyleId)
            .FillColor.Should().Be(new CellColor(222, 235, 247));
    }
}
