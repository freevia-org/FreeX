using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class ConsolidateCommandTests
{
    [Fact]
    public void ConsolidateCommand_SumsNumericCellsByPositionAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var source2 = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 2, 5));
        var destination = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("ignored"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(40));
        sheet.SetCell(destination, new NumberValue(999));

        var command = new ConsolidateCommand([source1, source2], destination);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(4, 1).Should().Be(new NumberValue(11));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(22));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(30));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(44));

        command.Revert(ctx);

        sheet.GetValue(destination).Should().Be(new NumberValue(999));
        sheet.GetCell(4, 2).Should().BeNull();
        sheet.GetCell(5, 1).Should().BeNull();
        sheet.GetCell(5, 2).Should().BeNull();
    }

    [Theory]
    [InlineData(ConsolidateFunction.Count, 3)]
    [InlineData(ConsolidateFunction.Average, 20)]
    [InlineData(ConsolidateFunction.Max, 30)]
    [InlineData(ConsolidateFunction.Min, 10)]
    [InlineData(ConsolidateFunction.Product, 6000)]
    [InlineData(ConsolidateFunction.CountNumbers, 3)]
    public void ConsolidateCommand_AppliesSelectedFunctionByPosition(ConsolidateFunction function, double expected)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var source2 = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2));
        var source3 = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3));
        var destination = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        var command = new ConsolidateCommand([source1, source2, source3], destination, function);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(destination).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void ConsolidateCommand_RejectsDifferentSizedSourceRanges()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var source2 = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 3, 5));

        var command = new ConsolidateCommand([source1, source2], new CellAddress(sheet.Id, 5, 1));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("same size");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
