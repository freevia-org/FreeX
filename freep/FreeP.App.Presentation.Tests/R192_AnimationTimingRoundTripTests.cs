using System.Globalization;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r192 (backlog item 33): the Animation Pane's duration and delay fields are not display-only --
/// <c>TryParseTimingSeconds</c> reads the very string <c>FormatDuration</c> wrote back as the source
/// of truth when the field loses focus. At the old <c>"0.##"</c> (10ms resolution) any duration that
/// was not a multiple of 10ms was silently rounded by merely opening the pane and clicking away: a
/// 1234ms animation redisplayed as "1.23" and became 1230ms. Repeating that walked a value down.
/// </summary>
public sealed class R192_AnimationTimingRoundTripTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(1234)]
    [InlineData(333)]
    [InlineData(501)]
    [InlineData(1005)]
    [InlineData(1)]
    [InlineData(9999)]
    public void FormatThenParse_PreservesAMillisecondPreciseDuration(int ms)
    {
        var displayed = AnimationPanePlanner.FormatDuration(ms, Invariant);

        AnimationPanePlanner.TryParseDuration(displayed, out var parsed).Should().BeTrue();
        parsed.Should().Be(ms, "the pane must not round a value the user never edited");
    }

    [Fact]
    public void FormatThenParse_IsStableAcrossRepeatedFocusLoss()
    {
        // The failure mode was cumulative: each open-and-click-away lost a little more.
        var ms = 1234;
        for (var i = 0; i < 10; i++)
        {
            AnimationPanePlanner.TryParseDuration(
                AnimationPanePlanner.FormatDuration(ms, Invariant), out ms).Should().BeTrue();
        }

        ms.Should().Be(1234);
    }

    [Theory]
    [InlineData(500, "0.5")]
    [InlineData(1000, "1")]
    [InlineData(1250, "1.25")]
    public void FormatDuration_LeavesOrdinaryValuesLookingExactlyAsBefore(int ms, string expected)
    {
        // Every duration a multiple of 10ms -- which is everything an author types by hand -- prints
        // identically at the higher precision, so the pane does not suddenly show trailing digits.
        AnimationPanePlanner.FormatDuration(ms, Invariant).Should().Be(expected);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void FormatThenParse_StillRoundTripsOnACommaDecimalLocale(string cultureName)
    {
        // Guards the r177 fix as well: the extra digit must not break the culture round-trip.
        var culture = new CultureInfo(cultureName);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            var displayed = AnimationPanePlanner.FormatDuration(1234, culture);
            AnimationPanePlanner.TryParseDuration(displayed, out var parsed).Should().BeTrue();
            parsed.Should().Be(1234);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
