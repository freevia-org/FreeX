using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class TextVerticalTypeAuthoringTests
{
    [Theory]
    [InlineData("Horizontal", TextVerticalType.Horizontal)]
    [InlineData("Rotate 90 degrees", TextVerticalType.Vertical)]
    [InlineData("Rotate 270 degrees", TextVerticalType.Vertical270)]
    [InlineData("East Asian vertical", TextVerticalType.EastAsianVertical)]
    [InlineData("WordArt vertical", TextVerticalType.WordArtVertical)]
    [InlineData("WordArt vertical RTL", TextVerticalType.WordArtVerticalRtl)]
    [InlineData("vert", TextVerticalType.Vertical)]
    public void OptionParser_MapsPowerPointChoices(string option, TextVerticalType expected)
    {
        TextVerticalTypeOptionParser.TryParse(option, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Fact]
    public void OptionParser_RejectsUnknownChoice()
    {
        TextVerticalTypeOptionParser.TryParse("Diagonal", out _).Should().BeFalse();
    }
}
