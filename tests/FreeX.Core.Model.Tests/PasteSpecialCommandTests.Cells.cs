using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteSpecialCommandTests
{
    [Fact]
    public void PasteSpecialCellsCommand_TransposesCellsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("A"))),
            (new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("B"))),
            (new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("C"))),
            (new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("D")))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            source,
            new CellAddress(sheet.Id, 5, 5),
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(5, 5).Should().Be(new TextValue("A"));
        sheet.GetValue(6, 5).Should().Be(new TextValue("B"));
        sheet.GetValue(5, 6).Should().Be(new TextValue("C"));
        sheet.GetValue(6, 6).Should().Be(new TextValue("D"));

        command.Revert(ctx);

        sheet.GetCell(5, 5).Should().BeNull();
        sheet.GetCell(6, 6).Should().BeNull();
    }

    [Fact]
    public void PasteSpecialCellsCommand_AddOperationCombinesNumericValues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(dest, new NumberValue(10));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void PasteSpecialCellsCommand_AddOperationPreservesStyleOnlyDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(dest.Row, dest.Col, destinationStyle);
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(dest)!;
        pasted.Value.Should().Be(new NumberValue(5));
        pasted.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteSpecialCellsCommand_AddOperationUndoRestoresStyleOnlyDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(dest.Row, dest.Col, destinationStyle);
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        command.Revert(ctx);

        sheet.GetCell(dest).Should().BeNull();
        sheet.GetStyleOnly(dest.Row, dest.Col).Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteSpecialCellsCommand_DivideByZeroReturnsError()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(dest, new NumberValue(10));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Divide));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void PasteSpecialCellsCommand_AddOperationTreatsDatesAsExcelSerials()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        var startDate = DateTimeValue.FromDateTime(new DateTime(2026, 5, 29));
        sheet.SetCell(dest, startDate);
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(2)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 31)));
    }

    [Fact]
    public void PasteSpecialCellsCommand_AddOperationTreatsBooleansAsExcelNumbers()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(dest, new NumberValue(10));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new BoolValue(true)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(new NumberValue(11));
    }

    [Fact]
    public void PasteSpecialCellsCommand_RejectsInvalidOperation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(dest, new NumberValue(10));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: (PasteSpecialOperation)99));

        command.Apply(ctx).Success.Should().BeFalse();
        sheet.GetValue(dest).Should().Be(new NumberValue(10));
    }

}
