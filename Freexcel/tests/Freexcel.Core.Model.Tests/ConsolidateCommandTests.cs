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
    public void ConsolidateCommand_CreateLinksWritesSourceFormulaByPosition()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var source2 = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 1, 4));
        var destination = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(30));

        var command = new ConsolidateCommand(
            [source1, source2],
            destination,
            ConsolidateFunction.Average,
            createLinksToSourceData: true);

        command.Apply(ctx).Success.Should().BeTrue();

        var cell = sheet.GetCell(destination);
        cell.Should().NotBeNull();
        cell!.Value.Should().Be(new NumberValue(20));
        cell.FormulaText.Should().Be("AVERAGE(A1,D1)");
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

    [Fact]
    public void ConsolidateCommand_UsesTopRowAndLeftColumnLabels()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var source2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 3, 7));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 7), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 7), new NumberValue(13));

        var command = new ConsolidateCommand(
            [source1, source2],
            new CellAddress(sheet.Id, 6, 1),
            ConsolidateFunction.Sum,
            useTopRowLabels: true,
            useLeftColumnLabels: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(6, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(6, 2).Should().Be(new TextValue("Q1"));
        sheet.GetValue(6, 3).Should().Be(new TextValue("Q2"));
        sheet.GetValue(7, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(8, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(7, 2).Should().Be(new NumberValue(23));
        sheet.GetValue(7, 3).Should().Be(new NumberValue(31));
        sheet.GetValue(8, 2).Should().Be(new NumberValue(37));
        sheet.GetValue(8, 3).Should().Be(new NumberValue(45));
    }

    [Fact]
    public void ConsolidateCommand_CreateLinksWritesSourceFormulaByLabels()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var source2 = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 2, 5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(7));

        var command = new ConsolidateCommand(
            [source1, source2],
            new CellAddress(sheet.Id, 5, 1),
            ConsolidateFunction.Sum,
            useTopRowLabels: true,
            useLeftColumnLabels: true,
            createLinksToSourceData: true);

        command.Apply(ctx).Success.Should().BeTrue();

        var cell = sheet.GetCell(6, 2);
        cell.Should().NotBeNull();
        cell!.Value.Should().Be(new NumberValue(17));
        cell.FormulaText.Should().Be("SUM(B2,E2)");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
