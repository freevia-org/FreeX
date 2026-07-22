using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-74 formula-engineering fixes: BIT* functions truncate non-integer operands like Excel
/// instead of erroring, CONVERT "cal" uses the IT-calorie factor (not the thermochemical-calorie
/// factor already used by "c"), and CONVERT gains the missing "admkn" (Admiralty knot) speed unit.
/// </summary>
public sealed class R74_FormulaEngineeringTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet() => new(SheetId.New(), "S");

    // ── R74-formula-engineering-4-1: BIT* truncate non-integer args ───────────

    [Fact]
    public void BitAnd_NonIntegerNumberArgument_TruncatesLikeExcel()
    {
        // Excel truncates a non-integer number argument before applying BITAND, rather than
        // returning #NUM!: BITAND(5.5,3) == BITAND(5,3) == 1.
        var result = _eval.Evaluate("=BITAND(5.5,3)", MakeSheet());
        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void BitLShift_NonIntegerShiftArgument_TruncatesLikeExcel()
    {
        // Excel truncates a non-integer shift_amount before shifting: BITLSHIFT(4,2.9) ==
        // BITLSHIFT(4,2) == 16, not #NUM!.
        var result = _eval.Evaluate("=BITLSHIFT(4,2.9)", MakeSheet());
        result.Should().Be(new NumberValue(16));
    }

    [Fact]
    public void BitAnd_NegativeNumberArgument_StillReturnsNum()
    {
        // Sibling/no-regression: the negative-number validation must still reject, truncation
        // must not accidentally widen the accepted range.
        var result = _eval.Evaluate("=BITAND(-1,3)", MakeSheet());
        result.Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void BitAnd_IntegerArguments_Unchanged()
    {
        // Sibling/no-regression: plain integer arguments must keep working exactly as before.
        var result = _eval.Evaluate("=BITAND(5,3)", MakeSheet());
        result.Should().Be(new NumberValue(1));
    }

    // ── R74-formula-engineering-4-2: CONVERT "cal" uses IT-calorie factor ─────

    [Fact]
    public void Convert_ItCalorieToJoules_UsesItCalorieFactor()
    {
        // "cal" is the IT (International Table) calorie = 4.1868 J, distinct from the
        // thermochemical calorie "c" = 4.184 J.
        var result = _eval.Evaluate("=CONVERT(1,\"cal\",\"J\")", MakeSheet());
        result.Should().Be(new NumberValue(4.1868));
    }

    [Fact]
    public void Convert_ThermochemicalCalorieToJoules_StaysUnchanged()
    {
        // Sibling/no-regression: "c" (thermochemical calorie) must keep its original 4.184 J
        // factor — only "cal" (IT calorie) changes.
        var result = _eval.Evaluate("=CONVERT(1,\"c\",\"J\")", MakeSheet());
        result.Should().Be(new NumberValue(4.184));
    }

    [Fact]
    public void Convert_ThermochemicalCalorieToItCalorie_IsNotExactlyOne()
    {
        // Now that "cal" and "c" have distinct factors, converting between them must no longer
        // be a 1:1 identity.
        var result = _eval.Evaluate("=CONVERT(1,\"c\",\"cal\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        var value = ((NumberValue)result).Value;
        value.Should().BeApproximately(4.184 / 4.1868, 1e-12);
        value.Should().NotBe(1);
    }

    // ── R74-formula-engineering-4-3: CONVERT "admkn" Admiralty knot ───────────

    [Fact]
    public void Convert_AdmiraltyKnotToMetersPerSecond_MatchesExactFactor()
    {
        // 1 Admiralty knot = 1 UK Admiralty nautical mile (6080 ft) per hour.
        var result = _eval.Evaluate("=CONVERT(1,\"admkn\",\"m/s\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.514773333, 1e-6);
    }

    [Fact]
    public void Convert_InternationalKnotToAdmiraltyKnot_IsNotExactlyOne()
    {
        // Sibling/no-regression: the international knot ("kn", based on the 1852 m nautical
        // mile) and the Admiralty knot ("admkn", based on the 6080 ft nautical mile) are close
        // but distinct units, so the conversion factor must not collapse to 1.
        var result = _eval.Evaluate("=CONVERT(1,\"kn\",\"admkn\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        var value = ((NumberValue)result).Value;
        value.Should().BeApproximately(0.514444 / (6080.0 * 0.3048 / 3600), 1e-9);
        value.Should().NotBe(1);
    }

    [Fact]
    public void Convert_AdmiraltyKnotToMph_IsReasonable()
    {
        // Sibling/no-regression: cross-unit conversion (admkn -> mph) must resolve through the
        // shared Speed base (m/s) without error, landing close to the well-known ~1.151 mph/knot
        // ballpark.
        var result = _eval.Evaluate("=CONVERT(1,\"admkn\",\"mph\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        var value = ((NumberValue)result).Value;
        value.Should().BeApproximately((6080.0 * 0.3048 / 3600) / 0.44704, 1e-9);
        value.Should().BeGreaterThan(1.1);
        value.Should().BeLessThan(1.2);
    }
}
