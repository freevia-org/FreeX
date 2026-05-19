using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ColorInputParserTests
{
    [Theory]
    [InlineData("#217346", 0x21, 0x73, 0x46)]
    [InlineData("217346", 0x21, 0x73, 0x46)]
    [InlineData("  #Aa10fF  ", 0xAA, 0x10, 0xFF)]
    public void TryParseHexColor_AcceptsHashOrPlainSixDigitHex(string input, byte r, byte g, byte b)
    {
        ColorInputParser.TryParseHexColor(input, out var color).Should().BeTrue();
        color.Should().Be(new CellColor(r, g, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#12GG34")]
    public void TryParseHexColor_RejectsInvalidText(string input)
    {
        ColorInputParser.TryParseHexColor(input, out var color).Should().BeFalse();
        color.Should().BeNull();
    }

    [Theory]
    [InlineData("#217346", 0x21, 0x73, 0x46)]
    [InlineData("217346", 0x21, 0x73, 0x46)]
    [InlineData("33, 115, 70", 33, 115, 70)]
    [InlineData("33,115,70", 33, 115, 70)]
    public void TryParseColorText_AcceptsHexOrRgbTriples(string input, byte r, byte g, byte b)
    {
        ColorInputParser.TryParseColorText(input, out var color).Should().BeTrue();
        color.Should().Be(new CellColor(r, g, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("1,2")]
    [InlineData("1,2,300")]
    [InlineData("red")]
    public void TryParseColorText_RejectsInvalidColorText(string input)
    {
        ColorInputParser.TryParseColorText(input, out var color).Should().BeFalse();
        color.Should().Be(default(CellColor));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("clear")]
    [InlineData(" NONE ")]
    public void TryParseOptionalHexColor_TreatsClearKeywordsAsNull(string input)
    {
        ColorInputParser.TryParseOptionalHexColor(input, out var color).Should().BeTrue();
        color.Should().BeNull();
    }

    [Fact]
    public void FormatHexColor_ReturnsUppercaseHashRgb()
    {
        ColorInputParser.FormatHexColor(new CellColor(0x21, 0x73, 0x46)).Should().Be("#217346");
    }
}
