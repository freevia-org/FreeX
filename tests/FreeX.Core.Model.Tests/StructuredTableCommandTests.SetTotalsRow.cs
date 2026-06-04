using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void SetStructuredTableTotalsRowCommand_Show_InsertsWorksheetRowExpandsTableAndRefreshesTotals()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Below table"));
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            TotalsRowShown = false,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "count")
            }
        };
        var lowerTable = new StructuredTableModel
        {
            Id = 8,
            Name = "Inventory",
            DisplayName = "Inventory",
            Range = new GridRange(new CellAddress(sheet.Id, 8, 1), new CellAddress(sheet.Id, 9, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Sku"),
                new StructuredTableColumnModel(2, "Qty")
            }
        };
        sheet.StructuredTables.Add(table);
        sheet.StructuredTables.Add(lowerTable);
        var ctx = new TestCommandContext(wb);
        var command = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var configured = sheet.StructuredTables.Single(candidate => candidate.Id == table.Id);
        configured.Should().NotBeSameAs(table);
        configured.Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)));
        configured.TotalsRowShown.Should().BeTrue();
        configured.TotalsRowCount.Should().Be(1);
        sheet.GetValue(5, 1).Should().Be(new TextValue("Total"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(45));
        sheet.GetValue(5, 3).Should().Be(new NumberValue(2));
        sheet.GetValue(6, 1).Should().Be(new TextValue("Below table"));
        sheet.StructuredTables.Single(candidate => candidate.Id == lowerTable.Id).Range
            .Should().Be(new GridRange(new CellAddress(sheet.Id, 9, 1), new CellAddress(sheet.Id, 10, 2)));

        command.Revert(ctx);

        sheet.StructuredTables.Should().Equal(table, lowerTable);
        sheet.GetValue(5, 1).Should().Be(new TextValue("Below table"));
        sheet.GetValue(5, 2).Should().Be(BlankValue.Instance);
        sheet.GetValue(6, 1).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void SetStructuredTableTotalsRowCommand_Hide_DeletesTotalsRowShrinksTableAndRestoresOnUndo()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Below table"));
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TotalsRowShown = true,
            TotalsRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "count")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var configured = sheet.StructuredTables.Should().ContainSingle().Subject;
        configured.Should().NotBeSameAs(table);
        configured.Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)));
        configured.TotalsRowShown.Should().BeFalse();
        configured.TotalsRowCount.Should().Be(0);
        sheet.GetValue(5, 1).Should().Be(new TextValue("Below table"));
        sheet.GetValue(5, 2).Should().Be(BlankValue.Instance);
        sheet.GetValue(6, 1).Should().Be(BlankValue.Instance);

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        sheet.GetValue(5, 1).Should().Be(new TextValue("Total"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(45));
        sheet.GetValue(5, 3).Should().Be(new NumberValue(2));
        sheet.GetValue(6, 1).Should().Be(new TextValue("Below table"));
    }
}
