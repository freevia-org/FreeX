using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Calc.Tests;

// Regression coverage for J19: bracket/elapsed-time directive detection must be
// quote-aware. Text inside "..." literals (and characters following a backslash
// escape) must never be mistaken for an [h]/[m]/[s] elapsed-time token or for a
// generic bracket directive (e.g. a locale/color code), even when it happens to
// look like one.
public class NumberFormatterQuoteAwareBracketDirectiveTests
{
    [Fact]
    public void QuotedBracketLiteral_ThatLooksLikeElapsedHourToken_IsNotTreatedAsElapsedTime()
    {
        // "[h]" here is a literal suffix, not the elapsed-time [h] directive.
        // A plain number must render as itself with the literal suffix attached,
        // not be reinterpreted as an OADate-style elapsed-time duration.
        var result = NumberFormatter.Format(new NumberValue(5), "0\"[h]\"");

        Assert.Equal("5[h]", result);
    }

    [Fact]
    public void QuotedBracketLiteral_Total_Hrs_IsPreservedVerbatim()
    {
        var result = NumberFormatter.Format(new NumberValue(3), "0\" Total [hrs]\"");

        Assert.Equal("3 Total [hrs]", result);
    }

    [Theory]
    [InlineData("0\"[kg]\"", 7, "7[kg]")]
    [InlineData("0\"[EST]\"", 9, "9[EST]")]
    public void QuotedBracketLiteral_NonElapsedToken_SurvivesGenericBracketStripping(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[h]:mm:ss", "36:00:00")]
    [InlineData("[m]:ss", "2160:00")]
    [InlineData("[s]", "129600")]
    public void UnquotedElapsedTimeDirective_StillFormatsAsElapsedTime(string format, string expected)
    {
        // Genuine (unquoted) elapsed-time directives must keep working exactly as before.
        var result = NumberFormatter.Format(new NumberValue(1.5), format);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void EscapedBracketLiteral_HourToken_IsNotTreatedAsElapsedTime()
    {
        // A backslash-escaped '[' is a literal character, not the start of a directive.
        var result = NumberFormatter.Format(new NumberValue(5), "0\\[h]");

        Assert.Equal("5[h]", result);
    }

    [Fact]
    public void ColorDirective_OutsideQuotes_IsStillHonoredAlongsideQuotedBracketLiteral()
    {
        // [Red] is a real color directive (extracted upstream of the elapsed/bracket
        // scan) and must still be honored even when the format also contains an
        // unrelated quoted literal that looks bracket-like ("[hrs]").
        var result = NumberFormatter.FormatWithColor(new NumberValue(4), "[Red]0\"[hrs]\"");

        Assert.Equal("4[hrs]", result.Text);
        Assert.Equal("#FF0000", result.ColorHex);
    }

    [Fact]
    public void DateTimeValue_WithQuotedHourLiteral_IsNotMisroutedIntoElapsedTimePath()
    {
        // ShouldFormatDateTimeValue must not mis-detect a quoted "[h]" literal as an
        // elapsed-time directive when deciding how to route a DateTimeValue cell.
        var result = NumberFormatter.Format(new DateTimeValue(45292), "0\"[h]\"");

        Assert.Equal("45292[h]", result);
    }
}
