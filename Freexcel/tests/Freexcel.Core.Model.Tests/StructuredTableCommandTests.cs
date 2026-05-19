using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class StructuredTableCommandTests
{
    [Fact]
    public void CreateStructuredTableCommand_AddsNamedTableMetadataWithHeaders()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));

        var outcome = new CreateStructuredTableCommand(sheet.Id, range, "TableStyleMedium2").Apply(ctx);

        outcome.Success.Should().BeTrue();
        var table = sheet.StructuredTables.Should().ContainSingle().Subject;
        table.Name.Should().Be("Table1");
        table.DisplayName.Should().Be("Table1");
        table.Range.Should().Be(range);
        table.HasAutoFilter.Should().BeTrue();
        table.StyleName.Should().Be("TableStyleMedium2");
        table.ShowRowStripes.Should().BeTrue();
        table.Columns.Select(column => column.Name).Should().Equal("Region", "Sales");
    }

    [Fact]
    public void CreateStructuredTableCommand_GeneratesUniqueHeadersAndUndoRemovesTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), BlankValue.Instance);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 2))
        });
        var ctx = new SimpleCtx(wb);
        var command = new CreateStructuredTableCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            "TableStyleLight9");

        command.Apply(ctx).Success.Should().BeTrue();

        var table = sheet.StructuredTables.Last();
        table.Id.Should().Be(2);
        table.Name.Should().Be("Table2");
        table.Columns.Select(column => column.Name).Should().Equal("Name", "Name2", "Column3");

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Name.Should().Be("Table1");
    }

    [Fact]
    public void CreateStructuredTableCommand_CanCreateDefaultHeadersWhenFirstRowHasNoHeaders()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(120));
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));

        var outcome = new CreateStructuredTableCommand(
            sheet.Id,
            range,
            "TableStyleLight9",
            firstRowHasHeaders: false).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Single().Columns.Select(column => column.Name)
            .Should()
            .Equal("Column1", "Column2");
    }

    [Fact]
    public void CreateStructuredTableCommand_RejectsInvalidRangesWithoutChangingExistingTables()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var ctx = new SimpleCtx(wb);
        var existing = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2))
        };
        sheet.StructuredTables.Add(existing);

        var oneRow = new CreateStructuredTableCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2)),
            "TableStyleMedium2");
        var wrongSheet = new CreateStructuredTableCommand(
            sheet.Id,
            new GridRange(new CellAddress(other.Id, 1, 1), new CellAddress(other.Id, 2, 2)),
            "TableStyleMedium2");

        oneRow.Apply(ctx).Success.Should().BeFalse();
        wrongSheet.Apply(ctx).Success.Should().BeFalse();
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(existing);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
