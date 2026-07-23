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
    public void OneThird_RoundsToFifteenSignificantDigits_NotRawSeventeenDigitDouble()
    {
        // 1.0/3.0 as a raw double is 0.3333333333333333148296... (16 significant '3' digits in
        // its shortest round-trippable form). Excel only stores/computes with 15 significant
        // digits, so it displays and returns 0.333333333333333 (15 '3' digits, then zeroes) --
        // this is Excel's well-documented 15-significant-figure precision limit, not truncation
        // or a wrong answer. FreeX must match it, not merely pass through the raw 16-digit
        // double unrounded.
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=1/3", sheet);

        result.Should().BeOfType<NumberValue>();
        var value = ((NumberValue)result).Value;
        value.Should().Be(0.333333333333333d, "Excel rounds arithmetic results to 15 significant digits");
        value.Should().NotBe(1.0 / 3.0, "the raw unrounded double carries a 16th significant digit Excel would not");
    }

    [Fact]
    public void LargeMagnitudeAddition_SubResolutionAddendIsAbsorbed_MatchingExcel()
    {
        // 123456789012345 already has 15 significant digits, so adding 0.4 (which would only
        // affect the 16th+ significant digit) has no visible effect once rounded to 15 sig
        // digits -- exactly as Excel does: the addend is below the storage resolution of the
        // sum's magnitude.
        var sheet = MakeSheet();

        _eval.Evaluate("=123456789012345+0.4", sheet)
            .Should().Be(new NumberValue(123456789012345d));
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
