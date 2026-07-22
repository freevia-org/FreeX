using System.Diagnostics;
using System.Globalization;

using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-68 fml-text-eng bucket fixes:
///   R68-formula-text-format-6-1: a wide-"?" fraction denominator (e.g. 9 "?" characters,
///     maxDenominator = 10^9-1) made ApproximateFraction's brute-force O(maxDenominator) search
///     hang the evaluator. Replaced with a continued-fraction / Stern-Brocot best-rational-
///     approximation search bounded by maxDenominator, which is O(number of continued-fraction
///     terms) regardless of how wide the "?" run is.
///   R68-formula-text-format-6-2: UPPER("straße") returned "STRAßE" because .NET's
///     ToUpperInvariant maps German ß 1:1, whereas Excel/Windows casing expands it to "SS".
///   R68-formula-engineering-6-1: COMPLEX(3,4,A1) with A1 blank returned "3+4i" (falling back to
///     the default "i" suffix) instead of #VALUE!; the default must only apply when the suffix
///     argument slot is genuinely omitted, not when it's present-but-blank.
///   R68-formula-engineering-6-2: the IMSUB/IMSUM/IMPRODUCT/IMDIV i/j-notation mismatch guard
///     only compared Suffix when both operands had a non-zero Imaginary part, so an explicit
///     zero-coefficient suffix like "3+0j" wasn't caught against "5+2i". The guard now compares
///     the EXPLICIT suffix recorded by ParseComplexArgument (present even for a zero coefficient),
///     so a truly bare real (no suffix at all) still never triggers a false mismatch.
/// </summary>
public sealed class R68_FmlTextEngineeringFixesTests
{
    private readonly FormulaEvaluator _eval = new();

    // ── R68-formula-text-format-6-1: wide-"?" fraction denominator must not hang ────────────

    [Fact]
    public void WideQuestionMarkDenominator_Half_CompletesInstantly_AndFormatsAsOneHalf()
    {
        var sw = Stopwatch.StartNew();
        var result = NumberFormatter.Format(new NumberValue(0.5), "?????????/?????????");
        sw.Stop();

        // The old brute-force search iterated up to maxDenominator = 10^9-1 ~ 1e9 times; the
        // continued-fraction replacement finishes in a handful of iterations. A generous 3s
        // bound comfortably separates "instant" from "hung for seconds/minutes".
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        result.Trim().Should().Be("1/2");
    }

    [Fact]
    public void WideQuestionMarkDenominator_ExactEighth_ReturnsExactFractionInstantly()
    {
        var sw = Stopwatch.StartNew();
        var result = NumberFormatter.Format(new NumberValue(0.125), "?????????/?????????");
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        result.Trim().Should().Be("1/8");
    }

    [Fact]
    public void WideQuestionMarkDenominator_RepeatingDecimal_ReturnsCloseRationalInstantly()
    {
        var sw = Stopwatch.StartNew();
        var result = NumberFormatter.Format(new NumberValue(0.333333), "?????????/?????????");
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));

        var parts = result.Trim().Split('/');
        parts.Should().HaveCount(2);
        var numerator = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var denominator = double.Parse(parts[1], CultureInfo.InvariantCulture);
        (numerator / denominator).Should().BeApproximately(0.333333, 1e-9);
    }

    [Fact]
    public void NormalFraction_Half_UnaffectedByAlgorithmChange_SiblingNoRegression()
    {
        NumberFormatter.Format(new NumberValue(0.5), "?/?").Should().Be("1/2");
    }

    [Fact]
    public void NormalFraction_TwoAndQuarter_UnaffectedByAlgorithmChange_SiblingNoRegression()
    {
        NumberFormatter.Format(new NumberValue(2.25), "0 ?/?").Should().Be("2 1/4");
    }

    // ── R68-formula-text-format-6-2: UPPER must expand German ß to "SS" ─────────────────────

    [Fact]
    public void Upper_GermanSharpS_ExpandsToDoubleS()
    {
        _eval.Evaluate("=UPPER(\"straße\")", MakeSheet()).Should().Be(new TextValue("STRASSE"));
    }

    [Fact]
    public void Upper_PlainAscii_StillUppercasesNormally_SiblingNoRegression()
    {
        _eval.Evaluate("=UPPER(\"hello\")", MakeSheet()).Should().Be(new TextValue("HELLO"));
    }

    [Fact]
    public void Lower_PlainAscii_StillLowercasesNormally_SiblingNoRegression()
    {
        _eval.Evaluate("=LOWER(\"HELLO\")", MakeSheet()).Should().Be(new TextValue("hello"));
    }

    [Fact]
    public void Upper_NonGermanAccentedString_UnaffectedBySharpSFix_SiblingNoRegression()
    {
        _eval.Evaluate("=UPPER(\"café\")", MakeSheet()).Should().Be(new TextValue("CAFÉ"));
    }

    // ── R68-formula-engineering-6-1: COMPLEX's blank-but-present suffix arg is #VALUE! ──────

    [Fact]
    public void Complex_BlankSuffixCellReference_ReturnsValueError()
    {
        // A1 is never set, so referencing it evaluates to BlankValue -- present in the arg
        // slot, distinct from the argument being omitted entirely.
        var sheet = new Sheet(SheetId.New(), "S");

        _eval.Evaluate("=COMPLEX(3,4,A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Complex_OmittedSuffixArg_StillDefaultsToI_SiblingNoRegression()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        _eval.Evaluate("=COMPLEX(3,4)", sheet).Should().Be(new TextValue("3+4i"));
    }

    [Fact]
    public void Complex_ExplicitJSuffix_StillWorks_SiblingNoRegression()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        _eval.Evaluate("=COMPLEX(3,4,\"j\")", sheet).Should().Be(new TextValue("3+4j"));
    }

    // ── R68-formula-engineering-6-2: explicit zero-coefficient suffix mismatch (IMSUB et al.) ─

    [Fact]
    public void ImSub_ExplicitZeroImaginarySuffixMismatch_ReturnsNum()
    {
        _eval.Evaluate("=IMSUB(\"3+0j\",\"5+2i\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ImSub_BareRealNoSuffix_NoMismatch_SiblingNoRegression()
    {
        _eval.Evaluate("=IMSUB(\"3\",\"5+2i\")", MakeSheet()).Should().Be(new TextValue("-2-2i"));
    }

    [Fact]
    public void ImDiv_ExplicitZeroImaginarySuffixMismatch_ReturnsNum()
    {
        _eval.Evaluate("=IMDIV(\"3+0j\",\"5+2i\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ImSum_ExplicitZeroImaginarySuffixMismatch_ReturnsNum()
    {
        _eval.Evaluate("=IMSUM(\"3+0j\",\"5+2i\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ImProduct_ExplicitZeroImaginarySuffixMismatch_ReturnsNum()
    {
        _eval.Evaluate("=IMPRODUCT(\"1+0j\",\"5+2i\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ImSum_MatchingSuffixes_StillWorks_SiblingNoRegression()
    {
        _eval.Evaluate("=IMSUM(\"3+4i\",\"1+2i\")", MakeSheet()).Should().Be(new TextValue("4+6i"));
    }

    private static Sheet MakeSheet() => new(SheetId.New(), "S");
}
