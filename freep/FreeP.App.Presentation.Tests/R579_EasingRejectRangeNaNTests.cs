using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 579. <c>AnimationPanePlanner.TryParseEasing</c> screened the Smooth Start/End percentage
/// with the two-sided REJECT range <c>percent is &lt; 0 or &gt; 100</c>.
/// <para>
/// That range excludes both infinities — <c>Infinity &gt; 100</c> is true — but it does NOT exclude
/// NaN, because BOTH of its comparisons are false; and the literal "NaN" parses under
/// <c>NumberStyles.Float</c>. A NaN percent then reached
/// <c>(int)Math.Round(percent * 1000)</c>, whose saturating conversion is 0, so typing "NaN" into
/// the field was silently accepted as 0 instead of being reported invalid the way any other
/// unusable text is.
/// </para>
/// <para>
/// The general point, which r579 found in three separate files: an ACCEPT form with an upper bound
/// (<c>x &gt; 0 &amp;&amp; x &lt;= 100</c>) excludes Infinity AND NaN, since NaN fails an accept. A
/// two-sided REJECT form excludes Infinity but admits NaN. They read alike and are not alike.
/// </para>
/// </summary>
public sealed class R579_EasingRejectRangeNaNTests
{
    [Theory]
    [InlineData("NaN")]
    [InlineData("NaN%")]
    [InlineData("-NaN")]
    public void NaNText_IsRejected_NotSilentlyAcceptedAsZero(string text)
    {
        AnimationPanePlanner.TryParseEasing(text, out var value)
            .Should().BeFalse($"\"{text}\" is not a percentage; it was accepted as {value}");
        value.Should().BeNull();
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void InfiniteText_WasAlreadyRejectedByTheRange_AndStillIs(string text)
    {
        // Records what the two-sided range DID cover, so a later change cannot silently lose it.
        AnimationPanePlanner.TryParseEasing(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("50", 50000)]
    [InlineData("12.5%", 12500)]
    [InlineData("0", 0)]
    [InlineData("100", 100000)]
    public void OrdinaryPercentages_StillParse(string text, int expected)
    {
        // Non-vacuity: the guard must not have closed the field to legitimate values, INCLUDING the
        // "0" that a NaN used to masquerade as.
        AnimationPanePlanner.TryParseEasing(text, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }
}
