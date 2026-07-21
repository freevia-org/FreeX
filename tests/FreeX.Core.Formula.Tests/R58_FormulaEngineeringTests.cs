using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-58 formula-engineering fixes: CONVERT cubic-nautical-mile scaling, ERF/ERF.PRECISE
/// precision parity with ERFC, and COMPLEX/IM* exact-case "i"/"j" suffix validation.
/// </summary>
public sealed class R58_FormulaEngineeringTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet() => new(SheetId.New(), "S");

    // ── R58-formula-engineering-6-1: CONVERT Nmi3/Nmi^3 volume factor ─────────

    [Fact]
    public void Convert_CubicNauticalMileToCubicMeters_MatchesExactDimensionalIdentity()
    {
        // 1 Nmi = 1852 m exactly, so 1 Nmi^3 = 1852^3 m^3 = 6,352,182,208 m^3.
        var result = _eval.Evaluate("=CONVERT(1,\"Nmi3\",\"m3\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(6352182208, 1e-3);
    }

    [Fact]
    public void Convert_CubicNauticalMileToLiters_MatchesM3ToLiterScaling()
    {
        // Sibling/no-regression check: the liter-base scaling (x1000 vs m3) must also be correct,
        // and the caret-form alias "Nmi^3" must agree with "Nmi3".
        var result = _eval.Evaluate("=CONVERT(1,\"Nmi^3\",\"l\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(6352182208000, 1);
    }

    // ── R58-formula-engineering-6-2: ERF/ERF.PRECISE precision ─────────────────

    [Fact]
    public void Erf_MatchesExcelToFullDoublePrecision()
    {
        var result = _eval.Evaluate("=ERF(2)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        // Excel's ERF(2) = 0.9953222650189527; the old Abramowitz & Stegun approximation
        // returned ~0.9953221395809655 (off by ~1.25e-7, wrong at the 6th significant digit).
        ((NumberValue)result).Value.Should().BeApproximately(0.9953222650189527, 1e-12);
    }

    [Fact]
    public void ErfPrecise_And_ErfTwoArgForm_MatchHighPrecisionErfc()
    {
        // Sibling/no-regression: ERF.PRECISE(2) must share the same high-precision path,
        // and the two-argument ERF(lower,upper) form must be internally consistent using
        // the same precise Erf values (erf(2) - erf(1)).
        var precise = _eval.Evaluate("=ERF.PRECISE(2)", MakeSheet());
        precise.Should().BeOfType<NumberValue>();
        ((NumberValue)precise).Value.Should().BeApproximately(0.9953222650189527, 1e-12);

        var between = _eval.Evaluate("=ERF(1,2)", MakeSheet());
        between.Should().BeOfType<NumberValue>();
        ((NumberValue)between).Value.Should().BeApproximately(0.9953222650189527 - 0.8427007929497149, 1e-9);
    }

    // ── R58-formula-engineering-6-3: COMPLEX/IM* exact-case "i"/"j" suffix ────

    [Fact]
    public void Complex_UppercaseSuffix_ReturnsValueError()
    {
        // Excel requires the suffix argument to be exactly lowercase "i" or "j"; uppercase must
        // be rejected with #VALUE!, not silently normalized and accepted.
        var result = _eval.Evaluate("=COMPLEX(3,4,\"I\")", MakeSheet());
        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Complex_LowercaseSuffix_StillAccepted()
    {
        // Sibling/no-regression: the exact-case lowercase suffix path must keep working.
        var result = _eval.Evaluate("=COMPLEX(3,4,\"i\")", MakeSheet());
        result.Should().Be(new TextValue("3+4i"));

        var jResult = _eval.Evaluate("=COMPLEX(3,-4,\"j\")", MakeSheet());
        jResult.Should().Be(new TextValue("3-4j"));
    }
}
