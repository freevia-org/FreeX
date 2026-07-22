using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R68-io-cell-value-types-6-2: Paste Special Multiply/Divide overflow (e.g. 1E200 * 1E200) produced
/// a ScalarValue.NumberValue(double.PositiveInfinity), which XLSX-saves as the literal TEXT
/// "Infinity" instead of Excel's #NUM! for an overflowing arithmetic result. Fixed by extending
/// PasteArithmetic.ApplyOperation's NaN guard to also catch double.IsInfinity(result) and return
/// ErrorValue.Num.
/// </summary>
public sealed class R68_PasteSpecialOverflowNumErrorTests
{
    [Fact]
    public void MultiplyOperation_OverflowingResult_YieldsNumErrorNotInfinity()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceCell = Cell.FromValue(new NumberValue(1e200));
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(1e200)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Multiply));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(ErrorValue.Num, "an overflowing Paste Special operation must be #NUM!, not a literal Infinity value");
    }

    [Fact]
    public void MultiplyOperation_NormalResult_CombinesNormally_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceCell = Cell.FromValue(new NumberValue(2));
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(3)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Multiply));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void DivideOperation_DivideByZero_StillYieldsDivByZeroError_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceCell = Cell.FromValue(new NumberValue(0));
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(10)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Divide));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(ErrorValue.DivByZero);
    }
}
