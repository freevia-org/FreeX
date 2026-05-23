using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_ExposesKeyboardAccessKeysForInsertAndCancel()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Content = \"_Insert\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeSymbolSelectionAffordances()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("Content = \"_Font:\"");
        source.Should().Contain("Content = \"_Subset:\"");
        source.Should().Contain("Recently used symbols");
        source.Should().Contain("Character code:");
        source.Should().Contain("from: Unicode (hex)");
        source.Should().Contain("UniformGrid");
    }

    [Fact]
    public void Dialog_SelectsSymbolsBeforeExplicitInsert()
    {
        var source = ReadSymbolPickerDialogSources();

        source.Should().Contain("void SelectSymbol(char value)");
        source.Should().Contain("SelectedChar = value");
        source.Should().Contain("insert.Click += (_, _) =>");
        source.Should().Contain("DialogResult = true");
        source.Should().NotContain("SelectedChar = c;\r\n                    DialogResult = true");
    }

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

        source.Should().Contain("Header = \"Symbols\"");
        source.Should().Contain("Header = \"Special Characters\"");
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

    private static string ReadSymbolPickerDialogSources() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.Catalog.cs"));
}
