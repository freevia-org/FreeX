using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class FilterCommandTests
{
    [Fact]
    public void TopBottomFilter_KeepsTopTiesByRowAndPreservesRowsOutsideRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(9));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("n/a"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(8));
        sheet.FilterHiddenRows.UnionWith([2u, 20u]);

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 6, 1));
        var command = new TopBottomFilterCommand(sheet.Id, range, 0, count: 2, top: true);

        var outcome = command.Apply(new SimpleCtx(wb));

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u, 20u]);
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
