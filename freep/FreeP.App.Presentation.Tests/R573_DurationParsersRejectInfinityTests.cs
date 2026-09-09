using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r573: found by scanning the dialog planners with r571's CORRECTED accept-form rule rather than
/// the overbroad one. Both duration parsers bound only from below --
/// <c>allowZero ? seconds &gt;= 0 : seconds &gt; 0</c> -- and Infinity satisfies either, so it
/// passes. NaN is caught, because NaN fails every comparison.
///
/// <para>The consequence is concrete rather than cosmetic: the accepted value goes through
/// <c>(int)Math.Round(seconds * 1000.0)</c>, and .NET's floating-point conversion SATURATES, so an
/// infinite duration becomes int.MaxValue milliseconds -- about 24.8 days. That is r546's
/// saturating-cast class arriving via a range check that looked like a guard.</para>
/// </summary>
public sealed class R573_DurationParsersRejectInfinityTests
{
    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void A_transition_duration_of_infinity_is_rejected(string hostile)
    {
        PresentationTransitionCommandPlanner
            .TryParseSeconds(hostile, allowZero: true, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void A_transition_duration_of_infinity_is_rejected_when_zero_is_disallowed(string hostile)
    {
        // Both branches of the ternary bound only from below, so both admit Infinity.
        PresentationTransitionCommandPlanner
            .TryParseSeconds(hostile, allowZero: false, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Nan_was_already_rejected_by_the_lower_bound()
    {
        // Documentation, not evidence: NaN fails every comparison, so it answered false before
        // this fix too. Recording which half the old check actually covered.
        PresentationTransitionCommandPlanner.TryParseSeconds("NaN", allowZero: true, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Ordinary_durations_still_parse_and_round()
    {
        // Non-vacuity, and it pins r192's rounding behaviour so a guard cannot quietly change it:
        // 1.005 seconds must round to 1005 ms rather than truncate to 1004.
        PresentationTransitionCommandPlanner.TryParseSeconds("1.005", allowZero: false, out var ms)
            .Should().BeTrue();
        ms.Should().Be(1005);

        PresentationTransitionCommandPlanner.TryParseSeconds("0", allowZero: true, out var zero)
            .Should().BeTrue();
        zero.Should().Be(0);

        PresentationTransitionCommandPlanner.TryParseSeconds("0", allowZero: false, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void A_trailing_unit_suffix_still_parses()
    {
        // Pins the pre-existing "1.5s" handling the fix routes through unchanged.
        PresentationTransitionCommandPlanner.TryParseSeconds("1.5s", allowZero: false, out var ms)
            .Should().BeTrue();
        ms.Should().Be(1500);
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void An_animation_duration_of_infinity_is_rejected(string hostile)
    {
        // The Animation Pane's own duration/delay fields, a second pair of wrappers over the same
        // one-sided bound. Both are covered because TryParseDuration and TryParseDelay pass
        // different allowZero values into the shared helper.
        AnimationPanePlanner.TryParseDuration(hostile, out _).Should().BeFalse();
        AnimationPanePlanner.TryParseDelay(hostile, out _).Should().BeFalse();
    }

    [Fact]
    public void Ordinary_animation_timings_still_parse()
    {
        // Non-vacuity for the animation pair, including r192's rounding rule.
        AnimationPanePlanner.TryParseDuration("1.005", out var duration).Should().BeTrue();
        duration.Should().Be(1005);

        AnimationPanePlanner.TryParseDelay("0", out var delay).Should().BeTrue();
        delay.Should().Be(0);

        AnimationPanePlanner.TryParseDuration("0", out _).Should().BeFalse();
    }
}
