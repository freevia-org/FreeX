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
}
