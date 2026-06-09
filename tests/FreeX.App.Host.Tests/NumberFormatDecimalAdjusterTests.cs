using FluentAssertions;

namespace FreeX.App.Host.Tests;

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
    [InlineData("_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)", "_(* #,##0.000_);_(* (#,##0.000);_(* \"-\"???_);_(@_)")]
    [InlineData("_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)", "_(* #,##0.0_);_(* (#,##0.0);_(* \"-\"_);_(@_)")]
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
    [InlineData("_(* #,##0.000_);_(* (#,##0.000);_(* \"-\"???_);_(@_)", "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)")]
    [InlineData("_(* #,##0.0_);_(* (#,##0.0);_(* \"-\"_);_(@_)", "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)")]
    public void RemoveDecimalPlace_RemovesOneDecimalSlot(string? format, string expected)
    {
        NumberFormatDecimalAdjuster.RemoveDecimalPlace(format).Should().Be(expected);
    }

    [Fact]
    public void DecimalAdjustmentRegexes_AreGeneratedAndCached()
    {
        var source = DialogSourceTestSupport.ReadHostSources("NumberFormatDecimalAdjuster.cs");

        source.Should().Contain("[GeneratedRegex");
        source.Should().NotMatchRegex(@"\bRegex\.Match\s*\(");
    }
}
