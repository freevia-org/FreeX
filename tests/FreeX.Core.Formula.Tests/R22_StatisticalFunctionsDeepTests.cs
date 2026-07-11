using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-22 review fixes for src/FreeX.Core.Formula/FormulaEvaluator.FunctionClassification.cs
/// and src/FreeX.Core.Formula/BuiltInFunctions.cs:
///   R22-statistical-functions-deep-1: DEVSQ silently dropped a direct text-numeral literal
///     argument (e.g. DEVSQ(1,2,"3")) instead of coercing it, because DEVSQ was missing from
///     the DirectTextCoercingAggregates set that every sibling variadic stat function
///     (AVEDEV/VAR/STDEV/MODE) already appears in.
///   R22-statistical-functions-deep-2: MODE.MULT was entirely unregistered (#NAME?) instead of
///     returning the array of every most-frequent value in first-appearance order.
///   (TREND/GROWTH/LINEST/LOGEST were implemented alongside MODE.MULT in this fix wave but
///     were deliberately reverted before merge -- see the comment above ModeMult in
///     BuiltInFunctions.cs -- so this suite only covers DEVSQ and MODE.MULT.)
/// </summary>
public sealed class R22_StatisticalFunctionsDeepTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Devsq_DirectTextNumeralLiteral_IsCoercedNotDropped()
    {
        // Pre-fix: the "3" literal was silently dropped, giving DEVSQ(1,2) = 0.5.
        // Post-fix: "3" coerces to 3, mean = 2, DEVSQ = 1+0+1 = 2.
        _eval.Evaluate("=DEVSQ(1,2,\"3\")", MakeSheet())
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void ModeMult_BimodalData_ReturnsBothModesInFirstAppearanceOrder()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(2)), (4, 1, new NumberValue(2)),
            (5, 1, new NumberValue(3)));

        var result = _eval.Evaluate("=MODE.MULT(A1:A5)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void ModeMult_NoRepeats_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=MODE.MULT(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
