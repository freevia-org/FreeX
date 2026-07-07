using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Calc.Tests;

// Regression coverage for cleanup batch group 4.
public class FreeXCleanupB4Tests
{
    // P56: two-letter (zero-padded-width) elapsed-time bracket tokens [hh]/[mm]/[ss] must be
    // recognized the same way single-letter [h]/[m]/[s] tokens are — Excel/ECMA-376 allows
    // repeating the elapsed-unit letter inside the brackets, and the repeat count does not
    // truncate/pad the lead unit's magnitude (36 hours still renders "36", not "36" clipped to
    // two digits). Before the fix, NumericElapsedTokenRegex only matched a single letter, so
    // "[hh]:mm" fell through to RemoveUnquotedBracketDirectives, which deleted the whole "[hh]"
    // bracket and left a bare ":mm" that was then misformatted as a date/time fragment.
    [Theory]
    [InlineData("[hh]:mm", 1.5, "36:00")]
    [InlineData("[mm]:ss", 1.5, "2160:00")]
    [InlineData("[ss]", 1.5, "129600")]
    public void ElapsedTimeFormat_WithZeroPaddedBracketToken_FormatsFullElapsedDuration(
        string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    // The single-letter form must keep working identically after widening the token regex.
    [Theory]
    [InlineData("[h]:mm", 1.5, "36:00")]
    [InlineData("[m]:ss", 1.5, "2160:00")]
    [InlineData("[s]", 1.5, "129600")]
    public void ElapsedTimeFormat_WithSingleLetterBracketToken_StillFormatsFullElapsedDuration(
        string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }
}
