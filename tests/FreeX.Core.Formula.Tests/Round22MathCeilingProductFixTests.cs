using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-22 regression tests:
///  - R22-math-trig-functions-2: legacy CEILING must reject BOTH sign-mismatch
///    directions (number negative / significance positive, mirroring FLOOR's
///    `n * sig &lt; 0` guard), not just the number-positive/significance-negative case.
///  - R22-math-trig-functions-3: PRODUCT() over a range with no numeric values
///    must return 0 (Excel quirk), matching this codebase's own SUBTOTAL(6,...)/
///    AGGREGATE(6,...) special-casing of "no numbers found" to 0.
/// </summary>
public sealed class Round22MathCeilingProductFixTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Ceiling_NegativeNumberPositiveSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(-2.5,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Ceiling_PositiveNumberNegativeSignificance_StillReturnsNumError()
    {
        // Guard against regressing the direction that was already correctly handled.
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(2.3,-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Ceiling_BothNegative_StillComputesNormally()
    {
        // Matching signs (both negative) must remain a valid, non-error computation.
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(-2.5,-2)", sheet).Should().Be(new NumberValue(-4));
    }

    [Fact]
    public void Product_AllBlankRange_ReturnsZero()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=PRODUCT(A1:A3)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Product_RangeWithNumbers_StillMultipliesCorrectly()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(2)), (2, 1, new NumberValue(3)));
        _eval.Evaluate("=PRODUCT(A1:A2)", sheet).Should().Be(new NumberValue(6));
    }
}
