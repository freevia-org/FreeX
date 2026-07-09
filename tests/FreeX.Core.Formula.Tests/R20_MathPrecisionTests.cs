using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-20 findings R20-math-trig-functions-1 and
/// R20-math-trig-functions-2: FLOOR/CEILING (and the *.MATH/ISO/PRECISE variants)
/// and QUOTIENT computed n/significance via raw IEEE-754 double division with no
/// Excel-style precision correction, so a value that is mathematically an exact
/// multiple landed a full bucket (or an off-by-one integer quotient) wrong.
/// </summary>
public sealed class R20_math_precision_Tests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    // R20-math-trig-functions-1: FLOOR(0.3, 0.1) must be 0.3 (0.3 is an exact
    // multiple of 0.1), not 0.2 (double math: 0.3/0.1 == 2.9999999999999996).
    [InlineData("=FLOOR(0.3,0.1)", 0.3)]
    // R20-math-trig-functions-1: CEILING(4.2, 0.3) must be 4.2 (4.2 is an exact
    // multiple of 0.3), not 4.5 (double math: 4.2/0.3 == 14.000000000000002).
    [InlineData("=CEILING(4.2,0.3)", 4.2)]
    // Same defect inherited by the *.MATH/ISO/PRECISE variants sharing the pattern.
    [InlineData("=CEILING.MATH(4.2,0.3)", 4.2)]
    [InlineData("=FLOOR.MATH(4.2,0.3)", 4.2)]
    [InlineData("=ISO.CEILING(4.2,0.3)", 4.2)]
    [InlineData("=CEILING.PRECISE(4.2,0.3)", 4.2)]
    [InlineData("=FLOOR.PRECISE(4.2,0.3)", 4.2)]
    // R20-math-trig-functions-2: QUOTIENT(0.3, 0.1) must be 3 (0.3 divided by 0.1
    // is exactly 3), not 2 (double math truncates 2.9999999999999996 down to 2).
    [InlineData("=QUOTIENT(0.3,0.1)", 3)]
    public void ExactMultipleInputs_MatchExcelPrecisionCorrectedResult(string formula, double expected)
    {
        var result = _eval.Evaluate(formula, MakeSheet());
        result.Should().BeOfType<NumberValue>(formula);
        ((NumberValue)result).Value.Should().Be(expected);
    }

    [Fact]
    public void FloorMath_NegativeExactMultiple_TruncateModeMatchesExcel()
    {
        // FLOOR.MATH(-4.2, 0.3, 1) uses the truncate-toward-zero branch for
        // negative numbers with a nonzero mode; -4.2 is an exact multiple of 0.3
        // so it must stay -4.2, not drift to -4.5 from the same raw-division bug.
        var result = _eval.Evaluate("=FLOOR.MATH(-4.2,0.3,1)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().Be(-4.2);
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }
}
