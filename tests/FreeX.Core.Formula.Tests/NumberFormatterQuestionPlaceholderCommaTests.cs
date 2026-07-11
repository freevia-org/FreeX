using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression coverage for R28-number-format-render-deep-2-1: the '?' integer-digit
/// placeholder alignment desynced against a literal thousands-separator comma, leaving a
/// genuinely-insignificant leading zero rendered as '0' instead of being blanked to a space.
/// </summary>
public class NumberFormatterQuestionPlaceholderCommaTests
{
    [Theory]
    [InlineData("?,??0", 5, " ,  5")]
    [InlineData("?,??0", 50, " , 50")]
    [InlineData("?,??0", 500, " ,500")]
    [InlineData("?,??,??0", 7, "   ,  7")]
    [InlineData("?,??0;-?,??0", -5, "- ,  5")]
    public void QuestionPlaceholders_BlankInsignificantLeadingZero_AroundLiteralComma(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("??0", 5, "  5")]
    [InlineData("??0", 1234, "1234")]
    [InlineData("?,??0", 5000, "5,000")]
    public void QuestionPlaceholders_StillHandleAlreadyWorkingSiblingCases(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }
}
