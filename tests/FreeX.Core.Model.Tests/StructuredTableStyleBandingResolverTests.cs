using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class StructuredTableStyleBandingResolverTests
{
    // Excel's built-in "Light" table styles 8-14 render a BLACK header row (Text1/dark1) with white
    // bold text and an unbanded white body — NOT the light-grey accent header used by Light 1-7.
    // The contextures "expiry dates" workbook uses TableStyleLight8, whose real Excel render is a solid
    // black header; FreeX must resolve the same so the GridView matches the Excel ground truth.
    [Theory]
    [InlineData("TableStyleLight8")]
    [InlineData("TableStyleLight9")]
    [InlineData("TableStyleLight10")]
    [InlineData("TableStyleLight14")]
    public void Resolve_LightStyles8Through14_UseBlackHeaderWithWhiteFontAndWhiteBody(string styleName)
    {
        var banding = StructuredTableStyleBandingResolver.Resolve(styleName, WorkbookTheme.Office);

        banding.HeaderFill.Should().Be(CellColor.Black);
        banding.HeaderFontColor.Should().Be(CellColor.White);
        banding.EvenRowFill.Should().Be(CellColor.White);
        banding.OddRowFill.Should().Be(CellColor.White);
    }
}
