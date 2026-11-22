using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R77-precision-1-arithmetic-15sig: Excel rounds the result of every arithmetic operation
/// (+, -, *, /, ^) to 15 significant decimal digits, so that results which are mathematically
/// exact but land on ordinary IEEE-754 floating-point noise (e.g. 0.1+0.2-0.3, which raw double
/// arithmetic evaluates to ~5.5e-17) come out bit-exact instead (0). FreeX previously returned
/// the raw, unrounded double from these operators, diverging from Excel on such near-round
/// results.
///
/// Fixed via FormulaEvaluator.Operators.cs's new RoundTo15SignificantDigits helper -- O(1)
/// scaled Math.Round arithmetic (no string formatting/parsing/allocation, unlike the round-75
/// attempt at this same fix, which used value.ToString("G15")+double.TryParse on every
/// arithmetic op and was reverted for the resulting hot-path perf regression) -- applied to the
/// result of every binary +,-,*,/,^ evaluation (both the scalar fast path in
/// TryEvaluateNumericBinaryScalar and the general/array path in ArithNumberValues/
/// DivideNumberValues/PowerNumberValues).
/// </summary>
public sealed class R77_ArithmeticResult15SigRoundingTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void ClassicRoundingNoise_AddThenSubtract_IsExactlyZero()
    {
        // Raw IEEE-754 double arithmetic yields 0.1+0.2 == 0.30000000000000004, so
        // (0.1+0.2)-0.3 == 5.551115123125783e-17, not 0. Excel evaluates this to exactly 0,
        // and so must FreeX after the 15-sig-digit rounding fix.
        var sheet = MakeSheet();

        _eval.Evaluate("=0.1+0.2-0.3", sheet)
            .Should().Be(new NumberValue(0.0));
    }

    [Fact]
    public void OneThird_KeepsRawDouble_LikeExcel()
    {
        // Excel stores arithmetic results as full IEEE-754 doubles and only DISPLAYS 15 significant
        // digits: real Excel gives (1/3-0.333333333333333)*1 = 3.3306690738754696E-16 (verified via COM),
        // so =1/3 must stay the raw double rather than be rounded to 0.333333333333333.
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=1/3", sheet);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().Be(1.0 / 3.0);
    }

    [Fact]
    public void LargeMagnitudeAddition_SubResolutionAddendIsKept_LikeExcel()
    {
        // Real Excel: ((123456789012345+0.4)-123456789012345)*1 = 0.40625 (the double's own resolution
        // near 1.2e14 is 1/64) -- the addend is NOT absorbed by a 15-digit rounding.
        var sheet = MakeSheet();

        _eval.Evaluate("=(123456789012345+0.4)-123456789012345", sheet)
            .Should().Be(new NumberValue(0.40625));
    }

    [Fact]
    public void OrdinaryIntegerAddition_IsUnaffectedByRounding()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=2+2", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void OrdinaryDecimalMultiplication_IsUnaffectedByRounding()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=1.5*4", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void OrdinaryDivision_IsUnaffectedByRounding()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=10/4", sheet).Should().Be(new NumberValue(2.5));
    }

    [Fact]
    public void OrdinaryPower_IsUnaffectedByRounding()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=2^10", sheet).Should().Be(new NumberValue(1024));
    }

    private static Sheet MakeSheet()
    {
        return new Sheet(SheetId.New(), "S");
    }
}
