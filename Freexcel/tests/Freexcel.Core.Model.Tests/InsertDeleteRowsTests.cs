using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class InsertDeleteRowsTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void InsertRow_ShiftsCellsDown()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));

        new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1).Apply(ctx);

        sheet.GetValue(4, 1).Should().Be(new NumberValue(100));
        sheet.GetCell(3, 1).Should().BeNull();
    }

    [Fact]
    public void InsertRowRevert_RestoresOriginalState()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(3, 1).Should().Be(new NumberValue(100));
        sheet.GetCell(4, 1).Should().BeNull();
    }

    [Fact]
    public void DeleteRow_RemovesCellsAndShiftsUp()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1).Apply(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(30));
        sheet.GetCell(3, 1).Should().BeNull();
    }

    [Fact]
    public void DeleteRowRevert_RestoresCells()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(20));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void InsertRow_ShiftsMergedRegions()
    {
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.MergedRegions.Add(mergeRange);

        new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1).Apply(ctx);

        sheet.MergedRegions[0].Start.Row.Should().Be(4);
        sheet.MergedRegions[0].End.Row.Should().Be(5);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
