using Freexcel.Core.Calc;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Calc.Tests;

public class NumberFormatterTests
{
    [Theory]
    [InlineData("General", 42.0,    "42")]
    [InlineData("General", 42.5,    "42.5")]
    [InlineData("0.00",    42.0,    "42.00")]
    [InlineData("0.00",    3.14159, "3.14")]
    [InlineData("#,##0",   1234567.0, "1,234,567")]
    [InlineData("0%",      0.42,    "42%")]
    [InlineData("0.0%",    0.4225,  "42.3%")]
    public void Format_NumberValue_AppliesFormatString(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1234.5, "$ 1,234.50")]
    [InlineData(-1234.5, "$ (1,234.50)")]
    [InlineData(0, "$ -")]
    public void AccountingSubset_RemovesSpacingDirectivesAndPreservesVisibleLiterals(double value, string expected)
    {
        const string format = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)";

        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("#,##0.0###", 1234.567, "1,234.567")]
    [InlineData("# ?/?", 0.125, "1/8")]
    [InlineData("0.00E+00\" kg\"", 1200, "1.20E+03 kg")]
    [InlineData("0.00E+00", 1200, "1.20E+03")]
    [InlineData("0.00E-00", 1200, "1.20E03")]
    [InlineData("0*-", 12, "12")]
    public void CustomNumberSubset_FormatsVariableDecimalsFractionsAndScientific(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0.00;0.00", -1.25, "1.25")]
    [InlineData("0.00;-0.00", -1.25, "-1.25")]
    [InlineData("# ?/?;# ?/?", -0.125, "1/8")]
    [InlineData("# ?/?;-# ?/?", -0.125, "-1/8")]
    public void CustomNumberSubset_FormatsNegativeSectionsUsingAbsoluteValue(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[>100]0.0;[<=100]0.00", 125, "125.0")]
    [InlineData("[>100]0.0;[<=100]0.00", 25, "25.00")]
    [InlineData("[<0]0.0;[=0]\"zero\";0.00", 0, "zero")]
    public void CustomNumberSubset_UsesConditionalSections(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[Red][<0]0.00;[Blue]0.00", -2.5, "-2.50", "#FF0000")]
    [InlineData("[Red][<0]0.00;[Blue]0.00", 2.5, "2.50", "#0070C0")]
    [InlineData("[Color3][<0]0.00;[Color5]0.00", -2.5, "-2.50", "#FF0000")]
    [InlineData("[Color3][<0]0.00;[Color5]0.00", 2.5, "2.50", "#0070C0")]
    [InlineData("[Color6]0.00", 2.5, "2.50", "#FFFF00")]
    public void CustomNumberSubset_ReturnsColorFromConditionalSections(
        string format,
        double value,
        string expectedText,
        string expectedColor)
    {
        var result = NumberFormatter.FormatWithColor(new NumberValue(value), format);

        Assert.Equal(expectedText, result.Text);
        Assert.Equal(expectedColor, result.ColorHex);
    }

    [Theory]
    [InlineData("0\\ kg", 12, "12 kg")]
    [InlineData("\\#0", 12, "#12")]
    [InlineData("0\\,", 12, "12,")]
    [InlineData("0,,", 1234567, "1")]
    [InlineData("0.0,", 12345, "12.3")]
    public void CustomNumberSubset_HandlesEscapedLiteralsAndCommaScaling(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[$\u20AC-407]#,##0.00", 1234.5, "\u20AC1,234.50")]
    [InlineData("[$\u00A3-809] #,##0.00", 1234.5, "\u00A3 1,234.50")]
    [InlineData("[$-409]#,##0.00", 1234.5, "1,234.50")]
    [InlineData("-[$\u20AC-407]#,##0.00", 1234.5, "-\u20AC1,234.50")]
    [InlineData("([$\u20AC-407]#,##0.00)", 1234.5, "(\u20AC1,234.50)")]
    [InlineData("[$\u20AC-407]* #,##0.00", 1234.5, "\u20AC 1,234.50")]
    [InlineData("[$\u20AC-407]* \"-\"??", 0, "\u20AC -")]
    public void CustomNumberSubset_PreservesVisibleCurrencyFromLocaleTokens(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CustomNumberSubset_FormatsQuotedOnlyZeroSectionAsLiteral()
    {
        var result = NumberFormatter.Format(new NumberValue(0), "0;0;\"-\"");

        Assert.Equal("-", result);
    }

    [Theory]
    [InlineData("\"0\"", 12, "0")]
    [InlineData("\"??\"", 12, "??")]
    public void CustomNumberSubset_TreatsQuotedPlaceholdersAsLiterals(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_DateSerial_WithDateFormat_ReturnsFormattedDate()
    {
        // OADate 45292 = 2024-01-01
        var result = NumberFormatter.Format(new NumberValue(45292), "m/d/yyyy");
        Assert.Equal("1/1/2024", result);
    }

    [Fact]
    public void Format_DateTimeValue_WithGeneralFormat_ReturnsShortDate()
    {
        var result = NumberFormatter.Format(new DateTimeValue(45292), "General");
        Assert.Equal("01/01/2024", result);
    }

    [Theory]
    [InlineData("General", "hello", "hello")]
    [InlineData("@",       "hello", "hello")]
    public void Format_TextValue_PassesThrough(string format, string value, string expected)
    {
        var result = NumberFormatter.Format(new TextValue(value), format);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_BlankValue_ReturnsEmpty()
    {
        Assert.Equal("", NumberFormatter.Format(BlankValue.Instance, "General"));
    }

    [Fact]
    public void Format_ErrorValue_ReturnsCode()
    {
        Assert.Equal("#DIV/0!", NumberFormatter.Format(new ErrorValue("#DIV/0!"), "General"));
    }
}
