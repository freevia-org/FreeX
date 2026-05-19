using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class NumberFormatDecimalAdjusterTests
{
    [Theory]
    [InlineData(null, "0.0")]
    [InlineData("", "0.0")]
    [InlineData("General", "0.0")]
    [InlineData("0", "0.0")]
    [InlineData("#,##0", "#,##0.0")]
    [InlineData("#,##0.00", "#,##0.000")]
    [InlineData("$#,##0.00", "$#,##0.000")]
    public void AddDecimalPlace_AddsOneDecimalSlot(string? format, string expected)
    {
        NumberFormatDecimalAdjuster.AddDecimalPlace(format).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("General", "0")]
    [InlineData("0", "0")]
    [InlineData("#,##0.0", "#,##0")]
    [InlineData("#,##0.00", "#,##0.0")]
    [InlineData("$#,##0.000", "$#,##0.00")]
    public void RemoveDecimalPlace_RemovesOneDecimalSlot(string? format, string expected)
    {
        NumberFormatDecimalAdjuster.RemoveDecimalPlace(format).Should().Be(expected);
    }
}
