using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r226: three commands found by grepping for the SHAPE rather than sweeping a family -- an early
/// return, before any mutation, whose condition is an equality or emptiness test and whose outcome
/// is a plain success. Two of them had been recorded as judged-sound in earlier rounds on the
/// strength of a caller gate, and both carry their own already-matches check that fires regardless.
/// A command with its own check is telling you the caller gate is not being relied on.
/// </summary>
public sealed class R226_DetectedButUnsignalledNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static StructuredTableModel Table(Sheet sheet, bool totalsRowShown)
    {
        for (uint row = 1; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"r{row}"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            TotalsRowShown = totalsRowShown,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        sheet.StructuredTables.Add(table);
        return table;
    }

    [Fact]
    public void SettingATablesTotalsRowToTheStateItIsAlreadyIn_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var table = Table(sheet, totalsRowShown: false);

        new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void TurningATablesTotalsRowOn_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var table = Table(sheet, totalsRowShown: false);

        var outcome = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: true)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.StructuredTables[0].TotalsRowShown.Should().BeTrue();
    }

    [Fact]
    public void ApplyingTableFiltersThatAreAlreadyInEffect_ReportsNoOp()
    {
        // No filters requested and none hiding anything -- FilterHiddenRowsAlreadyMatch fires, which
        // is the path that has existed all along and returned a plain success.
        var (_, sheet, ctx) = Fixture();
        var table = Table(sheet, totalsRowShown: false);

        new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void AGroupedEditWithNoEdits_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new GroupedEditCellsCommand([sheet.Id], sheet.Id, []).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void AGroupedEditWithNoSheets_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new GroupedEditCellsCommand(
                [],
                sheet.Id,
                [(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("x")))])
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void AGroupedEditWithSomethingToDo_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        var outcome = new GroupedEditCellsCommand(
                [sheet.Id],
                sheet.Id,
                [(new CellAddress(sheet.Id, 8, 1), Cell.FromValue(new TextValue("x")))])
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
    }
}
