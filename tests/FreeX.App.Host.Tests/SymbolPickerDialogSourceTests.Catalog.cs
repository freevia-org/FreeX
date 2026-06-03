using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_RebuildsSymbolsForSelectedSubset()
    {
        SymbolPickerDialog.GetSymbolsForSubset("Currency Symbols").Should().Contain('\u20ac');
        SymbolPickerDialog.GetSymbolsForSubset("Greek and Coptic").Should().Contain('\u03c0');
        SymbolPickerDialog.GetSymbolsForSubset("Arrows").Should().Contain('\u2192');

        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("SymbolsBySubset");
        source.Should().Contain("subsetBox.SelectionChanged");
        source.Should().Contain("PopulateGrid(subset)");
    }

    [Fact]
    public void Dialog_OffersBroaderExcelLikeUnicodeSubsets()
    {
        SymbolPickerDialog.GetSubsetNames().Should().Contain([
            "Latin-1 Supplement",
            "Greek and Coptic",
            "Cyrillic",
            "Currency Symbols",
            "Arrows",
            "Mathematical Operators",
            "Box Drawing",
            "Geometric Shapes"]);

        SymbolPickerDialog.GetSymbolsForSubset("Latin-1 Supplement").Should().Contain('\u00f1');
        SymbolPickerDialog.GetSymbolsForSubset("Cyrillic").Should().Contain('\u0416');
        SymbolPickerDialog.GetSymbolsForSubset("Box Drawing").Should().Contain('\u250c');
        SymbolPickerDialog.GetSymbolsForSubset("Geometric Shapes").Should().Contain('\u25c6');
    }

    [Fact]
    public void Dialog_OffersSpecialCharactersSurface()
    {
        SymbolPickerDialog.GetSpecialCharacters().Should().Contain([
            new SymbolPickerDialog.SpecialCharacter("Em Dash", "\u2014"),
            new SymbolPickerDialog.SpecialCharacter("Nonbreaking Space", "\u00a0"),
            new SymbolPickerDialog.SpecialCharacter("Copyright", "\u00a9"),
            new SymbolPickerDialog.SpecialCharacter("Registered", "\u00ae"),
            new SymbolPickerDialog.SpecialCharacter("Trademark", "\u2122")]);

        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Header = UiText.Get(\"SymbolPicker_SymbolsTab\")");
        source.Should().Contain("Header = UiText.Get(\"SymbolPicker_SpecialCharactersTab\")");
    }

    [Theory]
    [InlineData("03C0", "\u03c0")]
    [InlineData("U+2192", "\u2192")]
    [InlineData("1F600", "\ud83d\ude00")]
    public void Dialog_ParsesUnicodeCharacterCodeEntries(string text, string expected)
    {
        SymbolPickerDialog.TryParseCharacterCode(text, out var symbol).Should().BeTrue();
        symbol.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("D800")]
    [InlineData("110000")]
    public void Dialog_RejectsInvalidUnicodeCharacterCodeEntries(string text)
    {
        SymbolPickerDialog.TryParseCharacterCode(text, out var symbol).Should().BeFalse();
        symbol.Should().BeEmpty();
    }

    [Fact]
    public void Dialog_PromotesSelectedSymbolsIntoRecentList()
    {
        var recent = SymbolPickerDialog.PromoteRecentSymbol(
            ["\u20ac", "\u00a3", "\u00a5"],
            "\u03c0",
            capacity: 3);

        recent.Should().Equal(["\u03c0", "\u20ac", "\u00a3"]);

        SymbolPickerDialog.PromoteRecentSymbol(recent, "\u20ac", capacity: 3)
            .Should().Equal(["\u20ac", "\u03c0", "\u00a3"]);
    }

    [Theory]
    [InlineData("\u03c0", '\u03c0', "03C0")]
    [InlineData("\ud83d\ude00", '\0', "1F600")]
    [InlineData("", '\0', "")]
    public void SelectionPlanner_FormatsSelectedSymbolState(string symbol, char selectedChar, string codeText)
    {
        SymbolPickerSelectionPlanner.CreateSelection(symbol)
            .Should()
            .Be(new SymbolPickerSelection(symbol, selectedChar, codeText));
    }
}
