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
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["North"]));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, ["Open"], IncludeBlank: true));
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
    public void ApplyStructuredTableFiltersCommand_UsesZeroBasedFilterColumnIdsLoadedFromXlsx()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var table = CreateSalesTable(sheet);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["North"]));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, ["Open"], IncludeBlank: true));
        sheet.StructuredTables.Add(table);
        var ctx = new SimpleCtx(wb);

        var outcome = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain([3u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain([1u, 2u, 5u]);
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

    [Fact]
    public void RefreshStructuredTableTotalsCommand_MaterializesLabelsAndCommonFunctionsWithUndo()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "count")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new SimpleCtx(wb);
        var command = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(5, 1).Should().Be(new TextValue("Total"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(45));
        sheet.GetValue(5, 3).Should().Be(new NumberValue(2));

        command.Revert(ctx);

        sheet.GetValue(5, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(5, 2).Should().Be(BlankValue.Instance);
        sheet.GetValue(5, 3).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void CreateStyledStructuredTableCommand_AppliesTableMetadataAndBandedStylesAsOneUndoableOperation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var ctx = new SimpleCtx(wb);
        var preexistingBodyStyleId = wb.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(192, 0, 0),
            Bold = true
        });
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId = preexistingBodyStyleId;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var command = new CreateStyledStructuredTableCommand(
            sheet.Id,
            range,
            "TableStyleMedium2",
            firstRowHasHeaders: true,
            new StructuredTableStyleBanding(
                HeaderFill: new CellColor(31, 78, 121),
                OddRowFill: new CellColor(222, 235, 247),
                EvenRowFill: new CellColor(255, 255, 255),
                HeaderFontColor: CellColor.White));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().ContainSingle()
            .Which.StyleName.Should().Be("TableStyleMedium2");
        var headerStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId);
        headerStyle.FillColor.Should().Be(new CellColor(31, 78, 121));
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.Bold.Should().BeTrue();
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.StyleId)
            .FillColor.Should().Be(new CellColor(255, 255, 255));
        var bodyStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId);
        bodyStyle.FillColor.Should().Be(new CellColor(222, 235, 247));
        bodyStyle.FontColor.Should().Be(CellColor.Black);
        bodyStyle.Bold.Should().BeFalse();

        command.Revert(ctx);

        sheet.StructuredTables.Should().BeEmpty();
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId)
            .Should().Be(wb.GetStyle(StyleId.Default));
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId)
            .Should().Be(wb.GetStyle(preexistingBodyStyleId));
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

    private static void SeedTotalsTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Orders"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), BlankValue.Instance);
    }
}
