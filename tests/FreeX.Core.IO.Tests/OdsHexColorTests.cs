using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Colours in an .ods style come straight from the file. The length check on the hex string does not
/// make its characters hex, so a crafted style such as fo:color="#GGGGGG" threw FormatException from
/// the middle of the load. Every other adapter here parses hostile file data with TryParse and falls
/// back; this was the outlier.
/// </summary>
public sealed class OdsHexColorTests
{
    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("#12-45G")]
    [InlineData("######")]
    [InlineData("      ")]
    public void ParseHexColor_NonHexSixCharacterValue_FallsBackInsteadOfThrowing(string value)
    {
        var parse = () => OdsBorder.ParseHexColor(value);

        parse.Should().NotThrow();
        OdsBorder.ParseHexColor(value).Should().Be(CellColor.Black);
    }

    [Fact]
    public void ParseHexColor_ValidValue_StillParses()
    {
        OdsBorder.ParseHexColor("#204080").Should().Be(new CellColor(0x20, 0x40, 0x80));
    }
}
