using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class TextAutoFitAuthoringTests
{
    [Theory]
    [InlineData("Do not autofit", TextAutoFitKind.None)]
    [InlineData("Shrink text on overflow", TextAutoFitKind.Normal)]
    [InlineData("Resize shape to fit text", TextAutoFitKind.Shape)]
    [InlineData("Normal", TextAutoFitKind.Normal)]
    [InlineData("Shape", TextAutoFitKind.Shape)]
    public void OptionParser_MapsPowerPointChoices(string option, TextAutoFitKind expected)
    {
        TextAutoFitOptionParser.TryParse(option, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Fact]
    public void OptionParser_RejectsUnknownChoice()
    {
        TextAutoFitOptionParser.TryParse("Scale everything", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("Columns 4", 4)]
    [InlineData("32", 32)]
    public void TextColumnCountParser_MapsPositiveCounts(string option, int expected)
    {
        TextColumnCountOptionParser.TryParse(option, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("33")]
    [InlineData("many")]
    public void TextColumnCountParser_RejectsInvalidCounts(string option)
    {
        TextColumnCountOptionParser.TryParse(option, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("0 pt", 0L)]
    [InlineData("4 pt", 50_800L)]
    [InlineData("12", 152_400L)]
    public void TextColumnSpacingParser_MapsPointChoices(string option, long expectedEmu)
    {
        TextColumnSpacingOptionParser.TryParse(option, out var actual).Should().BeTrue();
        actual.Should().Be(expectedEmu);
    }

    [Theory]
    [InlineData("-1 pt")]
    [InlineData("145 pt")]
    [InlineData("automatic")]
    public void TextColumnSpacingParser_RejectsInvalidChoices(string option)
    {
        TextColumnSpacingOptionParser.TryParse(option, out _).Should().BeFalse();
    }
}
