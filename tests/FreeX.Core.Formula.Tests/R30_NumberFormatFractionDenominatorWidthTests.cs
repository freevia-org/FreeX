using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-30 regression coverage for two custom fraction-format bugs in
/// NumberFormatter.Fractions.cs:
///   - R30-number-format-custom-deep-1: the fraction max-denominator cap was a 2-way ternary
///     (denominator placeholder width &gt;= 2 -&gt; 99, else 9) that never scaled beyond 99, so a
///     3-"?" denominator format like "???/???" could never represent fractions needing a
///     3-digit denominator (e.g. 0.002 = 1/500) and collapsed to "0" instead. Fixed by deriving
///     maxDenominator = 10^width - 1 from the actual placeholder width.
///   - R30-number-format-custom-deep-2: a pure variable-denominator fraction format with no
///     whole-number section (e.g. "?/?") applied to 0 dropped the fraction entirely and
///     returned the bare "0", even though the fixed-denominator sibling case (e.g. "?/8") already
///     kept the denominator visible for value 0. Fixed by extending the !hasWholeSection
///     zero-numerator branch to render the fraction (using the resolved denominator) regardless
///     of whether the denominator is fixed or variable.
/// </summary>
public sealed class R30_NumberFormatFractionDenominatorWidthTests
{
    // ── R30-number-format-custom-deep-1: denominator cap must scale with placeholder width ──

    [Fact]
    public void ThreeQuestionMarkDenominator_SmallFraction_UsesThreeDigitDenominator()
    {
        // 0.002 == 1/500 exactly. With the old 2-way cap (99), no denominator up to 99 can
        // represent this, so ApproximateFraction picks numerator 0 and the value collapsed to
        // "0". With the fix, maxDenominator scales to 999, so the exact 1/500 is found.
        var result = NumberFormatter.Format(new NumberValue(0.002), "???/???");

        result.Should().EndWith("1/500");
        result.Should().NotBe("0");
    }

    [Fact]
    public void TwoQuestionMarkDenominatorWithWholeSection_StillWorksAsBefore()
    {
        // Sibling already-working case: a normal "# ??/??" (two-digit denominator cap of 99)
        // must keep working unchanged after widening the cap derivation.
        var result = NumberFormatter.Format(new NumberValue(3.14159), "# ??/??");

        result.Should().StartWith("3 ");
        result.Should().Contain("/");
    }

    // ── R30-number-format-custom-deep-2: variable-denominator fraction at zero ──────────────

    [Fact]
    public void VariableDenominatorFractionNoWholeSection_AtZero_KeepsDenominatorVisible()
    {
        // "?/?" has no whole-number section, so the entire value is the fraction. For 0 the
        // best rational approximation is 0/1. Before the fix this returned the bare "0",
        // dropping the denominator; Excel (and the fixed-denominator sibling "?/8") keeps it.
        var result = NumberFormatter.Format(new NumberValue(0), "?/?");

        result.Should().Be("0/1");
    }

    [Fact]
    public void FixedDenominatorFractionNoWholeSection_AtZero_StillWorksAsBefore()
    {
        // Sibling already-working case (fixed denominator, no whole section) must be unaffected.
        var result = NumberFormatter.Format(new NumberValue(0), "?/8");

        result.Should().Be("0/8");
    }
}
