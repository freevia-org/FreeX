using FluentAssertions;
using FreeX.Core.IO;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the rebuilt-chain CellValues bug: a full ClosedXML re-save round-trips an
/// astral character (emoji) in a cached formula-string value into ClosedXML's literal <c>_xHHHH_</c>
/// surrogate-half escape on reload. <see cref="XlsxClosedXmlCellMapper.DecodeUnresolvedXmlHexEscapes"/>
/// re-assembles those escapes back into the real character.
/// </summary>
public sealed class XlsxFormulaStringEmojiRoundTripTests
{
    [Theory]
    // Astral emoji emitted as a surrogate-pair escape must recombine to the original character.
    [InlineData("_xD83C__xDF89_Another Thing", "\U0001F389Another Thing")]   // party popper U+1F389
    [InlineData("_xD83C__xDF82_Wedding Anniversary", "\U0001F382Wedding Anniversary")] // cake U+1F382
    [InlineData("✈Flight\n_xD83C__xDF89_Done", "✈Flight\n\U0001F389Done")]   // BMP char untouched, astral decoded
    [InlineData("_xD83D__xDC68_‍_xD83D__xDC67_‍_xD83D__xDC66_X", "\U0001F468‍\U0001F467‍\U0001F466X")] // ZWJ family
    public void DecodeUnresolvedXmlHexEscapes_AstralEscape_RecombinesToCharacter(string input, string expected)
    {
        XlsxClosedXmlCellMapper.DecodeUnresolvedXmlHexEscapes(input).Should().Be(expected);
    }

    [Theory]
    // No surrogate-half escape present -> text is returned verbatim (BMP _xHHHH_ escapes are not touched,
    // because Excel re-escapes those on every save so they never reach the model as literal text).
    [InlineData("plain text")]
    [InlineData("price _x0024_ is special")]   // _x0024_ is a BMP escape, left as-is by design
    [InlineData("")]
    [InlineData("no escape but has _ underscores _x_ partial")]
    public void DecodeUnresolvedXmlHexEscapes_NoSurrogateEscape_ReturnsInput(string input)
    {
        XlsxClosedXmlCellMapper.DecodeUnresolvedXmlHexEscapes(input).Should().Be(input);
    }

    [Fact]
    public void DecodeUnresolvedXmlHexEscapes_LoneHighSurrogateEscape_EmitsSurrogateUnit()
    {
        // A lone high-surrogate escape (no following low surrogate) is emitted as the raw code unit so
        // nothing is dropped; the result string length reflects the single surrogate.
        var result = XlsxClosedXmlCellMapper.DecodeUnresolvedXmlHexEscapes("_xD83C_orphan");
        result.Should().Be("\uD83Corphan");
    }
}
