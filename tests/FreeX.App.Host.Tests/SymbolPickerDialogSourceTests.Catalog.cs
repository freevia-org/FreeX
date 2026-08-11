using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host.Tests;

public sealed partial class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_RebuildsSymbolsForSelectedSubset()
    {
        SymbolPickerDialog.GetSymbolsForSubset("Currency Symbols").Should().Contain("\u20ac");
        SymbolPickerDialog.GetSymbolsForSubset("Greek and Coptic").Should().Contain("\u03c0");
        SymbolPickerDialog.GetSymbolsForSubset("Arrows").Should().Contain("\u2192");

        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("SymbolPickerCatalogPlanner.GetSymbolEntriesForSubset(subset)");
        source.Should().Contain("subsetBox.SelectionChanged");
        source.Should().Contain("RefreshSymbols()");
    }

    [Fact]
    public void Dialog_OffersBroaderExcelLikeUnicodeSubsets()
    {
        SymbolPickerDialog.GetSubsetNames().Should().Contain([
            "Latin-1 Supplement",
            "Latin Extended-A",
            "Greek and Coptic",
            "Cyrillic",
            "Hebrew",
            "Arabic",
            "Currency Symbols",
            "Letterlike Symbols",
            "Number Forms",
            "Arrows",
            "Mathematical Operators",
            "Miscellaneous Technical",
            "Box Drawing",
            "Block Elements",
            "Geometric Shapes",
            "Miscellaneous Symbols",
            "Dingbats",
            "Supplemental Arrows"]);

        SymbolPickerDialog.GetSymbolsForSubset("Latin-1 Supplement").Should().Contain("\u00f1");
        SymbolPickerDialog.GetSymbolsForSubset("Cyrillic").Should().Contain("\u0416");
        SymbolPickerDialog.GetSymbolsForSubset("Box Drawing").Should().Contain("\u250c");
        SymbolPickerDialog.GetSymbolsForSubset("Geometric Shapes").Should().Contain("\u25c6");
        SymbolPickerDialog.GetSymbolsForSubset("Arrows").Should().HaveCountGreaterThan(100);
        SymbolPickerDialog.GetSymbolsForSubset("Mathematical Operators").Should().HaveCountGreaterThan(150);
    }

    [Fact]
    public void Dialog_SearchesAcrossBroaderSymbolCatalog()
    {
        SymbolPickerDialog.SearchSymbolEntries("pi")
            .Select(entry => entry.Symbol)
            .Should()
            .Contain("\u03c0");

        SymbolPickerDialog.SearchSymbolEntries("arrow")
            .Select(entry => entry.Symbol)
            .Should()
            .Contain("\u2192");

        SymbolPickerDialog.SearchSymbolEntries("U+20AC")
            .Select(entry => entry.Symbol)
            .Should()
            .Contain("\u20ac");
    }

    [Fact]
    public void Dialog_CatalogPolicyDelegatesToSharedPresentationPlanner()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("using FreeX.App.Presentation.Dialogs;");
        source.Should().Contain("SymbolPickerCatalogPlanner.GetPreferredFontChoices()");
        source.Should().Contain("SymbolPickerCatalogPlanner.GetSubsetNames()");
        source.Should().Contain("SymbolPickerCatalogPlanner.PlanSymbolList(");
        source.Should().Contain("SymbolPickerCatalogPlanner.DefaultRecentSymbols");
        source.Should().Contain("ObservableCollection<SymbolPickerCatalogEntry>");
        source.Should().Contain("IReadOnlyList<SymbolPickerSpecialCharacter> GetSpecialCharacters()");
        source.Should().NotContain("record struct SymbolCatalogEntry");
        source.Should().NotContain("record struct SpecialCharacter");
        source.Should().NotContain("FromPresentation");
        source.Should().NotContain("FriendlySymbolNames");
        source.Should().NotContain("BuildSymbolsBySubset");
        source.Should().NotContain("UnicodeSubsetDefinition");
        source.Should().NotContain("new(\"Latin-1 Supplement\"");
    }

    [Fact]
    public void Dialog_OffersSpecialCharactersSurface()
    {
        SymbolPickerDialog.GetSpecialCharacters().Should().Contain([
            new SymbolPickerSpecialCharacter("Em Dash", "\u2014"),
            new SymbolPickerSpecialCharacter("Nonbreaking Space", "\u00a0"),
            new SymbolPickerSpecialCharacter("Copyright", "\u00a9"),
            new SymbolPickerSpecialCharacter("Registered", "\u00ae"),
            new SymbolPickerSpecialCharacter("Trademark", "\u2122")]);
        SymbolPickerDialog.GetSpecialCharacters().Should().Contain([
            new SymbolPickerSpecialCharacter("Nonbreaking Hyphen", "\u2011"),
            new SymbolPickerSpecialCharacter("Less-Than Or Equal", "\u2264"),
            new SymbolPickerSpecialCharacter("Check Mark", "\u2713")]);
        SymbolPickerDialog.GetSpecialCharacters().Should().HaveCountGreaterThan(35);

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
        SymbolPickerCatalogPlanner.CreateSelection(symbol)
            .Should()
            .Be(new SymbolPickerSelectionPlan(symbol, selectedChar, codeText));
    }
}
