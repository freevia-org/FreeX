using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Asserts that <see cref="NumberFormatColorMapper.TryMapColor"/> returns the Excel legacy
/// number-format palette hex values for all eight named colors. The legacy palette predates
/// Office brand colors — [Green] is pure green (#00FF00) and [Blue] is pure blue (#0000FF),
/// NOT the Office brand greens/blues used in chart fills.
/// Reference: ECMA-376 §18.8.31 &amp; Excel built-in number format behavior.
/// </summary>
public sealed class NumberFormatColorMapperTests
{
    [Theory]
    [InlineData("BLACK",   "#000000")]
    [InlineData("White",   "#FFFFFF")]
    [InlineData("RED",     "#FF0000")]
    [InlineData("Green",   "#00FF00")]   // legacy palette: pure green, NOT Office #00B050
    [InlineData("blue",    "#0000FF")]   // legacy palette: pure blue, NOT Office #0070C0
    [InlineData("YELLOW",  "#FFFF00")]
    [InlineData("CYAN",    "#00FFFF")]
    [InlineData("MAGENTA", "#FF00FF")]
    public void TryMapColor_NamedColors_ReturnExcelLegacyPaletteHex(string token, string expectedHex)
    {
        var found = NumberFormatColorMapper.TryMapColor(token, out var hex);

        found.Should().BeTrue(because: $"[{token}] is a recognised named color");
        hex.Should().Be(expectedHex, because: $"[{token}] maps to the Excel legacy palette color {expectedHex}");
    }

    [Theory]
    [InlineData("GREEN", "#00FF00")]
    [InlineData("BLUE",  "#0000FF")]
    public void TryMapColor_GreenAndBlue_AreNotOfficeBrandColors(string token, string expectedHex)
    {
        // Guard: these were previously returning Office brand colors (#00B050 / #0070C0).
        // This test locks in the correct Excel legacy palette values.
        NumberFormatColorMapper.TryMapColor(token, out var hex);
        hex.Should().NotBe("#00B050", because: "[Green] legacy hex is #00FF00, not Office brand #00B050");
        hex.Should().NotBe("#0070C0", because: "[Blue] legacy hex is #0000FF, not Office brand #0070C0");
        hex.Should().Be(expectedHex);
    }

    [Theory]
    [InlineData("[Green]0.00", "#00FF00", "0.00")]
    [InlineData("[Blue]#,##0", "#0000FF", "#,##0")]
    [InlineData("[Red]0",      "#FF0000", "0")]
    public void ExtractColor_LeadingNamedColorDirective_StripsAndMapsToLegacyHex(
        string format, string expectedHex, string expectedStripped)
    {
        var (color, stripped) = NumberFormatColorMapper.ExtractColor(format);

        color.Should().Be(expectedHex);
        stripped.Should().Be(expectedStripped);
    }

    [Fact]
    public void TryMapColor_UnknownToken_ReturnsFalse()
    {
        var found = NumberFormatColorMapper.TryMapColor("PURPLE", out var hex);
        found.Should().BeFalse();
        hex.Should().BeNull();
    }
}
