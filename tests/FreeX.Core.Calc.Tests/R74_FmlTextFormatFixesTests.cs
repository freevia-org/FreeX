using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R74-render-number-format-4-2: NumberFormatter.Fractions.cs's FormatSimpleFraction
/// unconditionally applied the negative sign in its numerator==0 return paths even when the
/// fully-rounded displayed magnitude (whole part + fraction) came out to all-zero -- e.g. -0.001
/// with "# ?/?" rendered "-0    " instead of Excel's "0    " (Excel never shows a sign on a
/// displayed zero, mirroring the IsNegativeZeroRepresentation/IsAllZeroText guards
/// NumberFormatter.cs already applies for plain numeric formats). Fixed by suppressing the sign
/// in those paths whenever the whole part is also 0.
///
/// R74-render-number-format-4-3: NumberFormatter.DateTime.cs's FormatElapsedTime had the same
/// bug for elapsed-time formats like "[h]:mm:ss" -- a tiny negative that rounds to an all-zero
/// elapsed duration (e.g. -0.0000005) rendered "-0:00:00" instead of "0:00:00". Fixed by only
/// prepending '-' when the fully-rounded elapsed total (totalSecondsD) is non-zero.
/// </summary>
public class R74_FmlTextFormatFixesTests
{
    // A plain (bracket-free) single-section format like "# ?/?" is formatted by FormatNumber's
    // fast magnitude path, which strips the sign BEFORE calling FormatSimpleFraction and only
    // re-applies it via its own (separately-guarded) IsAllZeroText check -- so it never actually
    // exercises FormatSimpleFraction's own sign handling either way. A single-section format that
    // opens with a bracket directive (e.g. the "[Red]" color tag used here, a standard, common
    // Excel construct for tagging a whole format's color) instead goes through
    // SelectPositionalSection, which -- for a lone, uncomparisoned section -- passes the value
    // through with its ORIGINAL sign intact (NumberFormatter.Sections.cs), so FormatSimpleFraction
    // itself is what must decide whether the numerator==0 sign is shown. That is the reachable
    // path this fix (and these tests) targets; the color tag itself does not appear in .Text.
    [Theory]
    [InlineData(-0.001, "[Red]# ?/?", "0    ")]
    [InlineData(-0.001, "[Red]?/2", "0/2")]
    public void FormatSimpleFraction_NegativeRoundsToAllZero_SuppressesSign(double value, string format, string expected)
    {
        Assert.Equal(expected, NumberFormatter.Format(new NumberValue(value), format));
    }

    [Fact]
    public void FormatSimpleFraction_GenuineNegativeWholeAndFraction_StillShowsSign()
    {
        Assert.Equal("-1 1/2", NumberFormatter.Format(new NumberValue(-1.5), "[Red]# ?/?"));
    }

    [Fact]
    public void FormatSimpleFraction_PositiveValue_Unaffected()
    {
        Assert.Equal("2    ", NumberFormatter.Format(new NumberValue(2.0), "[Red]# ?/?"));
    }

    [Fact]
    public void FormatElapsedTime_TinyNegativeRoundsToAllZero_SuppressesSign()
    {
        Assert.Equal("0:00:00", NumberFormatter.Format(new NumberValue(-0.0000005), "[h]:mm:ss"));
    }

    [Fact]
    public void FormatElapsedTime_GenuineNegative_StillShowsSign()
    {
        Assert.Equal("-1:30:00", NumberFormatter.Format(new NumberValue(-1.5 / 24), "[h]:mm:ss"));
    }

    [Fact]
    public void FormatElapsedTime_PositiveValue_Unaffected()
    {
        Assert.Equal("1:30:00", NumberFormatter.Format(new NumberValue(1.5 / 24), "[h]:mm:ss"));
    }
}
