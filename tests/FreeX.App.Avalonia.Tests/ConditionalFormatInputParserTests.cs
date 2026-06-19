using FluentAssertions;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the portable parse/validate/format helpers shared by the WPF dialog and the Avalonia
/// rule builder (<see cref="ConditionalFormatInputParser"/>). These lock the text-box round-trip
/// behaviour both editors rely on when committing a rule.
/// </summary>
public sealed class ConditionalFormatInputParserTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" x ", "x")]
    [InlineData("hi", "hi")]
    public void BlankToNull_trims_and_nulls_blank(string? input, string? expected) =>
        ConditionalFormatInputParser.BlankToNull(input).Should().Be(expected);

    [Theory]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("0", true, 0)]
    [InlineData("100", true, 100)]
    [InlineData(" 42 ", true, 42)]
    [InlineData("-1", false, null)]
    [InlineData("101", false, null)]
    [InlineData("abc", false, null)]
    public void TryParseOptionalPercent_validates_range(string input, bool ok, int? expected)
    {
        ConditionalFormatInputParser.TryParseOptionalPercent(input, out var percent).Should().Be(ok);
        if (ok)
            percent.Should().Be(expected);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("1000", true)]
    [InlineData(" 10 ", true)]
    [InlineData("0", false)]
    [InlineData("1001", false)]
    [InlineData("", false)]
    [InlineData("x", false)]
    public void TryParseTopBottomRank_validates_range(string input, bool ok)
    {
        ConditionalFormatInputParser.TryParseTopBottomRank(input, out var rank).Should().Be(ok);
        if (ok)
            rank.Should().Be(int.Parse(input.Trim()));
    }

    [Fact]
    public void FormatRgb_emits_comma_triplet() =>
        ConditionalFormatInputParser.FormatRgb(new RgbColor(1, 22, 255)).Should().Be("1,22,255");

    [Theory]
    [InlineData("255,0,128", true, 255, 0, 128)]
    [InlineData(" 10 , 20 , 30 ", true, 10, 20, 30)]
    [InlineData("0,0,0", true, 0, 0, 0)]
    [InlineData("256,0,0", false, 0, 0, 0)]
    [InlineData("1,2", false, 0, 0, 0)]
    [InlineData("", false, 0, 0, 0)]
    [InlineData("a,b,c", false, 0, 0, 0)]
    public void TryParseRgbColor_parses_triplet(string input, bool ok, byte r, byte g, byte b)
    {
        ConditionalFormatInputParser.TryParseRgbColor(input, out var color).Should().Be(ok);
        if (ok)
            color.Should().Be(new RgbColor(r, g, b));
    }

    [Fact]
    public void ParseOptionalRgbColor_returns_null_for_blank_or_invalid()
    {
        ConditionalFormatInputParser.ParseOptionalRgbColor("").Should().BeNull();
        ConditionalFormatInputParser.ParseOptionalRgbColor("   ").Should().BeNull();
        ConditionalFormatInputParser.ParseOptionalRgbColor("not-a-color").Should().BeNull();
        ConditionalFormatInputParser.ParseOptionalRgbColor("12,34,56").Should().Be(new RgbColor(12, 34, 56));
    }
}
