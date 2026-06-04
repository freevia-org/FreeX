using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void ReapplyStructuredTableStyleCommand_InfersNonGalleryBandingWhenOptionsChange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var headerFill = new CellColor(52, 58, 88);
        var bodyFill = new CellColor(245, 248, 250);
        var stripeFill = new CellColor(213, 231, 232);
        SetRangeStyle(wb, sheet, 1, 1, 1, 3, headerFill, CellColor.White, bold: true);
        SetRangeStyle(wb, sheet, 2, 1, 2, 3, bodyFill, CellColor.Black, bold: false);
        SetRangeStyle(wb, sheet, 3, 1, 3, 3, stripeFill, CellColor.Black, bold: false);
        SetRangeStyle(wb, sheet, 4, 1, 4, 3, bodyFill, CellColor.Black, bold: false);
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            StyleName = "CorporateLedger",
            ShowRowStripes = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Sales"),
                new StructuredTableColumnModel(3, "Orders")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new ReapplyStructuredTableStyleCommand(sheet.Id, table.Id, showRowStripes: false);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var configured = sheet.StructuredTables.Should().ContainSingle().Subject;
        configured.Should().NotBeSameAs(table);
        configured.StyleName.Should().Be("CorporateLedger");
        configured.ShowRowStripes.Should().BeFalse();
        StyleAt(wb, sheet, 1, 1).FillColor.Should().Be(headerFill);
        StyleAt(wb, sheet, 1, 1).FontColor.Should().Be(CellColor.White);
        StyleAt(wb, sheet, 2, 1).FillColor.Should().Be(bodyFill);
        StyleAt(wb, sheet, 3, 1).FillColor.Should().Be(bodyFill);
        StyleAt(wb, sheet, 4, 1).FillColor.Should().Be(bodyFill);

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        StyleAt(wb, sheet, 3, 1).FillColor.Should().Be(stripeFill);
    }

    [Fact]
    public void ReapplyStructuredTableStyleCommand_StylesInsertedTotalsRowForNonGalleryStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Below table"));
        var headerFill = new CellColor(34, 96, 89);
        var bodyFill = new CellColor(241, 247, 244);
        var stripeFill = new CellColor(219, 237, 230);
        SetRangeStyle(wb, sheet, 1, 1, 1, 3, headerFill, CellColor.White, bold: true);
        SetRangeStyle(wb, sheet, 2, 1, 2, 3, bodyFill, CellColor.Black, bold: false);
        SetRangeStyle(wb, sheet, 3, 1, 3, 3, stripeFill, CellColor.Black, bold: false);
        SetRangeStyle(wb, sheet, 4, 1, 4, 3, bodyFill, CellColor.Black, bold: false);
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            StyleName = "CorporateLedger",
            ShowRowStripes = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "count")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new CompositeWorkbookCommand(
            "Table Style Options",
            [
                new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: true),
                new ReapplyStructuredTableStyleCommand(sheet.Id, table.Id)
            ]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var configured = sheet.StructuredTables.Should().ContainSingle().Subject;
        configured.StyleName.Should().Be("CorporateLedger");
        configured.TotalsRowShown.Should().BeTrue();
        StyleAt(wb, sheet, 5, 1).FillColor.Should().Be(headerFill);
        StyleAt(wb, sheet, 5, 1).FontColor.Should().Be(CellColor.White);
        StyleAt(wb, sheet, 5, 1).Bold.Should().BeTrue();
        sheet.GetValue(5, 1).Should().Be(new TextValue("Total"));
        sheet.GetValue(6, 1).Should().Be(new TextValue("Below table"));

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        sheet.GetValue(5, 1).Should().Be(new TextValue("Below table"));
        sheet.GetValue(6, 1).Should().Be(BlankValue.Instance);
    }
}
