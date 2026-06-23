using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class SymbolPickerSelectionPlannerTests
{
    [Theory]
    [InlineData("\u20ac", '\u20ac', "20AC")]
    [InlineData("\ud83d\ude00", '\0', "1F600")]
    [InlineData("", '\0', "")]
    public void CreateSelection_NormalizesSymbolAndFormatsCodeText(string symbol, char selectedChar, string codeText)
    {
        var selection = SymbolPickerSelectionPlanner.CreateSelection(symbol);

        selection.Symbol.Should().Be(symbol);
        selection.SelectedChar.Should().Be(selectedChar);
        selection.CodeText.Should().Be(codeText);
    }

    [Fact]
    public void PromoteRecentSymbol_MovesSelectedSymbolToFrontAndDeduplicates()
    {
        SymbolPickerSelectionPlanner.PromoteRecentSymbol(["\u00a3", "\u20ac", "\u00a5"], "\u20ac", capacity: 3)
            .Should()
            .Equal("\u20ac", "\u00a3", "\u00a5");
    }
}
