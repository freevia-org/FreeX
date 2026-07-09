using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class R15_filter_b_Tests
{
    [Fact]
    public void TopBottomFilter_Top2OverThreeTiedValues_KeepsAllTiedRowsAtBoundary()
    {
        // Excel Top-N is threshold-based: it keeps every row whose value is >= the Nth-largest
        // value, not just the first N rows by row index. Top 2 over {100, 100, 100, 50} must
        // keep all three 100 rows visible and hide only the 50 row.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(50));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var command = new TopBottomFilterCommand(sheet.Id, range, 0, count: 2, top: true);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([5u]);
    }

    [Fact]
    public void TopBottomFilter_Bottom1OverTwoTiedLowValues_KeepsBothTiedRowsAtBoundary()
    {
        // Bottom 1 over {10, 10, 50} must keep both rows with the (tied) smallest value, 10,
        // and hide only the 50 row — not hide one of the tied 10 rows just because count == 1.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(50));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 1));
        var command = new TopBottomFilterCommand(sheet.Id, range, 0, count: 1, top: false);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);
    }
}
