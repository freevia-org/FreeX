using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

/// <summary>
/// R91-commands-insert-object-5-3: Insert Symbol's Font selector (Wingdings/Webdings/etc.) never
/// changed the catalog -- <see cref="SymbolPickerCatalogPlanner.PlanSymbolList"/> always returned
/// the fixed Unicode symbol table regardless of which font was chosen, so a user picking
/// "Wingdings" got the same Latin/Unicode glyphs shown for every other font instead of that font's
/// own dingbat glyph set. These tests pin the fixed behavior: picking a recognized Symbol-charset
/// font swaps the catalog to that font's own Private Use Area codepoints (the same convention
/// Windows/OOXML use to represent such fonts' characters as Unicode text), while non-symbol fonts
/// keep using the pre-existing Unicode subset/search catalog untouched.
/// </summary>
public sealed class R91_SymbolPickerFontCatalogTests
{
    [Theory]
    [InlineData("Wingdings")]
    [InlineData("Wingdings 2")]
    [InlineData("Wingdings 3")]
    [InlineData("Webdings")]
    public void IsSymbolFont_RecognizesDingbatFontFamily(string fontName)
    {
        SymbolPickerCatalogPlanner.IsSymbolFont(fontName).Should().BeTrue();
    }

    [Theory]
    [InlineData("Calibri")]
    [InlineData("Segoe UI Symbol")]
    [InlineData("Symbol")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSymbolFont_RejectsNonDingbatFonts(string? fontName)
    {
        SymbolPickerCatalogPlanner.IsSymbolFont(fontName).Should().BeFalse();
    }

    [Fact]
    public void PlanSymbolList_WithSymbolFont_SwitchesCatalogToThatFontsOwnGlyphSet()
    {
        // Before the fix, PlanSymbolList had no font parameter at all: the picker always showed
        // "Latin-1 Supplement" (or whatever subset was selected) no matter which font was chosen.
        var plan = SymbolPickerCatalogPlanner.PlanSymbolList(
            "Latin-1 Supplement",
            "",
            selectedSymbol: null,
            fontName: "Wingdings");

        plan.HasResults.Should().BeTrue();
        plan.Entries.Should().HaveCount(224);
        plan.Entries.Should().OnlyContain(entry => entry.Subset == "Wingdings");
        plan.Entries.Should().NotContain(entry => entry.Subset == "Latin-1 Supplement");

        // First glyph is the font's own raw code 0x20 mapped into the Private Use Area at U+F020 --
        // not any of the Latin-1 Supplement's U+00A1.. characters the old fixed table would show.
        plan.Entries[0].Symbol.Should().Be(char.ConvertFromUtf32(0xF020));
        plan.Entries[0].CodeText.Should().Be("F020");
    }

    [Fact]
    public void PlanSymbolList_SearchWithinSymbolFont_FiltersTheFontsOwnCatalog()
    {
        var plan = SymbolPickerCatalogPlanner.PlanSymbolList(
            null,
            "0x41",
            selectedSymbol: null,
            fontName: "Webdings");

        plan.HasResults.Should().BeTrue();
        plan.Entries.Should().ContainSingle();
        plan.Entries[0].Subset.Should().Be("Webdings");
        plan.Entries[0].Symbol.Should().Be(char.ConvertFromUtf32(0xF041));
    }

    [Fact]
    public void GetSymbolFontEntries_BuildsFullPrivateUseRangeForRecognizedFont()
    {
        var entries = SymbolPickerCatalogPlanner.GetSymbolFontEntries("Wingdings 2");

        entries.Should().HaveCount(224);
        entries.Should().Contain(new SymbolPickerCatalogEntry(
            char.ConvertFromUtf32(0xF0FF),
            "Wingdings 2 Character 0xFF",
            "Wingdings 2",
            "F0FF"));
    }

    [Fact]
    public void GetSymbolFontEntries_ReturnsEmptyForNonSymbolFont()
    {
        SymbolPickerCatalogPlanner.GetSymbolFontEntries("Calibri").Should().BeEmpty();
        SymbolPickerCatalogPlanner.GetSymbolFontEntries(null).Should().BeEmpty();
    }

    // No-regression sibling: a non-symbol (or absent) font must leave the pre-existing
    // Unicode subset/search catalog behavior completely unchanged.
    [Fact]
    public void PlanSymbolList_WithoutSymbolFont_StillUsesUnicodeSubsetCatalog()
    {
        var withNoFont = SymbolPickerCatalogPlanner.PlanSymbolList(
            "Greek and Coptic",
            "",
            "π");

        var withRegularFont = SymbolPickerCatalogPlanner.PlanSymbolList(
            "Greek and Coptic",
            "",
            "π",
            fontName: "Calibri");

        withNoFont.Entries.Should().Equal(withRegularFont.Entries);
        withRegularFont.HasResults.Should().BeTrue();
        withRegularFont.Entries.Select(entry => entry.Symbol).Should().Contain("π");
        withRegularFont.SelectedEntry.Should().Be(new SymbolPickerCatalogEntry(
            "π",
            "Greek Small Letter Pi",
            "Greek and Coptic",
            "03C0"));
    }
}
