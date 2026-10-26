using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round 579: a number format's theme-colour token can carry a tint percentage
/// (e.g. <c>[THEMEACCENT1 TINT 50%]</c>), and <c>NumberFormatColorMapper.TryGetThemeColorReference</c>
/// screened it with the two-sided REJECT range <c>tintPercent is &lt; -100d or &gt; 100d</c>.
/// <para>
/// That range excludes both infinities — <c>Infinity &gt; 100</c> is true, and so is the overflow of
/// a long digit run even with no <c>AllowExponent</c> in the styles — but it does NOT exclude NaN,
/// because BOTH of its comparisons are false. And the literal "NaN" does parse here despite the
/// narrow <c>AllowLeadingSign | AllowDecimalPoint</c> styles: .NET checks the NaN/Infinity symbols
/// independently of NumberStyles. Both halves of that were verified by probe rather than assumed.
/// </para>
/// <para>
/// This refines r574's encoded rule, which is about ACCEPT forms: an accept form with an upper bound
/// (<c>x &gt; 0 &amp;&amp; x &lt;= 12</c>) excludes Infinity AND NaN, since NaN fails an accept. A
/// two-sided REJECT form excludes Infinity but admits NaN. The two read alike and are not alike.
/// </para>
/// </summary>
public sealed class R579_NumberFormatThemeTintTests
{
    private static double? Tint(string token) =>
        NumberFormatColorMapper.TryGetThemeColorReference(token, out _, out var tint)
            ? tint
            : null;

    [Theory]
    [InlineData("THEMEACCENT1 TINT NaN%")]
    [InlineData("THEMEACCENT1 TINT NaN")]
    public void NaNTint_IsRejectedLikeAnyUnreadableTint(string token)
    {
        // Either the token is refused outright or it resolves to a usable tint -- what must never
        // happen is a NaN reaching theme.ResolveColor and the HSL luminance maths behind it.
        var tint = Tint(token);

        if (tint is { } value)
            double.IsFinite(value).Should().BeTrue($"\"{token}\" produced the tint {value}");
    }

    [Theory]
    [InlineData("THEMEACCENT1 TINT Infinity%")]
    [InlineData("THEMEACCENT1 TINT 999999999999999999999999999999999999999%")]
    public void InfiniteTint_WasAlreadyRejectedByTheRange_AndStillIs(string token)
    {
        // Non-vacuity of a different kind: this records what the two-sided range DID cover, so a
        // later change that trades the range for a bare finite test cannot silently lose it.
        var tint = Tint(token);

        if (tint is { } value)
            double.IsFinite(value).Should().BeTrue($"\"{token}\" produced the tint {value}");
    }

    [Theory]
    [InlineData("THEMEACCENT1 TINT 50%", 0.5)]
    [InlineData("THEMEACCENT1 TINT -25%", -0.25)]
    [InlineData("THEMEACCENT1 TINT 100%", 1.0)]
    public void OrdinaryTint_StillReadsThrough(string token, double expected)
    {
        // Non-vacuity: the guard must not have stopped legitimate tints being read.
        Tint(token).Should().BeApproximately(expected, 1e-12);
    }
}
