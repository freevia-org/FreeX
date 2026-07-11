using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R23-error-propagation-1: AND()/OR() must propagate ANY error present among their (already
// evaluated) arguments, even when an earlier argument already determines the boolean outcome
// (a determining FALSE for AND, or a determining TRUE for OR). Excel evaluates every argument
// and lets an error win regardless of argument order; short-circuiting on the boolean result
// before scanning the remaining arguments for an error is a parity bug.
public sealed class AndOrErrorPropagationTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void And_PropagatesLaterErrorEvenWhenLeadingArgumentIsDeterminingFalse()
    {
        // AND short-circuits to FALSE on the first FALSE in real Excel's UI sense, but the
        // engine must still surface an error present later in the argument list.
        _eval.Evaluate("=AND(FALSE,1/0)", Sheet()).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Or_PropagatesLaterErrorEvenWhenLeadingArgumentIsDeterminingTrue()
    {
        _eval.Evaluate("=OR(TRUE,1/0)", Sheet()).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void And_PropagatesErrorFromRangeMemberEvenWhenAnotherArgumentIsDeterminingFalse()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.DivByZero); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));  // A2

        // FALSE is a determining value for AND, but A1:A2 contains an error that must win.
        _eval.Evaluate("=AND(FALSE,A1:A2)", sheet, workbook).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Or_PropagatesErrorFromRangeMemberEvenWhenAnotherArgumentIsDeterminingTrue()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.DivByZero); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(false)); // A2

        // TRUE is a determining value for OR, but A1:A2 contains an error that must win.
        _eval.Evaluate("=OR(TRUE,A1:A2)", sheet, workbook).Should().Be(ErrorValue.DivByZero);
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
