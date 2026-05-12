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
        Assert.Equal("1/1/2024", result);
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
