using System.Globalization;

using FluentAssertions;

using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r571: <see cref="DialogNumericTextPolicy"/> is the shared numeric-entry policy behind 55 call
/// sites across FreeW's dialogs (chart size, column count and spacing, hyphenation zone, and so
/// on). It sits in the same tier as ZoomPercentPolicy, which r550 guarded, and
/// PageMarginTextPolicy, which already rejects with an explicit IsNaN/IsInfinity -- so two
/// policies in that tier guard and this third did not.
///
/// <para><b>This corrects a rule from r551.</b> That round recorded that the ACCEPT form of a
/// range check rejects both Infinity and NaN, and its example carried BOTH bounds
/// (<c>width &gt; 0 &amp;&amp; width &lt;= 12</c>). These wrappers are accept-form with only a
/// LOWER bound -- <c>value &gt; 0</c>, <c>value &gt;= 0</c> -- and Infinity satisfies both, so it
/// passes. Only NaN is caught, because NaN fails every comparison. An accept form excludes a
/// non-finite value only when it is bounded on the side that Infinity would exceed.</para>
/// </summary>
public sealed class R571_SharedDialogPolicyRejectsNonFiniteTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void A_positive_parse_rejects_an_infinite_value(string hostile)
    {
        // "value > 0" is satisfied by Infinity, so the lower-bound accept form never stopped it.
        DialogNumericTextPolicy.TryParsePositiveDouble(hostile, Invariant, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void A_non_negative_parse_rejects_an_infinite_value(string hostile)
    {
        DialogNumericTextPolicy.TryParseNonNegativeDouble(hostile, Invariant, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void The_nullable_parse_rejects_a_non_finite_value(string hostile)
    {
        // This one has no range check at all, so every spelling reaches the caller.
        DialogNumericTextPolicy.ParseNullableDouble(hostile, Invariant).Should().BeNull();
    }

    [Fact]
    public void Nan_was_already_rejected_by_the_lower_bound()
    {
        // Recorded rather than claimed as evidence: NaN fails every comparison, so the wrappers
        // already answered false for it before this fix. Keeping the case documents which half of
        // the problem the old guard actually covered.
        DialogNumericTextPolicy.TryParsePositiveDouble("NaN", Invariant, out _).Should().BeFalse();
        DialogNumericTextPolicy.TryParseNonNegativeDouble("NaN", Invariant, out _).Should().BeFalse();
    }

    [Fact]
    public void Ordinary_values_still_parse()
    {
        // Non-vacuity across every wrapper the fix touches.
        DialogNumericTextPolicy.TryParsePositiveDouble("12.5", Invariant, out var positive)
            .Should().BeTrue();
        positive.Should().Be(12.5);

        DialogNumericTextPolicy.TryParseNonNegativeDouble("0", Invariant, out var nonNegative)
            .Should().BeTrue();
        nonNegative.Should().Be(0);

        DialogNumericTextPolicy.ParseNullableDouble("-3.25", Invariant).Should().Be(-3.25);
        DialogNumericTextPolicy.ParseNullableDouble(7.5d, Invariant).Should().Be(7.5);
    }
}
