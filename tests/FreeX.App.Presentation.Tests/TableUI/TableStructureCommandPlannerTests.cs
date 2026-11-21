using FluentAssertions;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TableUI;

public sealed class TableStructureCommandPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // Table1 = A1:C5 (header + 4 data rows), optional totals row makes it A1:C5 with row 5 as totals.
    private static (Workbook Workbook, Sheet Sheet) Build(bool totals = false)
    {
        var workbook = new Workbook("t");
        var sheet = workbook.AddSheet("S");
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TotalsRowShown = totals,
        });
        return (workbook, sheet);
    }

    private static GridRange Sel(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    [Fact]
    public void RowsAbove_UsesTopOfSelectionAndCountsSelectedBodyRows()
    {
        var (_, sheet) = Build();

        var plan = TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 3, 1, 4, 2), TableStructureAction.InsertRowsAbove);

        plan.Succeeded.Should().BeTrue();
        plan.Command.Should().BeOfType<InsertTableRowsCommand>();
        plan.Label.Should().Be("Insert Table Rows");
    }

    [Fact]
    public void RowsAbove_OnTheHeaderRow_IsNotAvailable()
    {
        var (_, sheet) = Build();

        var plan = TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 1, 1, 1, 1), TableStructureAction.InsertRowsAbove);

        plan.Succeeded.Should().BeFalse();
        plan.Failure.Should().Be(TableStructureFailure.NeedsBodyCell);
    }

    [Fact]
    public void RowsBelow_OnTheHeaderRow_InsertsAtTheTopOfTheBody()
    {
        var (_, sheet) = Build();

        var plan = TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 1, 2, 1, 2), TableStructureAction.InsertRowsBelow);

        plan.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void RowsBelow_OnTheTotalsRow_IsNotAvailable()
    {
        var (_, sheet) = Build(totals: true);

        TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 5, 1, 5, 1), TableStructureAction.InsertRowsBelow)
            .Failure.Should().Be(TableStructureFailure.NeedsBodyCell);
        TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 5, 1, 5, 1), TableStructureAction.DeleteRows)
            .Failure.Should().Be(TableStructureFailure.NeedsBodyCell);
    }

    [Fact]
    public void OutsideAnyTable_ReportsNotInTable()
    {
        var (_, sheet) = Build();

        foreach (var action in Enum.GetValues<TableStructureAction>())
        {
            TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 20, 20, 20, 20), action)
                .Failure.Should().Be(TableStructureFailure.NotInTable, action.ToString());
        }

        TableStructureCommandPlanner.Plan(null, Sel(sheet, 2, 2, 2, 2), TableStructureAction.DeleteRows)
            .Failure.Should().Be(TableStructureFailure.NotInTable);
    }

    [Theory]
    [InlineData(TableStructureAction.InsertColumnsLeft, typeof(InsertTableColumnsCommand))]
    [InlineData(TableStructureAction.InsertColumnsRight, typeof(InsertTableColumnsCommand))]
    [InlineData(TableStructureAction.DeleteColumns, typeof(DeleteTableColumnsCommand))]
    [InlineData(TableStructureAction.DeleteRows, typeof(DeleteTableRowsCommand))]
    [InlineData(TableStructureAction.InsertRowsBelow, typeof(InsertTableRowsCommand))]
    public void ActionsPlanTheMatchingCoreCommand(TableStructureAction action, Type commandType)
    {
        var (_, sheet) = Build();

        var plan = TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 2, 2, 3, 2), action);

        plan.Succeeded.Should().BeTrue();
        plan.Command.Should().BeOfType(commandType);
    }

    [Fact]
    public void PlannedCommands_ExecuteAgainstTheModelWithExcelSemantics()
    {
        var workbook = new Workbook("t");
        var sheet = workbook.AddSheet("S");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        for (uint r = 2; r <= 4; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r * 10));
        }

        new CreateStructuredTableCommand(sheet.Id, Sel(sheet, 1, 1, 4, 2)).Apply(ctx).Success.Should().BeTrue();

        // Selecting rows 2-3 and choosing "Table Rows Below" inserts two rows after row 3.
        var below = TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 2, 1, 3, 1), TableStructureAction.InsertRowsBelow);
        below.Command!.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Single().Range.End.Row.Should().Be(6);
        sheet.GetCell(6, 1)!.Value.Should().Be(new NumberValue(4));

        // Selecting the second column and choosing "Table Columns to the Right" adds a column after it.
        var right = TableStructureCommandPlanner.Plan(sheet, Sel(sheet, 2, 2, 2, 2), TableStructureAction.InsertColumnsRight);
        right.Command!.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Single().Range.End.Col.Should().Be(3);
        sheet.StructuredTables.Single().Columns.Select(c => c.Name).Should().Equal("A", "B", "Column1");
    }
}
