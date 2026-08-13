using FreeX.App.Services;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FormatCellsBorderPalettePlannerTests
{
    [Fact]
    public void StyleChoices_CoverEveryModelStyleExactlyOnce()
    {
        FormatCellsBorderPalettePlanner.StyleChoices.Select(choice => choice.Style)
            .Should().Equal(Enum.GetValues<BorderStyle>());
        FormatCellsBorderPalettePlanner.StyleChoices.Select(choice => choice.DisplayName)
            .Should().OnlyHaveUniqueItems().And.NotContain(string.Empty);
    }

    [Fact]
    public void ColorEntries_PreserveTheWpfBorderPaletteAndMoreCommand()
    {
        FormatCellsBorderPalettePlanner.ColorEntries.Select(entry => entry.Color)
            .Should().Equal(
                new CellColor(0, 0, 0),
                new CellColor(128, 128, 128),
                new CellColor(255, 0, 0),
                new CellColor(255, 192, 0),
                new CellColor(0, 176, 80),
                new CellColor(0, 112, 192),
                new CellColor(112, 48, 160),
                null);
        FormatCellsBorderPalettePlanner.ColorEntries[^1].IsMore.Should().BeTrue();
        FormatCellsBorderPalettePlanner.ColorEntries.Take(7).Should().OnlyContain(entry => entry.IsColor);
    }

    [Fact]
    public void ChoiceFor_RoundTripsTypedStyleWithoutUsingItsDisplayName()
    {
        foreach (var style in Enum.GetValues<BorderStyle>())
            FormatCellsBorderPalettePlanner.ChoiceFor(style).Style.Should().Be(style);
    }
}
