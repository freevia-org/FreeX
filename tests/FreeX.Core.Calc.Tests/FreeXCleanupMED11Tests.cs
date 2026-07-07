using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for cleanup batch MED11 finding P60: FormatElapsedTime must be quote-aware.
/// Quote characters in an elapsed-time format like <c>[h]:mm " hrs"</c> must not be emitted literally
/// into the rendered text, and a quoted literal that happens to spell "mm"/"ss" must be copied
/// verbatim rather than substituted with the minutes/seconds remainder.
/// </summary>
public class FreeXCleanupMED11Tests
{
    [Fact]
    public void ElapsedTimeFormat_WithTrailingQuotedLiteral_DropsQuoteCharactersFromOutput()
    {
        // [h]:mm " hrs" of 1.5 days => 36 hours, 0 minutes, plus the literal suffix " hrs" (with its
        // own leading space, plus the unquoted space before the literal) and no quote characters.
        var result = NumberFormatter.Format(new NumberValue(1.5), "[h]:mm \" hrs\"");

        Assert.Equal("36:00  hrs", result);
        Assert.DoesNotContain('"', result);
    }

    [Fact]
    public void ElapsedTimeFormat_QuotedLiteralSpellingMinutesToken_IsNotSubstituted()
    {
        // The quoted literal "mm" must survive as the two literal characters 'm','m', not be
        // reinterpreted as the minutes-remainder substitution token.
        var result = NumberFormatter.Format(new NumberValue(1.5), "[h] \"mm\"");

        Assert.Equal("36 mm", result);
    }

    [Fact]
    public void ElapsedTimeFormat_QuotedLiteralSpellingSecondsToken_IsNotSubstituted()
    {
        var result = NumberFormatter.Format(new NumberValue(1.5), "[h]:mm \"ss\"");

        Assert.Equal("36:00 ss", result);
    }
}
