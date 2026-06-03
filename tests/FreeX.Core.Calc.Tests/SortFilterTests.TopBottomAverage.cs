using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class SortFilterTests
{
    [Fact]
    public void TopBottomFilterCommand_TopN_KeepsHighestNumericRowsAndUndoRestores()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 2, top: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
        sheet.FilterHiddenRows.Should().NotContain(5u);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void TopBottomFilterCommand_BottomN_KeepsLowestNumericRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 2, top: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
        sheet.FilterHiddenRows.Should().Contain(5u);
    }

    [Fact]
    public void TopBottomFilterCommand_TopPercent_KeepsCeilingPercentageOfNumericRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = TopBottomFilterCommand.Percent(sid, range, filterColOffset: 0, percent: 50, top: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
        sheet.FilterHiddenRows.Should().NotContain(5u);
    }

    [Fact]
    public void TopBottomFilterCommand_BottomPercent_KeepsCeilingPercentageOfNumericRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = TopBottomFilterCommand.Percent(sid, range, filterColOffset: 0, percent: 25, top: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
        sheet.FilterHiddenRows.Should().Contain(5u);
    }

    [Fact]
    public void AverageFilterCommand_AboveAverage_KeepsRowsGreaterThanColumnAverage()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = new AverageFilterCommand(sid, range, filterColOffset: 0, above: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
        sheet.FilterHiddenRows.Should().NotContain(5u);
    }

    [Fact]
    public void AverageFilterCommand_BelowAverage_KeepsRowsLessThanColumnAverage()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = new AverageFilterCommand(sid, range, filterColOffset: 0, above: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
        sheet.FilterHiddenRows.Should().Contain(5u);
    }
}
