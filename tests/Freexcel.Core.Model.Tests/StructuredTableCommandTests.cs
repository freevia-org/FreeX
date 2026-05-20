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

    [Fact]
    public void ApplyStructuredTableFiltersCommand_HidesRowsThatDoNotMatchTableFilterColumns()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, ["North"]));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(2, ["Open"], IncludeBlank: true));
        sheet.StructuredTables.Add(table);
        sheet.FilterHiddenRows.Add(20u);
        var ctx = new SimpleCtx(wb);
        var command = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain([3u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain([1u, 2u, 5u]);
        sheet.FilterHiddenRows.Should().Contain(20u);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([20u]);
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_ClearsRowsInTableWhenNoFilterColumnsRemain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        sheet.StructuredTables.Add(table);
        sheet.FilterHiddenRows.UnionWith([2u, 3u, 20u]);
        var ctx = new SimpleCtx(wb);

        var outcome = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().NotContain([2u, 3u]);
        sheet.FilterHiddenRows.Should().Contain(20u);
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_RejectsUnknownFilterColumnWithoutChangingHiddenRows()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(99, ["North"]));
        sheet.StructuredTables.Add(table);
        sheet.FilterHiddenRows.UnionWith([2u, 20u]);
        var ctx = new SimpleCtx(wb);

        var outcome = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 20u]);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }

    private static StructuredTableModel CreateSalesTable(Sheet sheet) =>
        new()
        {
            Id = 7,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Status")
            }
        };

    private static void SeedTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), BlankValue.Instance);
    }
}
