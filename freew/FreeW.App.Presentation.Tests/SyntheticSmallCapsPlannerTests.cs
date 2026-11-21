using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class SyntheticSmallCapsPlannerTests
{
    [Fact]
    public void MixedCaseText_SplitsIntoCapitalAndSmallCapitalSegments()
    {
        var segments = SyntheticSmallCapsPlanner.Split("Bureau DE ligue");

        segments.Should().Equal(
            new SyntheticSmallCapsPlanner.Segment("B", false),
            new SyntheticSmallCapsPlanner.Segment("UREAU", true),
            new SyntheticSmallCapsPlanner.Segment(" DE ", false),
            new SyntheticSmallCapsPlanner.Segment("LIGUE", true));
    }

    [Fact]
    public void SegmentsConcatenateToTheUpperCasedText()
    {
        var segments = SyntheticSmallCapsPlanner.Split("été 2024, naïve");

        string.Concat(segments.Select(segment => segment.Text)).Should().Be("ÉTÉ 2024, NAÏVE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ALREADY UPPER 123")]
    public void TextWithoutLowerCaseLetters_KeepsThePlainRun(string text)
    {
        SyntheticSmallCapsPlanner.Split(text).Should().BeEmpty();
    }

    [Fact]
    public void SmallCapitalsAreEightyPercentOfTheRunSize()
    {
        SyntheticSmallCapsPlanner.SmallCapScale.Should().Be(0.8);
    }
}
