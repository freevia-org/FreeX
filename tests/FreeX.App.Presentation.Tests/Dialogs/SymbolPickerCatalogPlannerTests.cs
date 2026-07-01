using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class SymbolPickerCatalogPlannerTests
{
    [Fact]
    public void GetSubsetNames_OffersExcelLikeUnicodeSubsets()
    {
        SymbolPickerCatalogPlanner.GetSubsetNames().Should().Contain([
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
    }

    [Fact]
    public void GetPreferredFontChoices_OffersSharedSymbolPickerFonts()
    {
        SymbolPickerCatalogPlanner.GetPreferredFontChoices().Should().Contain([
            "Segoe UI Symbol",
            "Segoe UI Emoji",
            "Segoe UI Historic",
            "Cambria Math",
            "Symbol",
            "Wingdings",
            "Webdings"]);
    }

    [Fact]
    public void GetSymbolsForSubset_BuildsBroaderCatalogFromUnicodeRanges()
    {
        SymbolPickerCatalogPlanner.GetSymbolsForSubset("Currency Symbols").Should().Contain("\u20ac");
        SymbolPickerCatalogPlanner.GetSymbolsForSubset("Greek and Coptic").Should().Contain("\u03c0");
        SymbolPickerCatalogPlanner.GetSymbolsForSubset("Latin-1 Supplement").Should().Contain("\u00f1");
        SymbolPickerCatalogPlanner.GetSymbolsForSubset("Cyrillic").Should().Contain("\u0416");
        SymbolPickerCatalogPlanner.GetSymbolsForSubset("Box Drawing").Should().Contain("\u250c");
        SymbolPickerCatalogPlanner.GetSymbolsForSubset("Geometric Shapes").Should().Contain("\u25c6");
        SymbolPickerCatalogPlanner.GetSymbolsForSubset("Arrows").Should().HaveCountGreaterThan(100);
        SymbolPickerCatalogPlanner.GetSymbolsForSubset("Mathematical Operators").Should().HaveCountGreaterThan(150);
    }

    [Fact]
    public void SearchSymbolEntries_FiltersAcrossTheWholeCatalog()
    {
        SymbolPickerCatalogPlanner.SearchSymbolEntries("pi")
            .Select(entry => entry.Symbol)
            .Should()
            .Contain("\u03c0");

        SymbolPickerCatalogPlanner.SearchSymbolEntries("arrow")
            .Select(entry => entry.Symbol)
            .Should()
            .Contain("\u2192");

        SymbolPickerCatalogPlanner.SearchSymbolEntries("U+20AC")
            .Select(entry => entry.Symbol)
            .Should()
            .Contain("\u20ac");
    }

    [Fact]
    public void PlanSymbolList_UsesSubsetWithoutSearchAndSelectsVisibleCurrentSymbol()
    {
        var plan = SymbolPickerCatalogPlanner.PlanSymbolList(
            "Greek and Coptic",
            "",
            "\u03c0");

        plan.HasResults.Should().BeTrue();
        plan.Entries.Select(entry => entry.Symbol).Should().Contain("\u03c0");
        plan.SelectedEntry.Should().Be(new SymbolPickerCatalogEntry(
            "\u03c0",
            "Greek Small Letter Pi",
            "Greek and Coptic",
            "03C0"));
    }

    [Fact]
    public void PlanSymbolList_SearchesAcrossSubsetsAndFallsBackToFirstResult()
    {
        var plan = SymbolPickerCatalogPlanner.PlanSymbolList(
            "Latin-1 Supplement",
            "right arrow",
            "\u03c0");

        plan.HasResults.Should().BeTrue();
        plan.Entries.Select(entry => entry.Symbol).Should().Contain("\u2192");
        plan.SelectedEntry.Should().NotBeNull();
        plan.SelectedEntry!.Value.Symbol.Should().Be(plan.Entries[0].Symbol);
    }

    [Fact]
    public void PlanSymbolList_ReportsNoResults()
    {
        var plan = SymbolPickerCatalogPlanner.PlanSymbolList(
            "Latin-1 Supplement",
            "definitelynotasymbol",
            "\u20ac");

        plan.HasResults.Should().BeFalse();
        plan.Entries.Should().BeEmpty();
        plan.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void GetSpecialCharacters_OffersWordLikeSpecialCharacterSurface()
    {
        SymbolPickerCatalogPlanner.GetSpecialCharacters().Should().Contain([
            new SymbolPickerSpecialCharacter("Em Dash", "\u2014"),
            new SymbolPickerSpecialCharacter("Nonbreaking Space", "\u00a0"),
            new SymbolPickerSpecialCharacter("Copyright", "\u00a9"),
            new SymbolPickerSpecialCharacter("Registered", "\u00ae"),
            new SymbolPickerSpecialCharacter("Trademark", "\u2122")]);
        SymbolPickerCatalogPlanner.GetSpecialCharacters().Should().Contain([
            new SymbolPickerSpecialCharacter("Nonbreaking Hyphen", "\u2011"),
            new SymbolPickerSpecialCharacter("Less-Than Or Equal", "\u2264"),
            new SymbolPickerSpecialCharacter("Check Mark", "\u2713")]);
        SymbolPickerCatalogPlanner.GetSpecialCharacters().Should().HaveCountGreaterThan(35);
    }

    [Theory]
    [InlineData("03C0", "\u03c0")]
    [InlineData("U+2192", "\u2192")]
    [InlineData("1F600", "\ud83d\ude00")]
    public void TryParseCharacterCode_AcceptsUnicodeHex(string text, string expected)
    {
        SymbolPickerCatalogPlanner.TryParseCharacterCode(text, out var symbol).Should().BeTrue();
        symbol.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("D800")]
    [InlineData("110000")]
    public void TryParseCharacterCode_RejectsInvalidUnicodeHex(string text)
    {
        SymbolPickerCatalogPlanner.TryParseCharacterCode(text, out var symbol).Should().BeFalse();
        symbol.Should().BeEmpty();
    }

    [Fact]
    public void PromoteRecentSymbol_MovesSelectedSymbolToFrontAndTrims()
    {
        var recent = SymbolPickerCatalogPlanner.PromoteRecentSymbol(
            ["\u20ac", "\u00a3", "\u00a5"],
            "\u03c0",
            capacity: 3);

        recent.Should().Equal(["\u03c0", "\u20ac", "\u00a3"]);

        SymbolPickerCatalogPlanner.PromoteRecentSymbol(recent, "\u20ac", capacity: 3)
            .Should().Equal(["\u20ac", "\u03c0", "\u00a3"]);
    }

    [Theory]
    [InlineData("\u03c0", '\u03c0', "03C0")]
    [InlineData("\ud83d\ude00", '\0', "1F600")]
    [InlineData("", '\0', "")]
    public void CreateSelection_FormatsSelectedSymbolState(string symbol, char selectedChar, string codeText)
    {
        SymbolPickerCatalogPlanner.CreateSelection(symbol)
            .Should()
            .Be(new SymbolPickerSelectionPlan(symbol, selectedChar, codeText));
    }
}
