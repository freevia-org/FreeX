using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r247: ApplyStructuredTableStyleCommand, and the payoff from fixing its children first.
/// <para>
/// This command is a composite in all but name: one ConfigureStructuredTableStyleOptionsCommand
/// (fixed in r219) plus a set of ApplyStyleCommands (fixed in r246). Once every child can say
/// whether it changed anything, the parent can simply bubble that up -- the same mechanism
/// CompositeWorkbookCommand has used all along, which r224 relied on to clear RemoveSheetsCommand.
/// </para>
/// <para>
/// r231 recorded ReapplyStructuredTableStyleCommand as inheriting this command's DEFECT through
/// delegation. It now inherits the fix instead, so this round clears two entries with one change.
/// </para>
/// </summary>
public sealed class R247_StructuredTableStyleNoOpTests
{
    private static StructuredTableStyleBanding Banding() =>
        new(CellColor.Black, CellColor.White, CellColor.White, CellColor.White);

    private static (Sheet Sheet, StructuredTableModel Table, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"r{row}"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Amount"));
        sheet.StructuredTables.Add(table);
        return (sheet, table, new TestCommandContext(workbook));
    }

    [Fact]
    public void ApplyingTheSameTableStyleTwice_ReportsNoOpTheSecondTime()
    {
        var (sheet, table, ctx) = Fixture();

        new ApplyStructuredTableStyleCommand(sheet.Id, table.Id, Banding())
            .Apply(ctx)
            .Success.Should().BeTrue();

        new ApplyStructuredTableStyleCommand(sheet.Id, table.Id, Banding())
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingATableStyleOption_DoesNotReportNoOp()
    {
        var (sheet, table, ctx) = Fixture();
        new ApplyStructuredTableStyleCommand(sheet.Id, table.Id, Banding())
            .Apply(ctx);

        var outcome = new ApplyStructuredTableStyleCommand(
                sheet.Id, table.Id, Banding(), showFirstColumn: true)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.StructuredTables[0].ShowFirstColumn.Should().BeTrue();
    }

    [Fact]
    public void ReapplyInheritsTheFix()
    {
        // r231 recorded this command as inheriting the inner command's defect. The same delegation
        // now carries the fix, which is the point of having fixed the inner one first.
        var (sheet, table, ctx) = Fixture();
        new ApplyStructuredTableStyleCommand(sheet.Id, table.Id, Banding())
            .Apply(ctx);

        new ReapplyStructuredTableStyleCommand(sheet.Id, table.Id).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }
}
