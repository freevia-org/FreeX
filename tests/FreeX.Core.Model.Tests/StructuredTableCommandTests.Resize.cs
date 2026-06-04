using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void ResizeStructuredTableCommand_UpdatesRangeColumnsAndFiltersWithUndo()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), BlankValue.Instance);
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Status")
            },
            FilterColumns =
            {
                new StructuredTableFilterColumnModel(1, ["Open"]),
                new StructuredTableFilterColumnModel(9, ["Dropped"])
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var resized = sheet.StructuredTables.Should().ContainSingle().Subject;
        resized.Should().NotBeSameAs(table);
        resized.Range.Should().Be(newRange);
        resized.Columns.Select(column => (column.Id, column.Name))
            .Should()
            .Equal((1, "Region"), (2, "Status"), (3, "Status2"), (4, "Column4"));
        resized.FilterColumns.Should().ContainSingle().Which.ColumnId.Should().Be(1);
        resized.StyleName.Should().Be(table.StyleName);
        resized.ShowRowStripes.Should().BeTrue();

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }

    [Fact]
    public void ResizeStructuredTableCommand_RejectsMovedInvalidAndProtectedRanges()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        new ResizeStructuredTableCommand(
                sheet.Id,
                table.Id,
                new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 6, 2)))
            .Apply(ctx)
            .Success.Should().BeFalse();
        new ResizeStructuredTableCommand(
                sheet.Id,
                table.Id,
                new GridRange(new CellAddress(other.Id, 1, 1), new CellAddress(other.Id, 6, 2)))
            .Apply(ctx)
            .Success.Should().BeFalse();
        new ResizeStructuredTableCommand(
                sheet.Id,
                table.Id,
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2)))
            .Apply(ctx)
            .Success.Should().BeFalse();

        sheet.IsProtected = true;
        var protectedOutcome = new ResizeStructuredTableCommand(
            sheet.Id,
            table.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2))).Apply(ctx);

        protectedOutcome.Success.Should().BeFalse();
        protectedOutcome.ErrorMessage.Should().Contain("protected");
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }
}
