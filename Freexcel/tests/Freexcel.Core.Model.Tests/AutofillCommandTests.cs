using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class AutofillCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void FillValue_Down_RepeatsSourceValue()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(42));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(42));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(42));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void FillNumberSeries_Down_ContinuesStepFromSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(3, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(7));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void FillNumberSeries_Right_ContinuesStepFromSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 1, 5));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(1, 3).Should().Be(new NumberValue(8));
        sheet.GetValue(1, 4).Should().Be(new NumberValue(11));
        sheet.GetValue(1, 5).Should().Be(new NumberValue(14));
    }

    [Fact]
    public void FillDateSeries_Down_ContinuesDayStepFromSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 3)));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        ((DateTimeValue)sheet.GetValue(3, 1)).ToDateTime().Should().Be(new DateTime(2026, 5, 5));
        ((DateTimeValue)sheet.GetValue(4, 1)).ToDateTime().Should().Be(new DateTime(2026, 5, 7));
    }

    [Fact]
    public void FillFormula_Down_IncrementsRowReferences()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("A1+B1"));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetCell(2, 1)!.FormulaText.Should().Be("A2+B2");
        sheet.GetCell(3, 1)!.FormulaText.Should().Be("A3+B3");
    }

    [Fact]
    public void FillFormula_PreservesFunctionNames_WithDigitSuffix()
    {
        // Regression: regex shift incorrectly incremented digits inside function names
        // e.g. =LOG10(A1) shifted down 1 row would become =LOG11(A2) with the old regex.
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("LOG10(A1)"));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetCell(2, 1)!.FormulaText.Should().Be("LOG10(A2)");
    }

    [Fact]
    public void FillFormula_PreservesAbsoluteRefs()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("$A$1+B1"));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetCell(2, 1)!.FormulaText.Should().Be("$A$1+B2");
    }

    [Fact]
    public void FillRevert_RestoresOriginalCells()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, new NumberValue(10));
        sheet.SetCell(target, new NumberValue(99));

        var cmd = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target));
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void Autofill_RejectsLockedTargetsOnProtectedSheet()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, new TextValue("source"));
        sheet.SetCell(target, new TextValue("target"));
        sheet.IsProtected = true;

        var outcome = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(target).Should().Be(new TextValue("target"));
    }

    [Fact]
    public void Autofill_AllowsUnlockedTargetsOnProtectedSheet()
    {
        var (workbook, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        var unlockedStyle = workbook.RegisterStyle(new CellStyle { Locked = false });
        sheet.SetCell(source, new TextValue("source"));
        var targetCell = Cell.FromValue(new TextValue("target"));
        targetCell.StyleId = unlockedStyle;
        sheet.SetCell(target, targetCell);
        sheet.IsProtected = true;

        var outcome = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(target).Should().Be(new TextValue("source"));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
