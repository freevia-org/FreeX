using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Presentation.Tests;

public sealed class RunCharacterSpacingPlannerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 4)]
    [InlineData(-0.75, -1)]
    [InlineData(0.001, 0)]
    public void ResolveAdvanceDip_ConvertsPointsToDips(double spacingPt, double expectedDip) =>
        RunCharacterSpacingPlanner
            .ResolveAdvanceDip(RunFormatting.Default with { CharacterSpacingPt = spacingPt })
            .Should().BeApproximately(expectedDip, 0.001);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ResolveAdvanceDip_IgnoresNonFiniteSpacing(double spacingPt) =>
        RunCharacterSpacingPlanner
            .ResolveAdvanceDip(RunFormatting.Default with { CharacterSpacingPt = spacingPt })
            .Should().Be(0);

    [Fact]
    public void ResolveAdvanceDip_ClampsToWordsLimit() =>
        RunCharacterSpacingPlanner
            .ResolveAdvanceDip(RunFormatting.Default with { CharacterSpacingPt = 99999 }, 1)
            .Should().Be(RunCharacterSpacingPlanner.MaximumAbsolutePt);

    [Fact]
    public void ResolveAdvanceDip_RejectsABadScale() =>
        RunCharacterSpacingPlanner
            .ResolveAdvanceDip(RunFormatting.Default with { CharacterSpacingPt = 2 }, double.NaN)
            .Should().Be(0);

    [Fact]
    public void CountSpacedElements_SkipsZeroWidthFormatCharacters()
    {
        RunCharacterSpacingPlanner.CountSpacedElements("ab c").Should().Be(4);
        RunCharacterSpacingPlanner.CountSpacedElements("a⁠b­c").Should().Be(3);
        RunCharacterSpacingPlanner.CountSpacedElements("éx").Should().Be(2);
        RunCharacterSpacingPlanner.CountSpacedElements("").Should().Be(0);
    }

    [Theory]
    [InlineData("plain text", true)]
    [InlineData("a\tb", false)]
    [InlineData("a\nb", false)]
    [InlineData("", false)]
    public void CanPaintSpaced_RejectsTabsAndLineBreaks(string text, bool expected) =>
        RunCharacterSpacingPlanner.CanPaintSpaced(text).Should().Be(expected);
}
